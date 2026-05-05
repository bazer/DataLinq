# Internal Tooling

This page is the canonical map for the repo-local developer tooling.

If you are working inside the DataLinq repo, these are the tools that matter:

If you are setting up a machine for the first time, start with [Dev and Test Environment](Dev%20and%20Test%20Environment.md) before diving into individual tool behavior.

| Tool | Use it for | Do not use it for |
| --- | --- | --- |
| `DataLinq.Dev.CLI` | repo-local `dotnet` execution, environment diagnosis, restore, build, direct test wrapping | provider-matrix orchestration or benchmarks |
| `DataLinq.Testing.CLI` | test infrastructure lifecycle, target aliases, batched provider runs, suite orchestration | normal restore/build workflows |
| `DataLinq.Benchmark.CLI` | benchmark discovery, benchmark runs, history artifacts, regression comparison | ordinary tests or direct build workflows |

## Why These Tools Exist

The repo has already paid the price for ad hoc tooling:

- raw `dotnet` output is noisy
- environment behavior is inconsistent across local machines, CI, and sandboxed agents
- Podman orchestration and provider selection are too easy to get subtly wrong
- benchmark runs are only useful when their inputs and artifacts are stable

These tools exist to make the sane path the default path.

## Start Here

Use this decision table:

| You want to... | Tool |
| --- | --- |
| check whether the local `dotnet` environment is sane | [`DataLinq.Dev.CLI`](DataLinq.Dev.CLI.md) |
| restore or build the repo with repo-local caches and concise output | [`DataLinq.Dev.CLI`](DataLinq.Dev.CLI.md) |
| run a single project or solution through `dotnet test` | [`DataLinq.Dev.CLI`](DataLinq.Dev.CLI.md) |
| bring test containers up or down | [`DataLinq.Testing.CLI`](DataLinq.Testing.CLI.md) |
| run `quick`, `latest`, or `all` target aliases | [`DataLinq.Testing.CLI`](DataLinq.Testing.CLI.md) |
| run or compare benchmarks | [`DataLinq.Benchmark.CLI`](DataLinq.Benchmark.CLI.md) |

## Native Windows Sandbox Build Notes

The sandboxed path is still the default first attempt, but two failure modes are easy to misread.

### Restore and Package Cache

`.\scripts\dotnet-sandbox.ps1` intentionally redirects .NET and NuGet state into the repo-local `.dotnet-cli/` tree. On a cold cache, sandboxed restore can fail with warnings/errors like:

```text
NU1801: Unable to load the service index for source https://api.nuget.org/v3/index.json.
NU1101: Unable to find package Spectre.Console.
NU1101: Unable to find package System.CommandLine.
```

That does not mean those project references are wrong. It means the sandbox could not reach NuGet.org and the package was not already in the workspace-local cache. Retry the same restore command outside the sandbox, then return to sandboxed build/test work.

### WebAssembly Task Host

`src/DataLinq.BlazorWasm/DataLinq.BlazorWasm.csproj` currently exercises the .NET WebAssembly SDK. In the Codex sandbox on native Windows, the WebAssembly build can fail while running `MarshalingPInvokeScanner`:

```text
MSB4216: Could not run the "MarshalingPInvokeScanner" task because MSBuild could not create or connect to a task host with runtime "NET" and architecture "x64".
MSB4027: The "MarshalingPInvokeScanner" task generated invalid items from the "IncompatibleAssemblies" output parameter.
```

The full solution build may surface the same underlying problem less helpfully as:

```text
Build FAILED.
    0 Warning(s)
    0 Error(s)
```

When that happens, do not burn time hunting phantom compiler errors. Verify the Blazor WebAssembly project, or the whole solution, outside the sandbox. If it succeeds there, document the sandbox limitation in your final note and continue with source-level warnings or failures separately.

The `WASM0001` warnings emitted by the successful outside-sandbox build are different. They are real SDK warnings about `SQLitePCLRaw.provider.e_sqlite3` exposing varargs native SQLite functions that WebAssembly cannot call safely. Treat those as product/runtime compatibility work, not as sandbox noise.

## Canonical Tool Pages

- [DataLinq.Dev.CLI](DataLinq.Dev.CLI.md)
- [DataLinq.Testing.CLI](DataLinq.Testing.CLI.md)
- [DataLinq.Benchmark.CLI](DataLinq.Benchmark.CLI.md)

## Artifact Conventions

The tools deliberately write artifacts into predictable repo-local locations:

- `artifacts/dev/`
  restore/build/test wrapper artifacts and binary logs
- `artifacts/testdata/`
  test runtime state and test CLI logs
- `artifacts/benchmarks/`
  benchmark reports, summaries, comparisons, telemetry, and raw logs

Those paths are part of the workflow. If you bypass the tools, you usually also bypass the artifacts that make failures diagnosable later.
