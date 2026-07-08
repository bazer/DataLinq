# Benchmark Results

This page shows the small published benchmark subset that DataLinq publishes from CI.

The point is not to pretend these numbers are universal truth. They are not.

They are useful because they are:

- run repeatedly on one controlled CI lane
- limited to the current stable benchmark category plus small and batch macro CRUD lanes that are useful enough to watch separately
- currently trended only on the `sqlite-memory` provider to avoid redundant file-vs-memory noise
- stored in a consistent machine-readable format
- plotted over time so regressions, profile differences, and trend changes are visible

## Read This Honestly

These graphs are decision support, not marketing material.

Important limits:

- the numbers come from GitHub-hosted CI runners, so absolute timings still include runner variance
- the current suite is intentionally narrow and should be read as hot-path trend data, not as a full product performance verdict
- `default` and `heavy` benchmark profiles are selected separately, because comparing them in one table is noisy and misleading
- older published history used different benchmark category selections, so the page filters to the current published scenario set
- high-noise rows should be treated with suspicion even when the line moves in an exciting direction

## Profiles

`default` is the ordinary CI profile. It uses BenchmarkDotNet `ShortRun`, so it is quick enough to publish on push/manual runs and gives us frequent trend points. The tradeoff is higher noise.

`heavy` is the scheduled profile. It uses BenchmarkDotNet `MediumRun`, so it spends more time measuring and is the better lane for judging whether a movement is probably real.

Do not compare default numbers directly against heavy numbers. They use different measurement jobs; the useful comparison is default-to-default or heavy-to-heavy.

## Published Trends

<div
  id="benchmark-results-root"
  data-history-url="https://raw.githubusercontent.com/bazer/DataLinq/benchmark-data/benchmarks/history.json"
  data-commit-url-template="https://github.com/bazer/DataLinq/commit/{commit}"
  data-provider-filter="sqlite-memory"
  data-method-filter="Provider initialization,Startup primary-key fetch,Cold primary-key fetch,Warm primary-key fetch,Cold relation traversal,Warm relation traversal,Update employees,CRUD workflow small,CRUD workflow batch">
  Loading benchmark history...
</div>

<script type="module" src="../public/benchmark-results.js"></script>

## Maintainer Evidence

Exact artifact paths below are repo-local release evidence, not website downloads. They are useful to maintainers reviewing the 0.8 release pass, but they should not be read as public performance claims.

The final 0.8 release pass refreshed local heavy-profile benchmark histories:

- `artifacts/benchmarks/history/v0.8-final-query-hotpath.json`
- `artifacts/benchmarks/history/v0.8-final-phase2-watch.json`

Those files are release evidence, not marketing copy. The query-hotpath run still had noisy rows, so it should not be used as a latency-improvement claim. The watchpoint run is useful for allocation baselines; for example, warm primary-key fetch remained at 1.77 KB allocated in both SQLite memory and file modes on that machine.
