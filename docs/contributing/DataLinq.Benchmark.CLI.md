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

## Stable CI Lane

The benchmark history lane is intentionally narrower than the full local benchmark surface.

Current policy:

- CI trends the `stable` benchmark category
- CI currently trends the `sqlite-memory` provider only
- scheduled history runs use the heavier benchmark profile
- broader or noisier scenarios stay available locally until they are stable enough to deserve regression history

That is the right tradeoff. Benchmark history should be boring and trustworthy, not broad and noisy.
