# DataLinq.Benchmark.CLI

`DataLinq.Benchmark.CLI` is the canonical entry point for the DataLinq benchmark harness.

Use it instead of calling BenchmarkDotNet directly.

## Why It Exists

Direct BenchmarkDotNet invocation is too raw for normal repo use.

The CLI standardizes restore/build behavior, keeps artifacts under repo control, and adds stable history and comparison outputs that are actually useful for regression tracking.

## Commands

The command examples assume your current directory is the repo's `src` folder.

### `list`

Lists the available benchmark methods.

```bash
dotnet run --project DataLinq.Benchmark.CLI -- list
```

Useful options:

- `--no-build`
  Skips restore/build and uses the existing benchmark assembly.
- `--verbose`
  Prints the underlying restore/build/BenchmarkDotNet output.

You can also pass extra BenchmarkDotNet arguments after `--`.

### `run`

Runs the benchmark harness with compact output.

```bash
dotnet run --project DataLinq.Benchmark.CLI -- run
dotnet run --project DataLinq.Benchmark.CLI -- run --filter "*WarmPrimaryKeyFetch*"
dotnet run --project DataLinq.Benchmark.CLI -- run --profile smoke
dotnet run --project DataLinq.Benchmark.CLI -- run --profile heavy
dotnet run --project DataLinq.Benchmark.CLI -- run --phase2-watch
dotnet run --project DataLinq.Benchmark.CLI -- run --phase3-query-hotpath
dotnet run --project DataLinq.Benchmark.CLI -- run --phase10-key-foundation
dotnet run --project DataLinq.Benchmark.CLI -- run --phase11-cache-invalidation
dotnet run --project DataLinq.Benchmark.CLI -- run --v09-query-backend
dotnet run --project DataLinq.Benchmark.CLI -- run --v09-memory-read
dotnet run --project DataLinq.Benchmark.CLI -- run --allocation-regression
dotnet run --project DataLinq.Benchmark.CLI -- run --allocation-stages
```

Important options:

- `--filter`
  BenchmarkDotNet filter pattern. Defaults to `*`.
- `--profile`
  `default`, `smoke`, or `heavy`. The wrapper selects one configured BenchmarkDotNet job for the chosen profile.
- `--no-build`
  Reuses the existing benchmark assembly.
- `--benchmark-target-root`
  Builds and runs the benchmark project from a clean historical DataLinq worktree beneath `artifacts/benchmarks/targets`. The current CLI remains the evidence writer.
- `--keep-files`
  Preserves BenchmarkDotNet-generated temporary files.
- `--verbose`
  Prints the underlying restore/build/BenchmarkDotNet output.
- `--phase2-watch`
  Runs only the Phase 2 benchmark watchpoints.
- `--phase3-query-hotpath`
  Runs only the Phase 3 query/runtime hot-path benchmark lane.
- `--phase10-key-foundation`
  Runs only the Phase 10 key/cache attribution lane.
- `--phase11-cache-invalidation`
  Runs only the Phase 11 explicit cache invalidation lane.
- `--v09-query-backend`
  Runs only the v0.9 query-planning, invocation-binding, and SQL-adapter evidence lane.
- `--v09-memory-read`
  Runs only the provider-free v0.9 `DataLinq.Memory` read evidence lane.
- `--allocation-regression`
  Runs the exact nine-row SQLite-memory allocation comparison lane used for the final-0.8 budget.
- `--allocation-stages`
  Runs focused provider-row decoding/materialization and mutation capture/preflight allocation benchmarks.
- `--history-json`
  Writes a schema-v3 benchmark history entry beneath the repository `artifacts` tree.
- `--baseline`
  Compares the current run against an existing history artifact beneath the repository `artifacts` tree. Current v3 and structurally valid legacy v1/v2 inputs are readable, but legacy inputs are diagnostic-only.
- `--comparison-json`
  Writes a schema-v3 machine-readable comparison artifact beneath the repository `artifacts` tree. Requires both `--baseline` and `--history-json`.
- `--warning-threshold-percent`
  Controls the percent regression threshold for comparison warnings.
- `--release-evidence`
  Enables the strict release-evidence gate. It requires `--history-json`, fails unless the new history is valid release evidence, and, when `--baseline` is supplied, also requires `--comparison-json` and a release-valid comparison.

Additional BenchmarkDotNet arguments can be passed after `--`.

Example:

```bash
dotnet run --project DataLinq.Benchmark.CLI -- run -- --anyCategories stable macro-readwrite macro-bulk
```

## Final-0.8 allocation baseline

`--benchmark-target-root` separates the tooling checkout from the runtime checkout. The current `DataLinq.Benchmark.CLI` and `DataLinq.DevTools` assemblies must match the clean current checkout; the benchmark assembly must match the clean target worktree. Both repository states are captured before and after the run. A dirty or changing checkout, mismatched assembly commit, or unknown build state invalidates evidence.

The frozen baseline is commit `8bcfc770246f960e27a91e3046f19a76c3736217`. Its runtime and benchmark sources are identical to tag `0.8.0`; the later commit contains documentation changes only. The historical benchmark config did not register CSV or GitHub-Markdown exporters, so current tooling replaces only that benchmark config during the external build with `HistoricalBenchmarkConfig.cs.txt`. The shim preserves the historical jobs, columns, summary style, and ordering and adds only the exporters required by schema v3. The target worktree remains byte-for-byte clean, while the shim is covered by the current tooling commit provenance.

Run the stable allocation baseline and candidate from a clean committed checkout at the repo root:

```powershell
git worktree add --detach artifacts\benchmarks\targets\final-0.8 8bcfc770246f960e27a91e3046f19a76c3736217

.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run `
  --benchmark-target-root artifacts\benchmarks\targets\final-0.8 `
  --allocation-regression --profile heavy --release-evidence `
  --history-json artifacts\benchmarks\allocation\final-0.8-stable.json

.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run `
  --allocation-regression --profile heavy --release-evidence `
  --history-json artifacts\benchmarks\allocation\candidate-stable.json `
  --baseline artifacts\benchmarks\allocation\final-0.8-stable.json `
  --comparison-json artifacts\benchmarks\allocation\stable-comparison.json
```

Repeat the same-runtime SQL hot-path comparison separately; combining selectors would destroy the exact scope contract:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run `
  --benchmark-target-root artifacts\benchmarks\targets\final-0.8 `
  --phase3-query-hotpath --profile heavy --release-evidence `
  --history-json artifacts\benchmarks\allocation\final-0.8-sql-hotpath.json

.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run `
  --phase3-query-hotpath --profile heavy --release-evidence `
  --history-json artifacts\benchmarks\allocation\candidate-sql-hotpath.json `
  --baseline artifacts\benchmarks\allocation\final-0.8-sql-hotpath.json `
  --comparison-json artifacts\benchmarks\allocation\sql-hotpath-comparison.json
```

The allocation budget is intentionally uncompromising: every tracked candidate row must allocate no more bytes per operation than the same-runner final-0.8 row (`AllocatedDeltaPercent <= 0`). The comparison warning percentage remains a triage threshold, not permission to rebaseline above 0.8. The machine-readable scope and policy live in `docs/contributing/benchmark-allocation-budgets.json`.

Use `--allocation-stages --profile heavy` to attribute current-runtime work without a macro run. The four cases isolate canonical provider-row ownership/copying, provider-to-model materialization, state-change capture from prebuilt changed mutables, and execution preflight against a prepared transaction/state change. The existing `--v09-query-backend` lane already isolates structural parsing, initial bind/freeze, invocation rebinding, and capability preparation.

## Phase 2 Watchpoints

Phase 2 metadata and generator work should be checked against the narrow `phase2-watch` benchmark category before claiming a runtime win.

That category intentionally contains only:

- `ProviderInitialization`
  Tracks metadata/provider startup cost.
- `StartupPrimaryKeyFetch`
  Tracks the first-query path after opening a fresh scope.
- `WarmPrimaryKeyFetch`
  Tracks the hot primary-key path after the row cache has already been populated.

Run the watchpoints with:

```bash
dotnet run --project DataLinq.Benchmark.CLI -- run --phase2-watch
```

For quick local smoke validation, combine the category with the dry profile:

```bash
dotnet run --project DataLinq.Benchmark.CLI -- run --phase2-watch --profile smoke
```

The dry profile is useful for checking harness wiring. It is not a trustworthy performance result.

## Phase 3 Query Hot Path

Phase 3 query/runtime work should start against the narrow `phase3-query-hotpath` benchmark category before changing the SQL parameter boundary or writer internals.

That category intentionally contains:

- `RepeatedNonPrimaryKeyEqualityFetch`
  Tracks repeated same-shape entity queries where values change and the simple primary-key cache shortcut should not erase SQL generation.
- `RepeatedInPredicateFetch`
  Tracks repeated `IN` predicate generation and command construction with multiple parameter slots.
- `RepeatedScalarAny`
  Tracks repeated scalar command construction and execution for a common `Any` query shape.

Run the lane with:

```bash
dotnet run --project DataLinq.Benchmark.CLI -- run --phase3-query-hotpath
```

For quick local smoke validation:

```bash
dotnet run --project DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile smoke
```

Use the smoke profile only to prove the lane is wired correctly. Use the default or heavy profile before interpreting performance.

## v0.9 Query Backend Evidence

The `v0.9-query-backend` category isolates the query-foundation seams required by the v0.9 release-evidence plan. It complements the broader Phase 3 end-to-end query lane; it does not replace it.

The category contains six deliberately narrow cases:

- `Expression parse/structural template`
  Parses one prebuilt scalar-`Any` expression into the structural template plus an unbound captured-value snapshot. It deliberately stops before `QueryPlanInvocation.Bind`, isolating structural parsing/template creation from binding validation, binding-order normalization, and the binder's second defensive freeze/copy. Capture itself snapshots the local sequence once, and that real parse cost remains in this case.
- `Expression parse/template/initial bind`
  Parses one prebuilt scalar-`Any` expression containing one non-null scalar and one three-item local sequence. The current production parser creates the structural template and immediately binds the first invocation, so this case names that combined contract exactly rather than pretending to measure template creation alone.
- `Template freeze/validation`
  Reconstructs a template from prebuilt structural nodes and measures collection freezing plus structural validation. It excludes expression parsing and invocation binding.
- `Invocation bind scalar/local sequence`
  Rebinds the prebuilt template with the same specialization. The measured work includes validation, ordering, and the defensive copy of the three-item local sequence; that copy is part of the real invocation contract.
- `SQL request/capability preparation`
  Prepares a prebuilt execution request, including source ownership, backend selection, requirement extraction, and capability validation. It performs no database command.
- `SQL adapter scalar Any`
  Repeatedly executes the same pre-parsed invocation through the production SQL adapter against warmed SQLite. The scalar result avoids entity materialization and row-cache shortcuts, but the measurement still includes real database execution and should not be described as a pure adapter microbenchmark.

Run a wiring smoke from the repo root with:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --v09-query-backend --profile smoke --history-json artifacts\benchmarks\history\v0.9-query-backend-smoke.json
```

The smoke profile proves selection, execution, telemetry, and history serialization only. Use a default or heavy run before interpreting timings or allocations.

The accepted clean query-heavy checkpoint establishes the first post-foundation baseline for these exact cases. It is not a retroactive pre-foundation comparison. At that query-only checkpoint the separate `DataLinq.Memory` lane remained open; that lane is now implemented and has its own accepted checkpoint below, while RE-5 still owns both selectors' final-RC comparison.

## v0.9 Memory Read Evidence

The `v0.9-memory-read` category measures the bounded public `DataLinq.Memory` preview without multiplying the cases across SQL providers. Its single `ProviderName` value is `memory`, which means the provider-free backend—not SQLite's `sqlite-memory` mode.

The category contains nine cases over deterministic generated benchmark models:

- `Memory database construction`
  Constructs an empty database after generated metadata has been bound during global setup. This is a warm-metadata construction measurement; it is not presented as cold process startup.
- `Memory construct and seed`
  Constructs a new database and snapshots, converts, indexes, and publishes `1,024` primitive rows plus `256` canonical-`Guid` rows through the public one-shot seed surface.
- `Memory primary-key hit`
  Performs one warmed public `Find<TModel>` hit against the existing primary-key index and materialization cache.
- `Memory primary-key miss`
  Performs one stable absent-key `Find<TModel>` probe.
- `Memory scalar scan`
  Enumerates a prebuilt direct-`Int32` projection over `1,024` rows and computes a checksum without entity materialization.
- `Memory filter order page`
  Executes the exact supported filter, primary-key order, `Skip(8)`, `Take(16)`, direct-`Int32` projection shape against descending seed input.
- `Memory repeated entity identity`
  Re-executes a prebuilt exact entity query after priming identity and verifies that the cached instance is reused.
- `Memory direct-Guid equality count`
  Counts one direct canonical-`Guid` equality match without entity materialization.
- `Memory typed-ID equality count`
  Counts the corresponding Guid-backed typed-ID equality match, including model-to-canonical binding conversion but no entity materialization.

The reusable source, filter, order, paging, and projection query chains are prebuilt. Scalar-scan and page enumeration therefore add no terminal expression, while the identity and Guid cases intentionally call the public `Single`/`Count` terminals and include construction of those terminal method-call expressions. Every query case still performs the shipped parse, bind, capability-validation, and Memory execution path; these are not production plan-cache benchmarks.

Memory telemetry is recorded separately from SQL telemetry. History rows include explicit database-construction and seed-row counts plus backend diagnostic deltas for primary-key requests/probes, visited scan rows, predicate evaluations/rejections, cache lookups/hits/misses, materializations, and cache insertions. The SQL query fields remain zero because a Memory operation is not a SQL command.

The direct-`Guid`/typed-ID pair is an end-to-end canonical query-binding comparison with equal row count, match cardinality, and query shape; it is not presented as an isolated converter-only delta. `DataLinq.Memory` does not encode provider wire values, so SQLite/MySQL/MariaDB UUID codecs are deliberately not attributed to this lane.

Run a wiring smoke from the repo root with:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --v09-memory-read --profile smoke --history-json artifacts\benchmarks\history\v0.9-memory-read-smoke.json
```

The smoke profile proves selection, execution, telemetry, and history serialization only. Use a clean-commit default or heavy run before interpreting timings or allocations. Because these cases were introduced after the Memory foundation, their first accepted result is a post-foundation baseline rather than a retroactive before-state.

The first accepted clean heavy checkpoint is commit `24374aa9990b97c85a7a8bb8e7619c7ddfbc8207`, history artifact `artifacts/benchmarks/history/v0.9-memory-read-24374aa9-heavy.json`, run `20260806-000816787-5c9bb4f575e04d5c81002c0f5e2dcbf3`. All nine rows are complete; relative error is `1.36%` to `3.71%` and standard deviation is `1.90%` to `5.32%`. BenchmarkDotNet reports three minimum-iteration warnings—typed ID `83.258` ms, direct `Guid` `87.555` ms, and filter/order/page `80.319` ms—so RE-5 must repeat the exact lane at final RC and preserve or supersede those caveats.

## Phase 10 Key Foundation

Phase 10 key/cache work should use the `phase10-key-foundation` benchmark category to attribute changes that the broader Phase 2 and Phase 3 lanes would otherwise blur together.

That category intentionally contains:

- `WarmGeneratedStaticGet`
  Tracks the generated static primary-key fetch surface after the row cache has already been populated.
- `WarmRelationTraversal`
  Tracks relation traversal after relation and row-cache warmup.
- `ScalarRowCacheAddGetRemove`
  Tracks direct scalar primary-key row-cache add/get/remove operations without SQL execution noise.

Run the lane with:

```bash
dotnet run --project DataLinq.Benchmark.CLI -- run --phase10-key-foundation
```

For quick local smoke validation:

```bash
dotnet run --project DataLinq.Benchmark.CLI -- run --phase10-key-foundation --profile smoke
```

Use the smoke profile only to prove the lane is wired correctly. Use the default or heavy profile before interpreting performance.

## Phase 11 Cache Invalidation

Phase 11 cache clearing and external invalidation work should use the `phase11-cache-invalidation` category to keep invalidation overhead visible without blending it into read hot-path numbers.

That category intentionally contains:

- `InvalidateOneEmployeeRow`
  Tracks repeated provider-key precise row invalidation.
- `InvalidateManyEmployeeRows`
  Tracks one normalized rows invalidation envelope with many provider keys.
- `InvalidateEmployeeTable`
  Tracks conservative table invalidation.
- `InvalidateDatabase`
  Tracks conservative database invalidation across loaded table caches.

Run the lane with:

```bash
dotnet run --project DataLinq.Benchmark.CLI -- run --phase11-cache-invalidation
```

For quick local smoke validation:

```bash
dotnet run --project DataLinq.Benchmark.CLI -- run --phase11-cache-invalidation --profile smoke
```

Use the smoke profile only to prove the lane is wired correctly. Use the default or heavy profile before interpreting performance.

## Provider Selection

The CLI passes through the `DATALINQ_BENCHMARK_PROVIDERS` environment variable.

Example:

```bash
DATALINQ_BENCHMARK_PROVIDERS=sqlite-memory dotnet run --project DataLinq.Benchmark.CLI -- run
```

PowerShell:

```powershell
$env:DATALINQ_BENCHMARK_PROVIDERS='sqlite-memory'
dotnet run --project DataLinq.Benchmark.CLI -- run
```

That is the clean way to narrow provider scope for local trend runs or CI-like validation.

## History And Comparison Evidence

New history artifacts use numeric `SchemaVersion` `3` and named `SchemaId` `v0.9.benchmark-history.v3`. New comparison artifacts use numeric `SchemaVersion` `3` and named `SchemaId` `v0.9.benchmark-comparison.v3`. The numeric and named identities must agree; neither “v3” in a filename nor a successful BenchmarkDotNet process is enough.

Every invocation receives a unique `<timestamp>-<guid>` run id and an exclusive raw-artifact root at `artifacts/benchmarks/runs/<run-id>/`. A pre-existing run root is rejected rather than reused. History records the resolved repository/project/assembly/run paths, profile and expected BenchmarkDotNet job, filter, selected category, normalized providers, build/keep/verbose choices, sanitized pass-through arguments, requested report paths, warning threshold, release intent, command arguments/timing/environment/logs, OS and architecture, runtime and logical-processor count, bounded processor identity, the resolved BenchmarkDotNet version from executed output when available or otherwise from the adjacent dependency assembly, expected and observed targets, row completeness, warnings/failure, and checkout plus runner provenance.

The six canonical release-history matrices are exact:

| Selector | Expected methods | Providers | Expected rows | Required operations per invoke |
| --- | ---: | --- | ---: | --- |
| `--phase2-watch` | 3 | `sqlite-file`, `sqlite-memory` | 6 | provider initialization `1`; startup PK `1`; warm PK `1000` |
| `--phase3-query-hotpath` | 3 | `sqlite-file`, `sqlite-memory` | 6 | `1000` for every method |
| `--v09-query-backend` | 6 | `sqlite-file`, `sqlite-memory` | 12 | `1000` except SQL-adapter scalar `Any` at `3000` |
| `--v09-memory-read` | 9 | `memory` | 9 | `1` for every method |
| `--allocation-regression` | 9 | `sqlite-memory` | 9 | provider/startup `1`; CRUD small `50`; CRUD batch `300`; remaining methods `1000` |
| `--allocation-stages` | 4 | `sqlite-file`, `sqlite-memory` | 8 | `1000` for every method |

Strict history validity requires exactly one of those selectors, `--profile heavy` (`MediumRun`), the default unfiltered `--filter "*"`, the exact provider set above, no `--no-build`, no pass-through BenchmarkDotNet arguments, one complete unique row per expected category/provider/method target, and the exact operation count and selector tracking group for every row. Each row must also carry its real non-`other` scenario category, runtime/job/toolchain identity, finite measurement/allocation data, and a matching complete nonnegative telemetry delta. A focused, filtered, smoke/default-profile, provider-subset, no-build, or pass-through invocation may still be a successful diagnostic run, but it is not release evidence.

`Outcome` and `IsCompleteForInvocation` describe the requested run. `ArtifactsComplete` describes its persisted raw evidence. `ValidForEvidence` is the stricter conjunction of canonical scope, completeness, artifacts, safe paths, and provenance. A complete run may therefore be `Passed` or `ReviewRequired` while `ValidForEvidence` is `false`. Without `--release-evidence`, a complete diagnostic run and a comparable diagnostic comparison exit successfully; incomplete/error history and non-comparable/error comparison exit unsuccessfully. With `--release-evidence`, any invalid requested history or comparison makes the command fail. `ReviewRequired` remains a review gate rather than an automatic execution failure: warnings and changed telemetry must be dispositioned even when the strict artifact is otherwise valid.

History warnings retain bounded sanitized BenchmarkDotNet warnings and add selector-specific telemetry-shape review when a canonical method lacks its expected workload signal. Comparisons require matching profile/filter/target identity. Current-v3 pairs additionally require matching OS, architecture, runtime, logical-processor count, processor identity, BenchmarkDotNet version, selector, expected job, provider set, expected targets, row category, tracking group, operations per invoke, JIT, platform, and toolchain. Per-row latency, allocation, and telemetry statuses are separate. Timing at or above `20%` recorded noise is labeled `noisy`, but that suppresses only the latency verdict: an allocation regression at the configured threshold is still a `warning`, and exact telemetry changes still require review.

History artifact references cover the summary JSON, BenchmarkDotNet CSV/Markdown, one telemetry JSON per row, and every restore/build/benchmark log. Each reference records absolute and repository-relative path, byte length, and SHA-256; `RowAggregateSha256` gives the normalized row set a path-independent identity. Comparison artifacts retain baseline/candidate path, bytes, SHA-256, schema/run/commit/profile/filter/scope identity, row aggregate, legacy status, and source-validity status. The comparison is artifact-complete only while both referenced input files still match their captured hashes and the comparison destination is safe.

Release validity also requires clean, unchanged tooling and benchmark-target checkouts with full commits. `DataLinq.Benchmark.CLI` and `DataLinq.DevTools` must match the tooling checkout; the freshly built `DataLinq.Benchmark` assembly must match the benchmark-target checkout (the same checkout for an ordinary run). The benchmark assembly path and SHA-256 are revalidated. This is why `--no-build` is never a strict-evidence shortcut.

All history, baseline, comparison, raw-log, and BenchmarkDotNet artifact paths are confined beneath the repository `artifacts` tree without reparse-point traversal; the three requested history/baseline/comparison paths must be distinct. JSON is serialized to a fresh sibling temporary file and atomically promoted. Once safe explicit output paths have been normalized, old requested history/comparison files are invalidated before action-level dependency, threshold, category, profile, provider, and baseline validation, so stale green output cannot survive a failed request. Parser or unsafe-path failures happen before that boundary; other early semantic failures, report-write failures, or abrupt process termination may leave no replacement JSON. Ordinary failures after a run identity exists attempt bounded `Error` artifacts. Evidence consumers must therefore require successful command exit and then validate the v3 identity, outcome/completeness, artifact, validity, scope, and provenance fields; file existence alone is never a pass.

Structurally valid schema-v1 and schema-v2 histories remain readable so retained baselines are not discarded. Missing category, tracking-group, and operation metadata is normalized where the old contract permits it, but a legacy source is always `SourceValidForEvidence: false`; a comparable legacy comparison is automatically `ReviewRequired` and can never satisfy strict `--release-evidence`. Generate the final candidate history in a separate strict invocation. Treat any comparison against the retained v2 before-state as diagnostic evidence with an explicit human disposition.

## Artifacts

Artifacts are written under this repo-root path:

```text
artifacts/benchmarks/
```

Important outputs include:

- `runs/<run-id>/results/*-report-github.md`
- `runs/<run-id>/results/*-report.csv`
- `runs/<run-id>/results/<run-id>-*-telemetry.json`
- `runs/<run-id>/results/<run-id>-summary.json`
- `runs/<run-id>/benchmark-restore-*.log`, `benchmark-build-*.log`, and `benchmark-benchmark-*.log`
- `benchmark-list-*.log`
- optional history JSON artifacts
- optional comparison JSON artifacts

Summary, history, and comparison JSON rows include:

- run metadata: profile, commit, branch, runner, workflow, and filter
- row metadata: provider, category, tracking group, operations per invoke, mean, median, error, standard deviation, allocation, uncertainty, and telemetry deltas when available

Comparison artifacts intentionally prefer same-profile baselines. A `default` run should not get its primary regression verdict from a `heavy` run just because that happened to be the latest published artifact.

## Stable CI Lane

The benchmark history lane is intentionally narrower than the full local benchmark surface.

Current policy:

- CI trends the `stable` benchmark category plus the `macro-readwrite` and `macro-bulk` CRUD workflow lanes
- CI currently trends the `sqlite-memory` provider only
- scheduled history runs use the heavier benchmark profile
- published history keeps all recent runs, then thins older runs by age instead of raw run count
- broader or noisier scenarios stay available locally until they are stable enough to deserve regression history

This filtered multi-category workflow is intentionally noncanonical. It records schema-v3 diagnostic history with `ReleaseEvidenceIntent: false`, unknown reconstructed release scope, and `ValidForEvidence: false`; its successful exit and publication are trend telemetry, not a final-RC benchmark gate. Automatic comparison selects only a retained schema-v1/v2 baseline with the exact profile and filter, because the hosted runner's full v3 processor/runtime/BenchmarkDotNet identity is known only after the benchmark runs. If no such diagnostic baseline remains, the workflow publishes the history without a comparison. Strict v3-to-v3 comparison is a separate release operation with exact environment matching.

Macro category policy:

- `macro-readwrite` is reserved for request-sized read/write workflows. The small CRUD workflow is published there because it gives a lighter ordinary-use signal.
- `macro-bulk` is reserved for larger batch workflows. The batch CRUD workflow is published there because it covers the broader read/write path the hot-path microbenchmarks do not.
- Other macro scenarios should stay `experimental` until repeated local and scheduled history says they are boring enough to publish.

That is the right tradeoff. Benchmark history should be boring and trustworthy, not broad and noisy.
