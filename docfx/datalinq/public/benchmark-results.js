const PROFILE_COLORS = new Map([
  ['default', '#2563eb'],
  ['heavy', '#b45309'],
  ['smoke', '#64748b']
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
    case 'CRUD workflow':
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

function numericValue(point, selector) {
  const value = point?.[selector]
  return value === null || value === undefined || Number.isNaN(value) ? null : value
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
    const validMeanPoints = groupPoints.filter(point => numericValue(point, 'meanMicroseconds') !== null)
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
      runCount: validMeanPoints.length,
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

function renderProfile(profile) {
  return `<span class="benchmark-profile benchmark-profile-${escapeHtml(profile)}">${escapeHtml(profile)}</span>`
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
    ['Operations per invoke', point.operationsPerInvoke],
    ['Queries entity/scalar', telemetry ? `${formatMetric(telemetry.EntityQueriesPerOperation)} / ${formatMetric(telemetry.ScalarQueriesPerOperation)}` : '-'],
    ['Transactions start/commit/rollback', telemetry ? `${formatMetric(telemetry.TransactionStartsPerOperation)} / ${formatMetric(telemetry.TransactionCommitsPerOperation)} / ${formatMetric(telemetry.TransactionRollbacksPerOperation)}` : '-'],
    ['Mutations insert/update/delete/rows', telemetry ? `${formatMetric(telemetry.MutationInsertsPerOperation)} / ${formatMetric(telemetry.MutationUpdatesPerOperation)} / ${formatMetric(telemetry.MutationDeletesPerOperation)} / ${formatMetric(telemetry.MutationAffectedRowsPerOperation)}` : '-'],
    ['Row cache hit/miss/store', telemetry ? `${formatMetric(telemetry.RowCacheHitsPerOperation)} / ${formatMetric(telemetry.RowCacheMissesPerOperation)} / ${formatMetric(telemetry.RowCacheStoresPerOperation)}` : '-'],
    ['Database rows/materializations', telemetry ? `${formatMetric(telemetry.DatabaseRowsPerOperation)} / ${formatMetric(telemetry.MaterializationsPerOperation)}` : '-'],
    ['Relation hit/load', telemetry ? `${formatMetric(telemetry.RelationHitsPerOperation)} / ${formatMetric(telemetry.RelationLoadsPerOperation)}` : '-']
  ]

  const items = rows.map(([label, value]) => `
    <div>
      <dt>${escapeHtml(label)}</dt>
      <dd>${escapeHtml(value ?? '-')}</dd>
    </div>
  `).join('')

  return `
    <details class="benchmark-details">
      <summary>Telemetry deltas</summary>
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

function renderTrendTable(points) {
  const rows = buildTrendRows(points)
  if (rows.length === 0) {
    return '<p class="benchmark-muted">No benchmark history rows match the current filters.</p>'
  }

  let currentCategory = null
  const body = rows.map(row => {
    const categoryHeader = row.category !== currentCategory
      ? `<tr class="benchmark-group-row"><th colspan="13">${escapeHtml(CATEGORY_LABELS.get(row.category) ?? row.category)}</th></tr>`
      : ''
    currentCategory = row.category

    return `
      ${categoryHeader}
      <tr>
        <td>${escapeHtml(row.method)}</td>
        <td>${escapeHtml(row.providerName)}</td>
        <td>${renderProfile(row.profile)}</td>
        <td>${formatDateLabel(row.latest.generatedAtUtc)}</td>
        <td>${formatMicroseconds(row.latest.meanMicroseconds)}</td>
        <td>${formatBytes(row.latest.allocatedBytes)}</td>
        <td>${formatUnsignedPercent(row.latest.uncertaintyPercent)}</td>
        <td>${formatPercent(row.meanDeltaPrevious)}</td>
        <td>${formatPercent(row.meanDelta7)}</td>
        <td>${formatPercent(row.meanDelta30)}</td>
        <td>${formatPercent(row.slope)}</td>
        <td>${escapeHtml(shortCommit(row.latest.commit))}</td>
        <td>${renderStatus(row.status)}</td>
      </tr>
      <tr class="benchmark-detail-row">
        <td colspan="13">
          <div class="benchmark-detail-content">
            <span>${escapeHtml(row.runCount)} same-profile runs. Last run ${escapeHtml(formatDateTime(row.latest.generatedAtUtc))} on ${escapeHtml(row.latest.runnerOs ?? 'unknown runner')}.</span>
            ${renderTelemetryDetails(row.latest)}
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
          <th>Provider</th>
          <th>Profile</th>
          <th>Last run</th>
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
    `${point.method} (${point.providerName}, ${point.profile})`,
    `run: ${formatDateTime(point.generatedAtUtc)}`,
    `commit: ${shortCommit(point.commit)}`,
    `${selector === 'allocatedBytes' ? 'allocated' : 'mean'}: ${formatMetricValue(point[selector], selector)}`,
    `uncertainty: ${formatUnsignedPercent(point.uncertaintyPercent)}`,
    `telemetry: ${formatTelemetrySummary(point.telemetryDelta)}`
  ].join('\n')
}

function renderTrendChart(points, selector) {
  const validPoints = points.filter(point => numericValue(point, selector) !== null)
  if (validPoints.length < 2) {
    return '<div class="benchmark-empty-chart">Need at least two runs</div>'
  }

  const width = 520
  const height = 190
  const paddingLeft = 68
  const paddingRight = 20
  const paddingTop = 22
  const paddingBottom = 34
  const values = validPoints.map(point => numericValue(point, selector))
  const min = Math.min(...values)
  const max = Math.max(...values)
  const range = Math.max(max - min, 1)
  const steps = Math.max(validPoints.length - 1, 1)
  const chartWidth = width - paddingLeft - paddingRight
  const chartHeight = height - paddingTop - paddingBottom
  const midY = paddingTop + chartHeight / 2
  const midValue = min + range / 2

  const coordinates = validPoints.map((point, index) => {
    const value = numericValue(point, selector)
    const x = paddingLeft + (index / steps) * chartWidth
    const y = height - paddingBottom - ((value - min) / range) * chartHeight
    return { point, value, x, y }
  })

  const lines = [...groupBy(coordinates, item => item.point.profile).entries()]
    .map(([profile, items]) => {
      if (items.length < 2) {
        return ''
      }

      const color = PROFILE_COLORS.get(profile) ?? '#334155'
      const text = items.map(item => `${item.x.toFixed(1)},${item.y.toFixed(1)}`).join(' ')
      return `<polyline fill="none" stroke="${color}" stroke-width="2.6" points="${text}"></polyline>`
    })
    .join('')

  const circles = coordinates.map(item => {
    const color = PROFILE_COLORS.get(item.point.profile) ?? '#334155'
    return `
      <circle
        class="benchmark-chart-point"
        cx="${item.x.toFixed(1)}"
        cy="${item.y.toFixed(1)}"
        r="4.2"
        fill="${color}"
        tabindex="0"
        aria-label="${escapeHtml(renderPointTitle(item.point, selector))}">
        <title>${escapeHtml(renderPointTitle(item.point, selector))}</title>
      </circle>
    `
  }).join('')

  return `
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
      ${lines}
      ${circles}
    </svg>
  `
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
    const latest = groupPoints[groupPoints.length - 1]
    const profiles = [...new Set(groupPoints.map(point => point.profile))]
    const legend = profiles.map(renderProfile).join('')

    return `
      <section class="benchmark-card">
        <header class="benchmark-card-header">
          <div>
            <h3>${escapeHtml(latest.method)} <span>${escapeHtml(latest.providerName)}</span></h3>
            <p>${groupPoints.length} published runs across ${escapeHtml(profiles.join(', '))}</p>
          </div>
          <div class="benchmark-profile-list">${legend}</div>
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

async function renderBenchmarkResults() {
  const root = document.getElementById('benchmark-results-root')
  if (!root) {
    return
  }

  root.innerHTML = '<p class="benchmark-muted">Loading benchmark history...</p>'

  try {
    const historyUrl = root.dataset.historyUrl
    const latestUrl = root.dataset.latestUrl
    const comparisonUrl = root.dataset.comparisonUrl
    const allowedProviders = parseListFilter(root, 'providerFilter')
    const allowedMethods = parseListFilter(root, 'methodFilter')

    const [rawHistory, rawLatest, rawComparison] = await Promise.all([
      fetchJson(historyUrl),
      latestUrl ? fetchJson(latestUrl).catch(() => null) : Promise.resolve(null),
      comparisonUrl ? fetchJson(comparisonUrl).catch(() => null) : Promise.resolve(null)
    ])

    const history = filterHistory(rawHistory, allowedProviders, allowedMethods)
    const points = flattenHistory(history)
    const latestPoint = points[points.length - 1]
    const profiles = [...new Set(points.map(point => point.profile))].sort()
    const providerScope = allowedProviders.length === 0 ? 'all published providers' : allowedProviders.join(', ')
    const comparisonStatus = rawComparison?.WarningCount > 0
      ? `${rawComparison.WarningCount} comparison warnings`
      : rawComparison
        ? 'latest same-profile comparison clean'
        : 'no same-profile comparison yet'

    root.innerHTML = `
      <section class="benchmark-overview">
        <div class="benchmark-overview-item">
          <strong>Latest run</strong>
          <span>${escapeHtml(formatDateTime(rawLatest?.GeneratedAtUtc ?? latestPoint?.generatedAtUtc))}</span>
        </div>
        <div class="benchmark-overview-item">
          <strong>Commit</strong>
          <span>${escapeHtml(shortCommit(rawLatest?.Metadata?.Commit ?? latestPoint?.commit))}</span>
        </div>
        <div class="benchmark-overview-item">
          <strong>Profiles</strong>
          <span>${profiles.map(renderProfile).join('')}</span>
        </div>
        <div class="benchmark-overview-item">
          <strong>Provider scope</strong>
          <span>${escapeHtml(providerScope)}</span>
        </div>
        <div class="benchmark-overview-item">
          <strong>Comparison</strong>
          <span>${escapeHtml(comparisonStatus)}</span>
        </div>
      </section>
      <h2>Trend Summary</h2>
      ${renderTrendTable(points)}
      <h2>Charts</h2>
      <div class="benchmark-card-list">
        ${renderTrendCards(points)}
      </div>
    `
  } catch (error) {
    root.innerHTML = `<p class="benchmark-error">Failed to load published benchmark history. ${escapeHtml(error?.message ?? String(error))}</p>`
  }
}

if (typeof window !== 'undefined') {
  window.addEventListener('DOMContentLoaded', () => {
    renderBenchmarkResults()
  })
}
