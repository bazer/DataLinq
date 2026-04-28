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
