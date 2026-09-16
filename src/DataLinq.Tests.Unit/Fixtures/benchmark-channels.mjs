import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'

// Give the module a website URL so the real legacy-history fallback can resolve its config.
const source = (await readFile('public/benchmark-results.js', 'utf8'))
  .replaceAll('import.meta.url', JSON.stringify('https://example.invalid/public/benchmark-results.js'))
const api = await import('data:text/javascript;base64,' + Buffer.from(source + `
export { flattenHistory, chartSeries, renderTrendChart, renderBenchmarkPage, renderBenchmarkResults,
  comparisonBranches, branchLabel, selectBenchmarkPoints, buildTrendRows };
`).toString('base64'))
const configured = JSON.parse(await readFile('public/release-channels.json', 'utf8'))
assert.equal(configured.SchemaVersion, 1)
const channels = { Stable: { Branch: 'master', Label: 'Stable (master)' },
  Development: { Branch: 'v0.10', Label: '0.10 (development)' } }
const run = (id, branch, value, runtime = '.NET 10.0.12', profile = 'default') => ({
  RunId: String(id), GeneratedAtUtc: `2026-09-${String(id).padStart(2, '0')}T00:00:00Z`,
  Metadata: { Branch: branch, Commit: 'a'.repeat(40), Profile: profile, RuntimeDescription: runtime },
  Rows: [{ Method: 'Warm primary-key fetch', ProviderName: 'sqlite-memory',
    MeanMicroseconds: value, AllocatedBytes: 0, UncertaintyPercent: 1 }]
})
const runs = [
  run(1, 'master', 100), run(2, 'v0.10', 1000), run(3, 'master', 110),
  run(4, 'v0.10', 900), run(5, 'v0.9', 800), run(6, 'master', 9999, '.NET 8.0.1'),
  run(7, 'master', 999, '.NET 10.0.12', 'heavy')
]
const before = JSON.stringify(runs)
const points = api.flattenHistory({ Runs: runs })
const selected = api.selectBenchmarkPoints(points, 'default', '.NET 10.0.12', 'master', 'v0.10')
assert.deepEqual(selected.map(point => point.runId), ['1', '2', '3', '4'])
const series = api.chartSeries(selected, 'meanMicroseconds')
assert.equal(series.length, 2)
assert.deepEqual(series[0].values.map(item => item.smoothedValue), [105, 105])
assert.deepEqual(series[1].values.map(item => item.smoothedValue), [950, 950])
for (const [branch, delta] of [['master', 10], ['v0.10', -10]]) {
  const rows = api.buildTrendRows(selected.filter(point => point.branch === branch), [], 'default')
  assert.equal(rows.length, 1)
  assert.equal(rows[0].meanDeltaPrevious, delta)
}
const html = api.renderTrendChart(selected, 'meanMicroseconds', channels)
assert.match(html, /data-series-branch="master"/)
assert.match(html, /data-series-branch="v0.10"/)
assert.match(html, /stroke-dasharray="6 3"/)
const decode = value => value.replaceAll('&quot;', '"').replaceAll('&#39;', "'").replaceAll('&amp;', '&')
const hover = JSON.parse(decode(html.match(/data-chart-points="([^"]+)"/)[1]))
// Dates, rather than each branch's sample index, determine the common x-axis.
assert.equal(hover[0].x, 68)
assert.equal(hover[3].x, 500)
assert.ok(hover[2].x > hover[0].x && hover[2].x < hover[1].x)
assert.ok(hover.every(point => point.title.includes('.NET 10.0.12')))
assert.doesNotMatch(api.renderTrendChart([selected[0]], 'allocatedBytes', channels), /NaN|Infinity/)
assert.match(api.renderTrendChart([selected[0]], 'allocatedBytes', channels), /<circle/)
assert.match(api.renderTrendChart([], 'meanMicroseconds', channels), /No matching runs/)

const rollover = { ...channels, Development: { Branch: 'v0.11', Label: '0.11 (development)' } }
assert.deepEqual(api.comparisonBranches(points, rollover), ['v0.11', 'v0.10', 'v0.9'])
assert.equal(api.branchLabel('v0.10', rollover), 'v0.10 (archived)')
assert.equal(api.branchLabel('v0.11', rollover), '0.11 (development)')
assert.equal(JSON.stringify(runs), before)
const root = {
  dataset: {}, innerHTML: '',
  querySelectorAll: () => [],
  querySelector: () => ({ addEventListener() {} })
}
api.renderBenchmarkPage(root, points, 'default', [], [], '', rollover)
assert.match(root.innerHTML, /Awaiting matching runs/)
assert.doesNotMatch(root.innerHTML, /data-series-branch="v0.10"/)
root.dataset.selectedBranch = 'v0.10'
root.dataset.selectedRuntime = '.NET 10.0.12'
api.renderBenchmarkPage(root, points, 'default', [], [], '', rollover)
assert.match(root.innerHTML, /data-series-branch="v0.10"/)
assert.match(root.innerHTML, /v0.10 \(archived\)/)
globalThis.document = { getElementById: () => root }
root.dataset.historyUrl = 'https://example.invalid/history.json'
root.dataset.providerFilter = 'sqlite-memory'
root.dataset.methodFilter = 'Warm primary-key fetch'
globalThis.fetch = async url => ({
  ok: true, json: async () => url.includes('/history.json') ? { Runs: runs } : channels
})
await api.renderBenchmarkResults()
assert.doesNotMatch(root.innerHTML, /Failed to load/)
assert.match(root.innerHTML, /data-series-branch="master"/)
console.log('Branch, date-axis, runtime, smoothing, deltas, empty data and rollover checks passed.')
