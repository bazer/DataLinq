# Row cache eviction: F12 follow-up

Date: 2026-09-05. Addresses [F12 in the codebase review](Codebase%20Review%202026-09-04.md#f12).

## Change

`RowStore<TKey>` keeps a removable linked node for each live row in timestamp order.
Selecting and removing the oldest row no longer scans the dictionary. Removing k
rows is amortized O(k) in the row store, including calls that remove one row at a
time. Oldest/newest timestamp reads are O(1), and age cleanup stops at the cutoff.
Normal insertion appends in O(1); if the wall clock moves backwards, insertion
walks backwards to preserve absolute timestamp order. Reads do not refresh age.

Explicit removals and clears discard the corresponding nodes, so replacement
churn cannot retain obsolete order entries. The extra node and reference per live
row are included in cache-size estimates. Invalidation, notification, and table-level
size-accounting work still have their own costs; the complexity claim concerns
the row store, not an entire application request.

## Verification

All 1,719 unit cases passed. New cases cover row and payload limits, replacement,
equal timestamps, clock rollback, exact age boundaries, clearing, accounting,
and concurrent lookup/churn/eviction.

The [benchmark workload](../../src/DataLinq.Benchmark/RowEvictionBenchmarks.cs)
populates distinct integer keys outside the measurement and removes half of them.
It measures row limits, payload limits, and repeated single-row eviction. Payloads
are represented by sizes; it excludes entity construction and database traffic.
The telemetry receipt explicitly records that this low-level workload emits no
query/provider telemetry.

The before run used `master`'s original `RowStore.cs`; the after run used the fix,
with the same benchmark source. Windows x64, .NET 8, BenchmarkDotNet MediumRun,
`DOTNET_TieredCompilation=0` for both runs:

| Initial rows | Removed | Before mean | After mean | Before error / mean | After error / mean | Eviction allocation, both |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 5,000 | 2,500 | 39.014 ms | 0.131 ms | 1.0% | 11.2% | 250.79 KiB |
| 10,000 | 5,000 | 167.806 ms | 0.245 ms | 4.1% | 6.9% | 501.41 KiB |
| 20,000 | 10,000 | 724.023 ms | 0.489 ms | 4.6% | 3.8% | 1,002.62 KiB |

The observed growth changes from roughly quadratic to roughly linear. Allocation
during removal is unchanged; the maintained-order nodes are allocated during cache
population, outside this measurement. ShortRun checks also exercised payload-limit
and single-row-batch eviction at all three sizes; their noisier timings are not
used to claim precise speedup ratios.

Both MediumRun commands completed successfully with `IsCompleteForInvocation=true`.
Their outcome is `ReviewRequired`, and `ValidForEvidence=false`: these are filtered
diagnostics outside the canonical release matrix, with short-iteration warnings.
They are not production latency measurements or release evidence. Concurrent
reader correctness is tested; reader tail latency under a production workload was
not measured.

Local evidence is retained under `artifacts/review-fixes-2026-09-04/`:
`F12-row-before.json`, `F12-row-after.json`, `F12-before.json`, `F12-after.json`,
and their logs. Each JSON references the original CSV, Markdown, and telemetry
artifacts. The final MediumRun IDs are
`20260904-224016042-89d88888e0874ec299a38cea1a389878` (before) and
`20260904-224232950-c7b2834b60d94e21b0a7861c02a2960f` (after).

To repeat the diagnostic on a selected checkout:

```powershell
$env:DOTNET_TieredCompilation = '0'
.\scripts\dotnet-sandbox.ps1 run --project src/DataLinq.Benchmark.CLI -- run `
  --filter '*RowEvictionBenchmarks.RowLimit*' --profile heavy `
  --history-json artifacts/benchmarks/row-eviction.json
```
