# Memory ordered paging measurements

Finding F25 from the [codebase review](Codebase%20Review%202026-09-04.md#f25).

Small ordered pages retain at most `Skip + Take` matching canonical rows in a priority queue when that prefix is at most one quarter of the source row count. The queue orders Int32 primary keys in the requested direction and uses source ordinal to preserve stable ties. Selection still scans every source row and applies the existing predicates. Only returned entities are materialized; scalar projections do not materialize entities.

For prefix size k, selection uses O(k) temporary storage and O(n log k) work. Larger prefixes and queries without Take retain the stable merge-sort path; its match list grows as needed rather than allocating for the entire source up front. Cancellation checks remain in scanning and result extraction. Zero Take performs no scan. Prefix arithmetic uses Int64 so large Skip and Take values cannot overflow.

## Local comparison

Measured 2026-09-05 on Windows x64, .NET 8.0.30, BenchmarkDotNet 0.15.8, ShortRun (`--profile default`), with `DOTNET_TieredCompilation=0` in both runs. The exact same six workloads seed reversed primary keys, warm and validate scalar queries, and exclude seeding from timing. Baseline product code: master `3915366a`; candidate: this change. Both were working-tree runs with the diagnostic benchmark added.

| Query | Rows | Baseline mean µs | Candidate mean µs | Baseline allocated KiB | Candidate allocated KiB |
|---|---:|---:|---:|---:|---:|
| Take(5) | 1,000 | 216.0 | 104.2 | 34.49 | 11.34 |
| Skip(100).Take(5) | 1,000 | 187.1 | 154.8 | 35.88 | 18.45 |
| Take(5) | 10,000 | 2,229.6 | 780.0 | 245.44 | 11.34 |
| Skip(100).Take(5) | 10,000 | 2,136.1 | 1,458.5 | 246.83 | 18.46 |
| Take(5) | 100,000 | 29,328.2 | 6,000.7 | 2,355.01 | 11.35 |
| Skip(100).Take(5) | 100,000 | 30,260.1 | 14,405.0 | 2,356.41 | 18.47 |

These are diagnostic observations, not a release benchmark or an IDE/application latency guarantee. ShortRun confidence intervals are wide (for example, the candidate 100,000-row paged mean has ±9,860.4 µs reported error). Allocation scaling and returned results are the stronger evidence. All six rows in each final run completed with telemetry showing exactly n scanned rows and zero materializations/cache accesses. Both artifacts report `Outcome=Passed`, `IsCompleteForInvocation=true`, and `ValidForEvidence=false` because these filtered runs do not establish canonical release evidence.

Local artifacts under `artifacts/review-fixes-2026-09-04/`: `F25-baseline.json`, `F25-candidate.json`, `F25-comparison.json`. Baseline run ID: `20260905-021236091-b92ff2ab44ec47458128d09c4cfcec43`; candidate: `20260905-021904296-9f627cd0b10a419395a4559da6864c26`. Initial setup runs were incomplete while workload category/telemetry integration was missing; they are excluded from this comparison.

Reproduce with `DataLinq.Benchmark.CLI run --filter '*MemoryPagingBenchmarks*' --profile default --history-json <path>` through the repository's dotnet wrapper, using the same tiered-compilation setting on both revisions.

## Correctness checks

All 149 Memory tests passed. The added public-query regressions compare against LINQ-to-Objects over sorted, reversed, and shuffled input; ascending/descending keys including Int32 extremes; selective predicates; zero, small, large, and overflowing-prefix page counts; cancellation before and between results; and selected-only materialization. A warmed allocation regression compares 2,000 and 20,000 rows with a broad 64 KiB growth allowance. The Memory provider also builds for net8.0, net9.0, and net10.0 with no warnings.
