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
```

Important options:

- `--filter`
  BenchmarkDotNet filter pattern. Defaults to `*`.
- `--profile`
  `default`, `smoke`, or `heavy`. The wrapper selects one configured BenchmarkDotNet job for the chosen profile.
- `--no-build`
  Reuses the existing benchmark assembly.
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
- `--history-json`
  Writes a stable benchmark history entry JSON artifact.
- `--baseline`
  Compares the current run against an earlier history JSON artifact.
- `--comparison-json`
  Writes a machine-readable comparison artifact.
- `--warning-threshold-percent`
  Controls the percent regression threshold for comparison warnings.

Additional BenchmarkDotNet arguments can be passed after `--`.

Example:

```bash
dotnet run --project DataLinq.Benchmark.CLI -- run -- --anyCategories stable macro-readwrite macro-bulk
```

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

This lane establishes the first post-foundation baseline for these exact cases. It is not a retroactive pre-foundation comparison, and it does not by itself close W10/RE-1G: the separate `DataLinq.Memory` read lane and final release-evidence run remain required.

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

## Artifacts

Artifacts are written under this repo-root path:

```text
artifacts/benchmarks/
```

Important outputs include:

- `results/*-report-github.md`
- `results/*-report.csv`
- `results/*-telemetry.json`
- `results/*-summary.json`
- `benchmark-run-*.log`
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

Macro category policy:

- `macro-readwrite` is reserved for request-sized read/write workflows. The small CRUD workflow is published there because it gives a lighter ordinary-use signal.
- `macro-bulk` is reserved for larger batch workflows. The batch CRUD workflow is published there because it covers the broader read/write path the hot-path microbenchmarks do not.
- Other macro scenarios should stay `experimental` until repeated local and scheduled history says they are boring enough to publish.

That is the right tradeoff. Benchmark history should be boring and trustworthy, not broad and noisy.
