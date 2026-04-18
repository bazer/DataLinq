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

function formatPercent(value) {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-'
  }

  return `${value >= 0 ? '+' : ''}${formatNumber(value, 1)}%`
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

async function fetchJson(url) {
  const separator = url.includes('?') ? '&' : '?'
  const response = await fetch(`${url}${separator}ts=${Date.now()}`, { cache: 'no-store' })
  if (!response.ok) {
    throw new Error(`Failed to fetch ${url}: ${response.status}`)
  }

  return await response.json()
}

function groupHistoryRuns(history) {
  const groups = new Map()

  for (const run of history?.Runs ?? []) {
    for (const row of run.Rows ?? []) {
      const key = `${row.Method}__${row.ProviderName}`
      const points = groups.get(key) ?? []
      points.push({
        runId: run.RunId,
        generatedAtUtc: run.GeneratedAtUtc,
        commit: run.Metadata?.Commit ?? null,
        method: row.Method,
        providerName: row.ProviderName,
        meanMicroseconds: row.MeanMicroseconds,
        allocatedBytes: row.AllocatedBytes,
        noisePercent: row.NoisePercent
      })
      groups.set(key, points)
    }
  }

  for (const points of groups.values()) {
    points.sort((left, right) => String(left.generatedAtUtc).localeCompare(String(right.generatedAtUtc)))
  }

  return groups
}

function parseProviderFilter(root) {
  const filter = root?.dataset?.providerFilter ?? ''
  return filter
    .split(',')
    .map(value => value.trim())
    .filter(Boolean)
}

function isAllowedProvider(providerName, allowedProviders) {
  return allowedProviders.length === 0 || allowedProviders.includes(providerName)
}

function filterHistory(history, allowedProviders) {
  if (!history?.Runs?.length) {
    return history
  }

  return {
    ...history,
    Runs: history.Runs
      .map(run => ({
        ...run,
        Rows: (run.Rows ?? []).filter(row => isAllowedProvider(row.ProviderName, allowedProviders))
      }))
      .filter(run => (run.Rows ?? []).length > 0)
  }
}

function filterLatest(latest, allowedProviders) {
  if (!latest?.Rows?.length) {
    return latest
  }

  return {
    ...latest,
    Rows: latest.Rows.filter(row => isAllowedProvider(row.ProviderName, allowedProviders))
  }
}

function filterComparison(comparison, allowedProviders) {
  if (!comparison?.Rows?.length) {
    return comparison
  }

  return {
    ...comparison,
    Rows: comparison.Rows.filter(row => isAllowedProvider(row.ProviderName, allowedProviders))
  }
}

function formatYAxisValue(value, selector) {
  if (selector === 'allocatedBytes') {
    return formatBytes(value)
  }

  if (selector === 'meanMicroseconds') {
    return formatMicroseconds(value)
  }

  return formatNumber(value, 1)
}

function renderTrendChart(points, selector, color) {
  const values = points
    .map(point => point[selector])
    .filter(value => value !== null && value !== undefined && !Number.isNaN(value))

  if (values.length < 2) {
    return '<div class="benchmark-empty-chart">Need at least two runs</div>'
  }

  const width = 420
  const height = 180
  const paddingLeft = 60
  const paddingRight = 18
  const paddingTop = 20
  const paddingBottom = 34
  const min = Math.min(...values)
  const max = Math.max(...values)
  const range = Math.max(max - min, 1)
  const steps = values.length - 1

  const chartWidth = width - paddingLeft - paddingRight
  const chartHeight = height - paddingTop - paddingBottom
  const pointsText = values
    .map((value, index) => {
      const x = paddingLeft + (index / steps) * chartWidth
      const normalized = (value - min) / range
      const y = height - paddingBottom - normalized * chartHeight
      return `${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')

  const firstDate = formatDateLabel(points[0]?.generatedAtUtc)
  const lastDate = formatDateLabel(points[points.length - 1]?.generatedAtUtc)
  const latestValue = values[values.length - 1]
  const latestX = paddingLeft + chartWidth
  const latestNormalized = (latestValue - min) / range
  const latestY = height - paddingBottom - latestNormalized * chartHeight
  const midY = paddingTop + chartHeight / 2
  const midValue = min + range / 2

  return `
    <svg class="benchmark-chart" viewBox="0 0 ${width} ${height}" preserveAspectRatio="none" role="img" aria-label="benchmark trend">
      <line x1="${paddingLeft}" y1="${paddingTop}" x2="${paddingLeft}" y2="${height - paddingBottom}" class="benchmark-axis"></line>
      <line x1="${paddingLeft}" y1="${height - paddingBottom}" x2="${width - paddingRight}" y2="${height - paddingBottom}" class="benchmark-axis"></line>
      <line x1="${paddingLeft}" y1="${paddingTop}" x2="${width - paddingRight}" y2="${paddingTop}" class="benchmark-grid"></line>
      <line x1="${paddingLeft}" y1="${midY}" x2="${width - paddingRight}" y2="${midY}" class="benchmark-grid"></line>
      <line x1="${paddingLeft}" y1="${height - paddingBottom}" x2="${width - paddingRight}" y2="${height - paddingBottom}" class="benchmark-grid"></line>
      <text x="${paddingLeft - 8}" y="${paddingTop + 4}" text-anchor="end" class="benchmark-axis-label">${escapeHtml(formatYAxisValue(max, selector))}</text>
      <text x="${paddingLeft - 8}" y="${midY + 4}" text-anchor="end" class="benchmark-axis-label">${escapeHtml(formatYAxisValue(midValue, selector))}</text>
      <text x="${paddingLeft - 8}" y="${height - paddingBottom + 4}" text-anchor="end" class="benchmark-axis-label">${escapeHtml(formatYAxisValue(min, selector))}</text>
      <text x="${paddingLeft}" y="${height - 8}" text-anchor="start" class="benchmark-axis-label">${escapeHtml(firstDate)}</text>
      <text x="${width - paddingRight}" y="${height - 8}" text-anchor="end" class="benchmark-axis-label">${escapeHtml(lastDate)}</text>
      <polyline fill="none" stroke="${color}" stroke-width="3" points="${pointsText}"></polyline>
      <circle cx="${latestX}" cy="${latestY}" r="4" fill="${color}"></circle>
    </svg>
  `
}

function renderLatestTable(latest) {
  const rows = [...(latest?.Rows ?? [])]
    .sort((left, right) => (left.MeanMicroseconds ?? Number.MAX_VALUE) - (right.MeanMicroseconds ?? Number.MAX_VALUE))

  const body = rows.map(row => `
    <tr>
      <td>${escapeHtml(row.Method)}</td>
      <td>${escapeHtml(row.ProviderName)}</td>
      <td>${formatMicroseconds(row.MeanMicroseconds)}</td>
      <td>${formatBytes(row.AllocatedBytes)}</td>
      <td>${row.NoisePercent === null || row.NoisePercent === undefined ? '-' : `${formatNumber(row.NoisePercent, 1)}%`}</td>
    </tr>
  `).join('')

  return `
    <table class="benchmark-table">
      <thead>
        <tr>
          <th>Method</th>
          <th>Provider</th>
          <th>Mean</th>
          <th>Allocated</th>
          <th>Noise</th>
        </tr>
      </thead>
      <tbody>${body}</tbody>
    </table>
  `
}

function renderComparisonTable(comparison) {
  if (!comparison?.Rows?.length) {
    return '<p class="benchmark-muted">No baseline comparison has been published yet.</p>'
  }

  const body = comparison.Rows.map(row => `
    <tr>
      <td>${escapeHtml(row.Method)}</td>
      <td>${escapeHtml(row.ProviderName)}</td>
      <td class="${row.Status === 'warning' ? 'benchmark-status-warning' : row.Status === 'improved' ? 'benchmark-status-good' : ''}">${formatPercent(row.MeanDeltaPercent)}</td>
      <td class="${row.Status === 'warning' ? 'benchmark-status-warning' : row.Status === 'improved' ? 'benchmark-status-good' : ''}">${formatPercent(row.AllocatedDeltaPercent)}</td>
      <td>${escapeHtml(row.Status)}</td>
    </tr>
  `).join('')

  return `
    <table class="benchmark-table">
      <thead>
        <tr>
          <th>Method</th>
          <th>Provider</th>
          <th>Mean Δ</th>
          <th>Allocated Δ</th>
          <th>Status</th>
        </tr>
      </thead>
      <tbody>${body}</tbody>
    </table>
  `
}

function renderTrendCards(history) {
  const groups = [...groupHistoryRuns(history).entries()]
    .sort((left, right) => left[0].localeCompare(right[0]))

  if (groups.length === 0) {
    return '<p class="benchmark-muted">No benchmark history is published yet.</p>'
  }

  return groups.map(([, points]) => {
    const latest = points[points.length - 1]
    return `
      <section class="benchmark-card">
        <header class="benchmark-card-header">
          <div>
            <h3>${escapeHtml(latest.method)} <span>${escapeHtml(latest.providerName)}</span></h3>
            <p>${points.length} published runs</p>
          </div>
        </header>
        <div class="benchmark-card-grid">
          <div>
            <h4>Mean Time</h4>
            ${renderTrendChart(points, 'meanMicroseconds', '#d97706')}
            <p class="benchmark-card-value">${formatMicroseconds(latest.meanMicroseconds)}</p>
          </div>
          <div>
            <h4>Allocated Bytes</h4>
            ${renderTrendChart(points, 'allocatedBytes', '#0f766e')}
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
    const allowedProviders = parseProviderFilter(root)

    const [rawHistory, rawLatest, rawComparison] = await Promise.all([
      fetchJson(historyUrl),
      fetchJson(latestUrl),
      comparisonUrl ? fetchJson(comparisonUrl).catch(() => null) : Promise.resolve(null)
    ])

    const history = filterHistory(rawHistory, allowedProviders)
    const latest = filterLatest(rawLatest, allowedProviders)
    const comparison = filterComparison(rawComparison, allowedProviders)

    const latestCommit = latest?.Metadata?.Commit ? latest.Metadata.Commit.slice(0, 7) : 'unknown'
    const latestBranch = latest?.Metadata?.Branch ?? 'unknown'
    const latestRunner = latest?.Metadata?.RunnerOs ?? 'unknown runner'
    const providerScope = allowedProviders.length === 0 ? 'all published providers' : allowedProviders.join(', ')

    root.innerHTML = `
      <section class="benchmark-overview">
        <div class="benchmark-overview-item">
          <strong>Latest run</strong>
          <span>${escapeHtml(latest?.GeneratedAtUtc ?? '-')}</span>
        </div>
        <div class="benchmark-overview-item">
          <strong>Commit</strong>
          <span>${escapeHtml(latestCommit)}</span>
        </div>
        <div class="benchmark-overview-item">
          <strong>Branch</strong>
          <span>${escapeHtml(latestBranch)}</span>
        </div>
        <div class="benchmark-overview-item">
          <strong>Runner</strong>
          <span>${escapeHtml(latestRunner)}</span>
        </div>
        <div class="benchmark-overview-item">
          <strong>Provider scope</strong>
          <span>${escapeHtml(providerScope)}</span>
        </div>
      </section>
      <h2>Latest Summary</h2>
      ${renderLatestTable(latest)}
      <h2>Latest Comparison</h2>
      ${renderComparisonTable(comparison)}
      <h2>Trends</h2>
      <div class="benchmark-card-list">
        ${renderTrendCards(history)}
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
