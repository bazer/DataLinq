# DataLinq Benchmark Harness

Use the cross-platform benchmark CLI in `src/DataLinq.Benchmark.CLI` instead of calling BenchmarkDotNet directly.

It sets up a repo-local CLI environment, restores and builds the harness, runs BenchmarkDotNet with repo-level artifacts, and prints a compact summary table instead of the full BenchmarkDotNet log.

## Common commands

List available benchmarks:

```bash
dotnet run --project ./src/DataLinq.Benchmark.CLI -- list
```

Run the normal short benchmark profile:

```bash
dotnet run --project ./src/DataLinq.Benchmark.CLI -- run
```

Run a focused benchmark:

```bash
dotnet run --project ./src/DataLinq.Benchmark.CLI -- run --filter "*WarmPrimaryKeyFetch*"
```

Run a fast smoke pass:

```bash
dotnet run --project ./src/DataLinq.Benchmark.CLI -- run --profile smoke
```

Skip restore/build when you know the harness is already up to date:

```bash
dotnet run --project ./src/DataLinq.Benchmark.CLI -- run --no-build --filter "*WarmPrimaryKeyFetch*"
```

Show the full underlying restore/build/BenchmarkDotNet output:

```bash
dotnet run --project ./src/DataLinq.Benchmark.CLI -- run --verbose
```

## Artifacts

Benchmark artifacts are written under `artifacts/benchmarks/`.

- `results/*-report-github.md`: concise Markdown summary
- `results/*-report.csv`: machine-readable benchmark data
- `results/*-telemetry.json`: per benchmark/provider telemetry deltas normalized per operation
- `results/*-summary.json`: merged summary artifact with timing, allocations, noise, and telemetry deltas
- `benchmark-run-*.log`: full captured console output for that run
- `benchmark-list-*.log`: full captured console output for a `list` run
