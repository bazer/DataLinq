const PROFILE_COLORS = new Map([
  ['default', '#2563eb'],
  ['heavy', '#b45309'],
  ['smoke', '#64748b']
])

const PROFILE_DETAILS = new Map([
  ['default', {
    label: 'default',
    summary: 'Default uses BenchmarkDotNet ShortRun. It is the normal push/manual CI profile: fast enough to publish often, but noisier than the nightly lane.'
  }],
  ['heavy', {
    label: 'heavy',
    summary: 'Heavy uses BenchmarkDotNet MediumRun. It is the scheduled profile: slower, more repeated, and better for deciding whether a movement is real.'
  }],
  ['smoke', {
    label: 'smoke',
    summary: 'Smoke uses BenchmarkDotNet Dry. It exists for local plumbing checks and is not a performance signal.'
  }]
])

const STATUS_LABELS = new Map([
  ['stable', 'stable'],
  ['improved', 'improved'],
  ['warning', 'warning'],
  ['noisy', 'noisy'],
  ['missing', 'missing'],
  ['profile-mismatch', 'profile']
])

const CATEGORY_LABELS = new Map([
  ['startup', 'Startup'],
  ['read-hotpath', 'Read Hot Paths'],
  ['relation-traversal', 'Relation Traversal'],
  ['mutation', 'Mutation'],
  ['macro-readwrite', 'Macro Read/Write'],
  ['macro-bulk', 'Macro Bulk'],
  ['phase2-watch', 'Phase 2 Watch'],
  ['phase3-query-hotpath', 'Phase 3 Query Hot Path'],
  ['other', 'Other']
])

const PROFILE_ORDER = ['default', 'heavy', 'smoke']
const OUTLIER_PERCENT_THRESHOLD = 35
const OUTLIER_NEIGHBOR_SPREAD_THRESHOLD = 15
const SMOOTHING_WINDOW = 3

function formatNumber(value, digits = 1) {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-'
  }

  return new Intl.NumberFormat('en-US', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits
  }).format(value)
}

function formatMicroseconds(value) {
  return value === null || value === undefined ? '-' : `${formatNumber(value, 1)} μs`
}

function formatBytes(value) {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-'
  }

  if (value >= 1024 * 1024) {
    return `${formatNumber(value / (1024 * 1024), 2)} MB`
  }

  if (value >= 1024) {
    return `${formatNumber(value / 1024, 2)} KB`
  }

  return `${formatNumber(value, 0)} B`
}

function formatPercent(value, digits = 1) {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-'
  }

  return `${value >= 0 ? '+' : ''}${formatNumber(value, digits)}%`
}

function formatUnsignedPercent(value, digits = 1) {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-'
  }

  return `${formatNumber(value, digits)}%`
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;')
}

function formatDateLabel(value) {
  if (!value) {
    return '-'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return String(value)
  }

  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric'
  }).format(date)
}

function formatDateTime(value) {
  if (!value) {
    return '-'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return String(value)
  }

  return new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  }).format(date)
}

function shortCommit(value) {
  return value ? String(value).slice(0, 7) : 'unknown'
}

function commitUrl(value, template) {
  if (!value || !template) {
    return null
  }

  return template.replace('{commit}', encodeURIComponent(value))
}

function renderCommitLink(value, template) {
  const label = shortCommit(value)
  const url = commitUrl(value, template)
  return url
    ? `<a href="${escapeHtml(url)}">${escapeHtml(label)}</a>`
    : escapeHtml(label)
}

async function fetchJson(url) {
  const separator = url.includes('?') ? '&' : '?'
  const response = await fetch(`${url}${separator}ts=${Date.now()}`, { cache: 'no-store' })
  if (!response.ok) {
    throw new Error(`Failed to fetch ${url}: ${response.status}`)
  }

  return await response.json()
}

function parseListFilter(root, name) {
  const filter = root?.dataset?.[name] ?? ''
  return filter
    .split(',')
    .map(value => value.trim())
    .filter(Boolean)
}

function isAllowed(value, allowedValues) {
  return allowedValues.length === 0 || allowedValues.includes(value)
}

function filterHistory(history, allowedProviders, allowedMethods) {
  if (!history?.Runs?.length) {
    return history
  }

  return {
    ...history,
    Runs: history.Runs
      .map(run => ({
        ...run,
        Rows: (run.Rows ?? []).filter(row =>
          isAllowed(row.ProviderName, allowedProviders) &&
          isAllowed(row.Method, allowedMethods))
      }))
      .filter(run => (run.Rows ?? []).length > 0)
  }
}

function getProfile(run) {
  return run?.Metadata?.Profile || 'default'
}

function getCategory(row) {
  return row.Category || row.TrackingGroup || inferCategory(row.Method)
}

function inferCategory(method) {
  switch (method) {
    case 'Provider initialization':
    case 'Startup primary-key fetch':
      return 'startup'
    case 'Cold primary-key fetch':
    case 'Warm primary-key fetch':
    case 'Repeated non-PK equality fetch':
    case 'Repeated IN predicate fetch':
    case 'Repeated scalar Any':
      return 'read-hotpath'
    case 'Cold relation traversal':
    case 'Warm relation traversal':
      return 'relation-traversal'
    case 'Insert employees':
    case 'Update employees':
    case 'Delete employees':
      return 'mutation'
    case 'CRUD workflow small':
      return 'macro-readwrite'
    case 'CRUD workflow':
    case 'CRUD workflow batch':
      return 'macro-bulk'
    default:
      return 'other'
  }
}

function flattenHistory(history) {
  const points = []

  for (const run of history?.Runs ?? []) {
    const profile = getProfile(run)
    for (const row of run.Rows ?? []) {
      points.push({
        runId: run.RunId,
        generatedAtUtc: run.GeneratedAtUtc,
        metadata: run.Metadata ?? {},
        commit: run.Metadata?.Commit ?? null,
        branch: run.Metadata?.Branch ?? null,
        runnerOs: run.Metadata?.RunnerOs ?? null,
        profile,
        method: row.Method,
        providerName: row.ProviderName,
        category: getCategory(row),
        meanMicroseconds: row.MeanMicroseconds,
        medianMicroseconds: row.MedianMicroseconds,
        errorMicroseconds: row.ErrorMicroseconds,
        stdDevMicroseconds: row.StdDevMicroseconds,
        allocatedBytes: row.AllocatedBytes,
        uncertaintyPercent: row.UncertaintyPercent ?? row.NoisePercent,
        stdDevPercent: row.StdDevPercent,
        operationsPerInvoke: row.OperationsPerInvoke ?? row.TelemetryDelta?.OperationsPerInvoke,
        telemetryDelta: row.TelemetryDelta ?? null
      })
    }
  }

  points.sort((left, right) => String(left.generatedAtUtc).localeCompare(String(right.generatedAtUtc)))
  return points
}

function groupBy(points, keySelector) {
  const groups = new Map()
  for (const point of points) {
    const key = keySelector(point)
    const group = groups.get(key) ?? []
    group.push(point)
    groups.set(key, group)
  }

  return groups
}

function profileRank(profile) {
  const index = PROFILE_ORDER.indexOf(profile)
  return index < 0 ? PROFILE_ORDER.length : index
}

function getAvailableProfiles(points) {
  return [...new Set(points.map(point => point.profile))]
    .sort((left, right) => profileRank(left) - profileRank(right) || left.localeCompare(right))
}

function chooseProfile(root, profiles) {
  const requested = root?.dataset?.selectedProfile || root?.dataset?.profile || ''
  if (requested && profiles.includes(requested)) {
    return requested
  }

  return profiles.includes('default') ? 'default' : profiles[0]
}

function numericValue(point, selector) {
  const value = point?.[selector]
  return value === null || value === undefined || Number.isNaN(value) ? null : value
}

function finiteValues(points, selector) {
  return points.filter(point => numericValue(point, selector) !== null)
}

function median(values) {
  const sorted = values
    .filter(value => value !== null && value !== undefined && !Number.isNaN(value))
    .sort((left, right) => left - right)

  if (sorted.length === 0) {
    return null
  }

  const middle = Math.floor(sorted.length / 2)
  return sorted.length % 2 === 0
    ? (sorted[middle - 1] + sorted[middle]) / 2
    : sorted[middle]
}

function deltaPercent(baseline, candidate) {
  if (baseline === null || baseline === undefined || candidate === null || candidate === undefined || baseline === 0) {
    return null
  }

  return ((candidate - baseline) / baseline) * 100
}

function isInteriorOutlier(previousValue, currentValue, nextValue) {
  const isLocalSpike = currentValue > previousValue && currentValue > nextValue
  const isLocalDrop = currentValue < previousValue && currentValue < nextValue
  if (!isLocalSpike && !isLocalDrop) {
    return false
  }

  const neighborAverage = (previousValue + nextValue) / 2
  const currentDelta = Math.abs(deltaPercent(neighborAverage, currentValue) ?? 0)
  const neighborSpread = Math.abs(deltaPercent(previousValue, nextValue) ?? 0)

  return currentDelta >= OUTLIER_PERCENT_THRESHOLD &&
    neighborSpread <= OUTLIER_NEIGHBOR_SPREAD_THRESHOLD
}

function withoutInteriorOutliers(points, selector) {
  const valid = finiteValues(points, selector)
  if (valid.length < 5) {
    return { points: valid, skipped: 0 }
  }

  const skipped = new Set()
  for (let index = 1; index < valid.length - 1; index += 1) {
    const previousValue = numericValue(valid[index - 1], selector)
    const currentValue = numericValue(valid[index], selector)
    const nextValue = numericValue(valid[index + 1], selector)

    if (isInteriorOutlier(previousValue, currentValue, nextValue)) {
      skipped.add(valid[index])
    }
  }

  return {
    points: valid.filter(point => !skipped.has(point)),
    skipped: skipped.size
  }
}

function smoothSeries(points, selector) {
  const radius = Math.floor(SMOOTHING_WINDOW / 2)
  return points.map((point, index) => {
    const start = Math.max(0, index - radius)
    const end = Math.min(points.length, index + radius + 1)
    const windowValues = points
      .slice(start, end)
      .map(item => numericValue(item, selector))
      .filter(value => value !== null)
    const smoothedValue = windowValues.reduce((sum, value) => sum + value, 0) / windowValues.length
    return { point, smoothedValue }
  })
}

function getRecentSlopePercent(points, selector) {
  const valid = points.filter(point => numericValue(point, selector) !== null)
  if (valid.length < 2) {
    return null
  }

  const window = valid.slice(-7)
  const first = numericValue(window[0], selector)
  const latest = numericValue(window[window.length - 1], selector)
  return deltaPercent(first, latest)
}

function buildTrendRows(points) {
  const groups = groupBy(points, point => `${point.method}__${point.providerName}__${point.profile}`)
  const rows = []

  for (const groupPoints of groups.values()) {
    const meanSeries = withoutInteriorOutliers(groupPoints, 'meanMicroseconds')
    const validMeanPoints = meanSeries.points
    if (validMeanPoints.length === 0) {
      continue
    }

    const latest = validMeanPoints[validMeanPoints.length - 1]
    const previous = validMeanPoints.length > 1 ? validMeanPoints[validMeanPoints.length - 2] : null
    const priorPoints = validMeanPoints.slice(0, -1)
    const previousMean = previous ? numericValue(previous, 'meanMicroseconds') : null
    const latestMean = numericValue(latest, 'meanMicroseconds')
    const previousAllocated = previous ? numericValue(previous, 'allocatedBytes') : null
    const latestAllocated = numericValue(latest, 'allocatedBytes')
    const median7 = median(priorPoints.slice(-7).map(point => point.meanMicroseconds))
    const median30 = median(priorPoints.slice(-30).map(point => point.meanMicroseconds))
    const meanDeltaPrevious = deltaPercent(previousMean, latestMean)
    const allocationDeltaPrevious = deltaPercent(previousAllocated, latestAllocated)
    const meanDelta7 = deltaPercent(median7, latestMean)
    const meanDelta30 = deltaPercent(median30, latestMean)
    const slope = getRecentSlopePercent(validMeanPoints, 'meanMicroseconds')
    const uncertainty = latest.uncertaintyPercent
    const primaryDelta = meanDelta7 ?? meanDeltaPrevious
    const primaryAllocationDelta = allocationDeltaPrevious
    const status = classifyStatus(primaryDelta, primaryAllocationDelta, uncertainty)

    rows.push({
      key: `${latest.method}__${latest.providerName}__${latest.profile}`,
      method: latest.method,
      providerName: latest.providerName,
      profile: latest.profile,
      category: latest.category,
      latest,
      rawRunCount: finiteValues(groupPoints, 'meanMicroseconds').length,
      runCount: validMeanPoints.length,
      skippedOutlierCount: meanSeries.skipped,
      meanDeltaPrevious,
      allocationDeltaPrevious,
      meanDelta7,
      meanDelta30,
      slope,
      status
    })
  }

  rows.sort((left, right) =>
    categoryRank(left.category) - categoryRank(right.category) ||
    left.method.localeCompare(right.method) ||
    left.providerName.localeCompare(right.providerName) ||
    left.profile.localeCompare(right.profile))

  return rows
}

function classifyStatus(meanDelta, allocatedDelta, uncertainty) {
  if (uncertainty !== null && uncertainty !== undefined && uncertainty >= 20) {
    return 'noisy'
  }

  if ((meanDelta ?? 0) >= 10 || (allocatedDelta ?? 0) >= 10) {
    return 'warning'
  }

  if ((meanDelta ?? 0) <= -10 || (allocatedDelta ?? 0) <= -10) {
    return 'improved'
  }

  return 'stable'
}

function categoryRank(category) {
  const order = [
    'startup',
    'read-hotpath',
    'relation-traversal',
    'mutation',
    'macro-readwrite',
    'macro-bulk',
    'phase2-watch',
    'phase3-query-hotpath',
    'other'
  ]
  const index = order.indexOf(category)
  return index < 0 ? order.length : index
}

function renderStatus(status) {
  const label = STATUS_LABELS.get(status) ?? status
  return `<span class="benchmark-status benchmark-status-${escapeHtml(status)}">${escapeHtml(label)}</span>`
}

function formatMetricValue(value, selector) {
  return selector === 'allocatedBytes' ? formatBytes(value) : formatMicroseconds(value)
}

function formatTelemetrySummary(telemetry) {
  if (!telemetry) {
    return '-'
  }

  const parts = []
  if (hasSignal(telemetry.EntityQueriesPerOperation, telemetry.ScalarQueriesPerOperation)) {
    parts.push(`Q ${formatMetric(telemetry.EntityQueriesPerOperation)}/${formatMetric(telemetry.ScalarQueriesPerOperation)}`)
  }
  if (hasSignal(telemetry.TransactionStartsPerOperation, telemetry.TransactionCommitsPerOperation, telemetry.TransactionRollbacksPerOperation)) {
    parts.push(`Tx ${formatMetric(telemetry.TransactionStartsPerOperation)}/${formatMetric(telemetry.TransactionCommitsPerOperation)}/${formatMetric(telemetry.TransactionRollbacksPerOperation)}`)
  }
  if (hasSignal(telemetry.MutationInsertsPerOperation, telemetry.MutationUpdatesPerOperation, telemetry.MutationDeletesPerOperation, telemetry.MutationAffectedRowsPerOperation)) {
    parts.push(`Mut ${formatMetric(telemetry.MutationInsertsPerOperation)}/${formatMetric(telemetry.MutationUpdatesPerOperation)}/${formatMetric(telemetry.MutationDeletesPerOperation)} rows ${formatMetric(telemetry.MutationAffectedRowsPerOperation)}`)
  }
  if (hasSignal(telemetry.RowCacheHitsPerOperation, telemetry.RowCacheMissesPerOperation, telemetry.RowCacheStoresPerOperation)) {
    parts.push(`Row ${formatMetric(telemetry.RowCacheHitsPerOperation)}/${formatMetric(telemetry.RowCacheMissesPerOperation)}/${formatMetric(telemetry.RowCacheStoresPerOperation)}`)
  }
  if (hasSignal(telemetry.RelationHitsPerOperation, telemetry.RelationLoadsPerOperation)) {
    parts.push(`Rel ${formatMetric(telemetry.RelationHitsPerOperation)}/${formatMetric(telemetry.RelationLoadsPerOperation)}`)
  }
  if (hasSignal(telemetry.DatabaseRowsPerOperation)) {
    parts.push(`DB ${formatMetric(telemetry.DatabaseRowsPerOperation)}`)
  }
  if (hasSignal(telemetry.MaterializationsPerOperation)) {
    parts.push(`Mat ${formatMetric(telemetry.MaterializationsPerOperation)}`)
  }

  return parts.length === 0 ? '-' : parts.join('  ')
}

function renderTelemetryDetails(point) {
  const telemetry = point.telemetryDelta
  const rows = [
    ['Ops/invoke', point.operationsPerInvoke],
    ['Queries', telemetry ? `${formatMetric(telemetry.EntityQueriesPerOperation)} / ${formatMetric(telemetry.ScalarQueriesPerOperation)}` : '-'],
    ['Transactions', telemetry ? `${formatMetric(telemetry.TransactionStartsPerOperation)} / ${formatMetric(telemetry.TransactionCommitsPerOperation)} / ${formatMetric(telemetry.TransactionRollbacksPerOperation)}` : '-'],
    ['Mutations', telemetry ? `${formatMetric(telemetry.MutationInsertsPerOperation)} / ${formatMetric(telemetry.MutationUpdatesPerOperation)} / ${formatMetric(telemetry.MutationDeletesPerOperation)} / ${formatMetric(telemetry.MutationAffectedRowsPerOperation)}` : '-'],
    ['Row cache', telemetry ? `${formatMetric(telemetry.RowCacheHitsPerOperation)} / ${formatMetric(telemetry.RowCacheMissesPerOperation)} / ${formatMetric(telemetry.RowCacheStoresPerOperation)}` : '-'],
    ['Rows/materialized', telemetry ? `${formatMetric(telemetry.DatabaseRowsPerOperation)} / ${formatMetric(telemetry.MaterializationsPerOperation)}` : '-'],
    ['Relations', telemetry ? `${formatMetric(telemetry.RelationHitsPerOperation)} / ${formatMetric(telemetry.RelationLoadsPerOperation)}` : '-']
  ]

  const items = rows.map(([label, value]) => `
    <div>
      <dt>${escapeHtml(label)}</dt>
      <dd>${escapeHtml(value ?? '-')}</dd>
    </div>
  `).join('')

  return `
    <details class="benchmark-details">
      <summary>Telemetry deltas &gt;</summary>
      <dl class="benchmark-telemetry-grid">${items}</dl>
    </details>
  `
}

function hasSignal(...values) {
  return values.some(value => value !== null && value !== undefined && Math.abs(value) >= 0.0001)
}

function formatMetric(value) {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-'
  }

  const absolute = Math.abs(value)
  if (absolute < 0.0001) {
    return '0'
  }

  if (absolute < 0.01) {
    return '<0.01'
  }

  const rounded = Math.round(value)
  if (absolute >= 0.95 && Math.abs(value - rounded) < 0.05) {
    return String(rounded)
  }

  return absolute < 0.1 ? formatNumber(value, 2) : formatNumber(value, 1)
}

function renderTrendTable(points, commitUrlTemplate) {
  const rows = buildTrendRows(points)
  if (rows.length === 0) {
    return '<p class="benchmark-muted">No benchmark history rows match the current filters.</p>'
  }

  let currentCategory = null
  const body = rows.map(row => {
    const categoryHeader = row.category !== currentCategory
      ? `<tr class="benchmark-group-row"><th colspan="10">${escapeHtml(CATEGORY_LABELS.get(row.category) ?? row.category)}</th></tr>`
      : ''
    currentCategory = row.category
    const outlierText = row.skippedOutlierCount > 0
      ? ` ${row.skippedOutlierCount} one-off outlier${row.skippedOutlierCount === 1 ? '' : 's'} skipped.`
      : ''

    return `
      ${categoryHeader}
      <tr class="benchmark-data-row">
        <td class="benchmark-scenario-name">${escapeHtml(row.method)}</td>
        <td class="benchmark-number">${formatMicroseconds(row.latest.meanMicroseconds)}</td>
        <td class="benchmark-number">${formatBytes(row.latest.allocatedBytes)}</td>
        <td class="benchmark-number">${formatUnsignedPercent(row.latest.uncertaintyPercent)}</td>
        <td class="benchmark-number">${formatPercent(row.meanDeltaPrevious)}</td>
        <td class="benchmark-number">${formatPercent(row.meanDelta7)}</td>
        <td class="benchmark-number">${formatPercent(row.meanDelta30)}</td>
        <td class="benchmark-number">${formatPercent(row.slope)}</td>
        <td>${renderCommitLink(row.latest.commit, commitUrlTemplate)}</td>
        <td>${renderStatus(row.status)}</td>
      </tr>
      <tr class="benchmark-detail-row">
        <td colspan="10">
          <div class="benchmark-detail-content">
            <div class="benchmark-row-meta">
              <span>${escapeHtml(row.runCount)} ${escapeHtml(row.profile)} runs. Last run ${escapeHtml(formatDateTime(row.latest.generatedAtUtc))} on ${escapeHtml(row.latest.runnerOs ?? 'unknown runner')}.${escapeHtml(outlierText)}</span>
              ${renderTelemetryDetails(row.latest)}
            </div>
          </div>
        </td>
      </tr>
    `
  }).join('')

  return `
    <table class="benchmark-table benchmark-trend-table">
      <thead>
        <tr>
          <th>Scenario</th>
          <th>Mean</th>
          <th>Allocated</th>
          <th>Uncertainty</th>
          <th>Vs prev</th>
          <th>Vs 7 median</th>
          <th>Vs 30 median</th>
          <th>Slope</th>
          <th>Commit</th>
          <th>Status</th>
        </tr>
      </thead>
      <tbody>${body}</tbody>
    </table>
  `
}

function renderPointTitle(point, selector) {
  return [
    `${formatDateTime(point.generatedAtUtc)} (${shortCommit(point.commit)})`,
    `${selector === 'allocatedBytes' ? 'allocated' : 'mean'}: ${formatMetricValue(point[selector], selector)}`,
    `uncertainty: ${formatUnsignedPercent(point.uncertaintyPercent)}`,
    `telemetry: ${formatTelemetrySummary(point.telemetryDelta)}`
  ].join('\n')
}

function renderTooltipHtml(title) {
  const lines = escapeHtml(title).split('\n')
  if (lines.length === 0) {
    return ''
  }

  return [`<strong>${lines[0]}</strong>`, ...lines.slice(1)].join('<br>')
}

function buildSmoothPath(coordinates) {
  if (coordinates.length === 0) {
    return ''
  }

  if (coordinates.length === 1) {
    return `M ${coordinates[0].x.toFixed(1)} ${coordinates[0].y.toFixed(1)}`
  }

  let path = `M ${coordinates[0].x.toFixed(1)} ${coordinates[0].y.toFixed(1)}`
  for (let index = 0; index < coordinates.length - 1; index += 1) {
    const current = coordinates[index]
    const next = coordinates[index + 1]
    const previous = coordinates[index - 1] ?? current
    const afterNext = coordinates[index + 2] ?? next
    const cp1x = current.x + (next.x - previous.x) / 6
    const cp1y = current.y + (next.y - previous.y) / 6
    const cp2x = next.x - (afterNext.x - current.x) / 6
    const cp2y = next.y - (afterNext.y - current.y) / 6
    path += ` C ${cp1x.toFixed(1)} ${cp1y.toFixed(1)}, ${cp2x.toFixed(1)} ${cp2y.toFixed(1)}, ${next.x.toFixed(1)} ${next.y.toFixed(1)}`
  }

  return path
}

function renderTrendChart(points, selector) {
  const series = withoutInteriorOutliers(points, selector)
  const validPoints = series.points
  if (validPoints.length < 2) {
    return '<div class="benchmark-empty-chart">Need at least two runs</div>'
  }

  const width = 520
  const height = 190
  const paddingLeft = 68
  const paddingRight = 20
  const paddingTop = 22
  const paddingBottom = 34
  const smoothed = smoothSeries(validPoints, selector)
  const values = [
    ...validPoints.map(point => numericValue(point, selector)),
    ...smoothed.map(item => item.smoothedValue)
  ]
  const min = Math.min(...values)
  const max = Math.max(...values)
  const range = Math.max(max - min, 1)
  const steps = Math.max(validPoints.length - 1, 1)
  const chartWidth = width - paddingLeft - paddingRight
  const chartHeight = height - paddingTop - paddingBottom
  const midY = paddingTop + chartHeight / 2
  const midValue = min + range / 2

  const coordinates = smoothed.map(({ point, smoothedValue }, index) => {
    const x = paddingLeft + (index / steps) * chartWidth
    const y = height - paddingBottom - ((smoothedValue - min) / range) * chartHeight
    return { point, value: numericValue(point, selector), x, y }
  })

  const profile = validPoints[validPoints.length - 1].profile
  const color = PROFILE_COLORS.get(profile) ?? '#334155'
  const path = buildSmoothPath(coordinates)
  const hoverPoints = coordinates.map(item => ({
    x: Number(item.x.toFixed(1)),
    y: Number(item.y.toFixed(1)),
    title: renderPointTitle(item.point, selector)
  }))

  return `
    <div class="benchmark-chart-frame" tabindex="0" data-chart-points="${escapeHtml(JSON.stringify(hoverPoints))}">
      <svg class="benchmark-chart" viewBox="0 0 ${width} ${height}" preserveAspectRatio="none" role="img" aria-label="benchmark trend">
        <line x1="${paddingLeft}" y1="${paddingTop}" x2="${paddingLeft}" y2="${height - paddingBottom}" class="benchmark-axis"></line>
        <line x1="${paddingLeft}" y1="${height - paddingBottom}" x2="${width - paddingRight}" y2="${height - paddingBottom}" class="benchmark-axis"></line>
        <line x1="${paddingLeft}" y1="${paddingTop}" x2="${width - paddingRight}" y2="${paddingTop}" class="benchmark-grid"></line>
        <line x1="${paddingLeft}" y1="${midY}" x2="${width - paddingRight}" y2="${midY}" class="benchmark-grid"></line>
        <line x1="${paddingLeft}" y1="${height - paddingBottom}" x2="${width - paddingRight}" y2="${height - paddingBottom}" class="benchmark-grid"></line>
        <text x="${paddingLeft - 8}" y="${paddingTop + 4}" text-anchor="end" class="benchmark-axis-label">${escapeHtml(formatMetricValue(max, selector))}</text>
        <text x="${paddingLeft - 8}" y="${midY + 4}" text-anchor="end" class="benchmark-axis-label">${escapeHtml(formatMetricValue(midValue, selector))}</text>
        <text x="${paddingLeft - 8}" y="${height - paddingBottom + 4}" text-anchor="end" class="benchmark-axis-label">${escapeHtml(formatMetricValue(min, selector))}</text>
        <text x="${paddingLeft}" y="${height - 8}" text-anchor="start" class="benchmark-axis-label">${escapeHtml(formatDateLabel(validPoints[0]?.generatedAtUtc))}</text>
        <text x="${width - paddingRight}" y="${height - 8}" text-anchor="end" class="benchmark-axis-label">${escapeHtml(formatDateLabel(validPoints[validPoints.length - 1]?.generatedAtUtc))}</text>
        <path class="benchmark-chart-line" fill="none" stroke="${color}" d="${path}">
          <title>${escapeHtml(renderPointTitle(validPoints[validPoints.length - 1], selector))}</title>
        </path>
        <line class="benchmark-chart-crosshair" x1="0" y1="${paddingTop}" x2="0" y2="${height - paddingBottom}"></line>
        <circle class="benchmark-chart-hover-point" cx="0" cy="0" r="4.2" fill="${color}"></circle>
      </svg>
      <div class="benchmark-chart-tooltip" role="tooltip"></div>
      ${series.skipped > 0 ? `<p class="benchmark-chart-note">${series.skipped} one-off outlier${series.skipped === 1 ? '' : 's'} skipped.</p>` : ''}
    </div>
  `
}

function getChartPoints(frame) {
  if (!frame.chartPoints) {
    frame.chartPoints = JSON.parse(frame.dataset.chartPoints || '[]')
  }

  return frame.chartPoints
}

function showChartHover(frame, point) {
  const svg = frame.querySelector('svg')
  const tooltip = frame.querySelector('.benchmark-chart-tooltip')
  const crosshair = frame.querySelector('.benchmark-chart-crosshair')
  const hoverPoint = frame.querySelector('.benchmark-chart-hover-point')
  if (!svg || !tooltip || !crosshair || !hoverPoint || !point) {
    return
  }

  crosshair.setAttribute('x1', point.x)
  crosshair.setAttribute('x2', point.x)
  hoverPoint.setAttribute('cx', point.x)
  hoverPoint.setAttribute('cy', point.y)
  tooltip.innerHTML = renderTooltipHtml(point.title)
  tooltip.style.left = `${Math.min(92, Math.max(8, (point.x / 520) * 100))}%`
  tooltip.style.top = `${Math.min(84, Math.max(28, (point.y / 190) * 100))}%`
  frame.classList.add('benchmark-chart-frame-active')
}

function hideChartHover(frame) {
  frame.classList.remove('benchmark-chart-frame-active')
}

function installChartHover(root) {
  for (const frame of root.querySelectorAll('.benchmark-chart-frame')) {
    const points = getChartPoints(frame)
    if (points.length === 0) {
      continue
    }

    frame.addEventListener('pointermove', event => {
      const svg = frame.querySelector('svg')
      const bounds = svg?.getBoundingClientRect()
      if (!bounds || bounds.width === 0) {
        return
      }

      const chartX = ((event.clientX - bounds.left) / bounds.width) * 520
      const nearest = points.reduce((best, point) =>
        Math.abs(point.x - chartX) < Math.abs(best.x - chartX) ? point : best)
      showChartHover(frame, nearest)
    })

    frame.addEventListener('pointerleave', () => hideChartHover(frame))
    frame.addEventListener('focus', () => showChartHover(frame, points[points.length - 1]))
    frame.addEventListener('blur', () => hideChartHover(frame))
  }
}

function renderTrendCards(points) {
  const groups = [...groupBy(points, point => `${point.method}__${point.providerName}`).values()]
    .sort((left, right) =>
      categoryRank(left[left.length - 1].category) - categoryRank(right[right.length - 1].category) ||
      left[left.length - 1].method.localeCompare(right[right.length - 1].method) ||
      left[left.length - 1].providerName.localeCompare(right[right.length - 1].providerName))

  if (groups.length === 0) {
    return '<p class="benchmark-muted">No benchmark history is published yet.</p>'
  }

  return groups.map(groupPoints => {
    const meanSeries = withoutInteriorOutliers(groupPoints, 'meanMicroseconds')
    const latest = meanSeries.points[meanSeries.points.length - 1] ?? groupPoints[groupPoints.length - 1]
    const skipped = meanSeries.skipped
    const skippedText = skipped > 0
      ? `, ${skipped} one-off outlier${skipped === 1 ? '' : 's'} skipped`
      : ''

    return `
      <section class="benchmark-card">
        <header class="benchmark-card-header">
          <div>
            <h3>${escapeHtml(latest.method)}</h3>
            <p>${meanSeries.points.length} ${escapeHtml(latest.profile)} runs${skippedText}</p>
          </div>
        </header>
        <div class="benchmark-card-grid">
          <div>
            <h4>Mean Time</h4>
            ${renderTrendChart(groupPoints, 'meanMicroseconds')}
            <p class="benchmark-card-value">${formatMicroseconds(latest.meanMicroseconds)}</p>
          </div>
          <div>
            <h4>Allocated Bytes</h4>
            ${renderTrendChart(groupPoints, 'allocatedBytes')}
            <p class="benchmark-card-value">${formatBytes(latest.allocatedBytes)}</p>
          </div>
        </div>
      </section>
    `
  }).join('')
}

function renderProfileSwitch(profiles, selectedProfile) {
  const buttons = profiles.map(profile => `
    <button
      type="button"
      class="benchmark-profile-button benchmark-profile-button-${escapeHtml(profile)}"
      data-profile-select="${escapeHtml(profile)}"
      aria-pressed="${profile === selectedProfile ? 'true' : 'false'}">
      ${escapeHtml(PROFILE_DETAILS.get(profile)?.label ?? profile)}
    </button>
  `).join('')

  return `
    <div class="benchmark-profile-switch" role="group" aria-label="Benchmark profile">
      ${buttons}
    </div>
  `
}

function renderBenchmarkPage(root, points, selectedProfile, allowedProviders, commitUrlTemplate) {
  root.dataset.selectedProfile = selectedProfile
  const profilePoints = points.filter(point => point.profile === selectedProfile)
  const latestPoint = profilePoints[profilePoints.length - 1]
  const profiles = getAvailableProfiles(points)
  const providerScope = allowedProviders.length === 0 ? 'all published providers' : allowedProviders.join(', ')

  root.innerHTML = `
    <section class="benchmark-overview">
      <div class="benchmark-overview-item benchmark-overview-profile">
        <strong>Profile</strong>
        ${renderProfileSwitch(profiles, selectedProfile)}
      </div>
      <div class="benchmark-overview-item">
        <strong>Latest ${escapeHtml(selectedProfile)} run</strong>
        <span>${escapeHtml(formatDateTime(latestPoint?.generatedAtUtc))}</span>
      </div>
      <div class="benchmark-overview-item">
        <strong>Commit</strong>
        <span>${renderCommitLink(latestPoint?.commit, commitUrlTemplate)}</span>
      </div>
      <div class="benchmark-overview-item">
        <strong>Provider scope</strong>
        <span>${escapeHtml(providerScope)}</span>
      </div>
    </section>
    <h2>Trend Summary</h2>
    ${renderTrendTable(profilePoints, commitUrlTemplate)}
    <h2>Charts</h2>
    <div class="benchmark-card-list">
      ${renderTrendCards(profilePoints)}
    </div>
  `

  for (const button of root.querySelectorAll('[data-profile-select]')) {
    button.addEventListener('click', () => {
      renderBenchmarkPage(root, points, button.dataset.profileSelect, allowedProviders, commitUrlTemplate)
    })
  }

  installChartHover(root)
}

async function renderBenchmarkResults() {
  const root = document.getElementById('benchmark-results-root')
  if (!root) {
    return
  }

  root.innerHTML = '<p class="benchmark-muted">Loading benchmark history...</p>'

  try {
    const historyUrl = root.dataset.historyUrl
    const commitUrlTemplate = root.dataset.commitUrlTemplate
    const allowedProviders = parseListFilter(root, 'providerFilter')
    const allowedMethods = parseListFilter(root, 'methodFilter')

    const rawHistory = await fetchJson(historyUrl)

    const history = filterHistory(rawHistory, allowedProviders, allowedMethods)
    const points = flattenHistory(history)
    const profiles = getAvailableProfiles(points)
    if (profiles.length === 0) {
      root.innerHTML = '<p class="benchmark-muted">No benchmark history rows match the current filters.</p>'
      return
    }

    renderBenchmarkPage(
      root,
      points,
      chooseProfile(root, profiles),
      allowedProviders,
      commitUrlTemplate)
  } catch (error) {
    root.innerHTML = `<p class="benchmark-error">Failed to load published benchmark history. ${escapeHtml(error?.message ?? String(error))}</p>`
  }
}

if (typeof window !== 'undefined') {
  window.addEventListener('DOMContentLoaded', () => {
    renderBenchmarkResults()
  })
}
