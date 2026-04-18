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

function renderSparkline(points, selector, color) {
  const values = points
    .map(point => point[selector])
    .filter(value => value !== null && value !== undefined && !Number.isNaN(value))

  if (values.length < 2) {
    return '<div class="benchmark-empty-chart">Need at least two runs</div>'
  }

  const width = 360
  const height = 140
  const padding = 16
  const min = Math.min(...values)
  const max = Math.max(...values)
  const range = Math.max(max - min, 1)
  const steps = values.length - 1

  const polyline = values
    .map((value, index) => {
      const x = padding + (index / steps) * (width - padding * 2)
      const normalized = (value - min) / range
      const y = height - padding - normalized * (height - padding * 2)
      return `${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')

  return `
    <svg class="benchmark-sparkline" viewBox="0 0 ${width} ${height}" preserveAspectRatio="none" role="img" aria-label="benchmark trend">
      <line x1="${padding}" y1="${height - padding}" x2="${width - padding}" y2="${height - padding}" class="benchmark-axis"></line>
      <line x1="${padding}" y1="${padding}" x2="${padding}" y2="${height - padding}" class="benchmark-axis"></line>
      <polyline fill="none" stroke="${color}" stroke-width="3" points="${polyline}"></polyline>
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
            ${renderSparkline(points, 'meanMicroseconds', '#d97706')}
            <p class="benchmark-card-value">${formatMicroseconds(latest.meanMicroseconds)}</p>
          </div>
          <div>
            <h4>Allocated Bytes</h4>
            ${renderSparkline(points, 'allocatedBytes', '#0f766e')}
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

    const [history, latest, comparison] = await Promise.all([
      fetchJson(historyUrl),
      fetchJson(latestUrl),
      comparisonUrl ? fetchJson(comparisonUrl).catch(() => null) : Promise.resolve(null)
    ])

    const latestCommit = latest?.Metadata?.Commit ? latest.Metadata.Commit.slice(0, 7) : 'unknown'
    const latestBranch = latest?.Metadata?.Branch ?? 'unknown'
    const latestRunner = latest?.Metadata?.RunnerOs ?? 'unknown runner'

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
