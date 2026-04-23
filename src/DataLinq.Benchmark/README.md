# DataLinq Benchmark Harness

Use `src/DataLinq.Benchmark.CLI` instead of calling BenchmarkDotNet directly.

The canonical documentation now lives in:

- [`docs/contributing/DataLinq.Benchmark.CLI.md`](../../docs/contributing/DataLinq.Benchmark.CLI.md)
- [`docs/contributing/Internal Tooling.md`](../../docs/contributing/Internal%20Tooling.md)

Keep this README as a quick local pointer, not as a duplicate long-form tool manual.

## Quick Start

```bash
dotnet run --project ./src/DataLinq.Benchmark.CLI -- list
dotnet run --project ./src/DataLinq.Benchmark.CLI -- run
dotnet run --project ./src/DataLinq.Benchmark.CLI -- run --filter "*WarmPrimaryKeyFetch*"
```

Benchmark artifacts are written under `artifacts/benchmarks/`.
