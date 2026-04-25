# DataLinq.Benchmark.CLI

`DataLinq.Benchmark.CLI` is the canonical entry point for the DataLinq benchmark harness.

Use it instead of calling BenchmarkDotNet directly.

## Why It Exists

Direct BenchmarkDotNet invocation is too raw for normal repo use.

The CLI standardizes restore/build behavior, keeps artifacts under repo control, and adds stable history and comparison outputs that are actually useful for regression tracking.

## Commands

### `list`

Lists the available benchmark methods.

```bash
dotnet run --project src/DataLinq.Benchmark.CLI -- list
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
dotnet run --project src/DataLinq.Benchmark.CLI -- run
dotnet run --project src/DataLinq.Benchmark.CLI -- run --filter "*WarmPrimaryKeyFetch*"
dotnet run --project src/DataLinq.Benchmark.CLI -- run --profile smoke
dotnet run --project src/DataLinq.Benchmark.CLI -- run --profile heavy
dotnet run --project src/DataLinq.Benchmark.CLI -- run --phase2-watch
```

Important options:

- `--filter`
  BenchmarkDotNet filter pattern. Defaults to `*`.
- `--profile`
  `default`, `smoke`, or `heavy`.
- `--no-build`
  Reuses the existing benchmark assembly.
- `--keep-files`
  Preserves BenchmarkDotNet-generated temporary files.
- `--verbose`
  Prints the underlying restore/build/BenchmarkDotNet output.
- `--phase2-watch`
  Runs only the Phase 2 benchmark watchpoints.
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
dotnet run --project src/DataLinq.Benchmark.CLI -- run -- --anyCategories stable
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
dotnet run --project src/DataLinq.Benchmark.CLI -- run --phase2-watch
```

For quick local smoke validation, combine the category with the dry profile:

```bash
dotnet run --project src/DataLinq.Benchmark.CLI -- run --phase2-watch --profile smoke
```

The dry profile is useful for checking harness wiring. It is not a trustworthy performance result.

## Provider Selection

The CLI passes through the `DATALINQ_BENCHMARK_PROVIDERS` environment variable.

Example:

```bash
DATALINQ_BENCHMARK_PROVIDERS=sqlite-memory dotnet run --project src/DataLinq.Benchmark.CLI -- run
```

PowerShell:

```powershell
$env:DATALINQ_BENCHMARK_PROVIDERS='sqlite-memory'
dotnet run --project src/DataLinq.Benchmark.CLI -- run
```

That is the clean way to narrow provider scope for local trend runs or CI-like validation.

## Artifacts

Artifacts are written under:

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

Summary, history, and comparison JSON rows include `TrackingGroup` when a benchmark belongs to a narrower decision lane such as `phase2-watch`.

## Stable CI Lane

The benchmark history lane is intentionally narrower than the full local benchmark surface.

Current policy:

- CI trends the `stable` benchmark category
- CI currently trends the `sqlite-memory` provider only
- scheduled history runs use the heavier benchmark profile
- broader or noisier scenarios stay available locally until they are stable enough to deserve regression history

That is the right tradeoff. Benchmark history should be boring and trustworthy, not broad and noisy.
