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

## 0.9 Candidate Disposition

The 0.9 candidate does not claim literal allocation parity with final 0.8 across the calibrated suite. It reaches or beats the final-0.8 allocation baseline on the accepted key rows, while four of nine `sqlite-memory` rows remain above their strict budgets: update employees, cold relation traversal, CRUD workflow batch, and CRUD workflow small.

Those exceptions were accepted without raising the budgets because the remaining candidates require correctness-sensitive cache, ownership, transaction, or provider-lifetime work. The calibrated allocation comparison has no telemetry changes, and its three latency warnings did not reproduce consistently in an adjacent same-runtime receipt.

The separate final SQL comparison against the retained July pre-foundation checkpoint is diagnostic rather than strict release evidence, because the older schema-v2 baseline lacks the complete environment identity now required by schema v3. Across its six repeated-query rows, 0.9 allocates about 17% to 34% more per operation after the backend boundary; timing is mostly noisy and does not establish a sustained hot-path regression. DataLinq 0.9 does not ship a production plan cache, so these results are not a plan-cache performance claim.

The same comparison does reproduce a bounded `sqlite-file` startup tradeoff: provider initialization is about 23% to 29% slower and the first primary-key fetch about 27% to 45% slower, or approximately 0.17 to 0.22 ms and 0.11 to 0.18 ms of one-time work. Warm primary-key latency remains stable or improved. The file-only movement aligns with the intentional switch from shared cache to SQLite's private/default file cache, which preserves committed-reader behavior under WAL instead of inheriting shared-cache locking behavior. That bounded cost is accepted for the correctness contract; a repeatable sustained-path regression remains a blocker.

Use the [0.9 candidate release notes](releases/0.9.md#performance-and-final-08-comparison), [calibrated receipt in PR #82](https://github.com/bazer/DataLinq/pull/82), [#26 allocation disposition](https://github.com/bazer/DataLinq/issues/26#issuecomment-5344291969), and [#101 final SQL disposition](https://github.com/bazer/DataLinq/issues/101) for exact values, commits, run identity, and rationale. This is candidate/source evidence. Final package-backed evidence and the explicit release GO remain tracked in [issue #80](https://github.com/bazer/DataLinq/issues/80).

## Historical 0.8 Evidence

The [0.8 GitHub release](https://github.com/bazer/DataLinq/releases/tag/0.8.0) is the durable published boundary. Maintainer records also name repo-local `artifacts/...` histories used during that release, but those paths are not website downloads and are not reproduced here as if they were public benchmark artifacts.

The historical 0.8 query-hotpath run contained noisy latency rows, so it is not an honest latency-improvement claim. Its allocation histories remain useful as the fixed comparison baseline used by the 0.9 candidate disposition above.
