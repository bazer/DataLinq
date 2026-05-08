# DataLinq.Dev.CLI

`DataLinq.Dev.CLI` is the repo-local wrapper for `dotnet` restore, build, test, environment diagnosis, and controlled passthrough execution.

Use it when you want a stable execution profile, concise output, and predictable artifacts.

## Why It Exists

Raw `dotnet` is a bad default for this repo when you care about repeatability.

The wrapper normalizes repo-local execution roots, keeps logs under `artifacts/dev/`, and gives you output modes that are usable in both normal terminal work and agent-driven workflows.

## Commands

### `doctor`

Diagnoses the local `dotnet` and NuGet execution environment.

```bash
dotnet run --project src/DataLinq.Dev.CLI -- doctor --profile repo
```

What it checks:

- repo-local execution roots
- writable tool paths
- `dotnet --version`
- installed SDK count
- `dotnet --info`
- workload resolver and workload auto-import presence
- NuGet sources from the repo-local `NuGet.Config`
- top-level cached package roots

Use this first when the environment looks suspicious.

### `restore`

Runs `dotnet restore` with the repo-local execution profile.

```bash
dotnet run --project src/DataLinq.Dev.CLI -- restore
dotnet run --project src/DataLinq.Dev.CLI -- restore src/DataLinq.sln --output summary
```

### `build`

Runs `dotnet build` with concise default output.

```bash
dotnet run --project src/DataLinq.Dev.CLI -- build
dotnet run --project src/DataLinq.Dev.CLI -- build src/DataLinq.sln --output errors
```

Useful options:

- `--configuration`
  Defaults to `Debug`.
- `--framework`
  Optional target framework.
- `--no-restore`
  Skips restore before build.
- `--binlog auto|always|never`
  Controls binary log generation.

### `test`

Runs `dotnet test` with concise failure-focused output.

```bash
dotnet run --project src/DataLinq.Dev.CLI -- test src/DataLinq.Tests.Unit/DataLinq.Tests.Unit.csproj
dotnet run --project src/DataLinq.Dev.CLI -- test src/DataLinq.Generators.Tests/DataLinq.Generators.Tests.csproj --output failures
```

Useful options:

- `--configuration`
  Defaults to `Debug`.
- `--framework`
  Optional target framework.
- `--filter`
  Standard `dotnet test` filter expression.
- `--no-build`
  Skips build before test.
- `--no-restore`
  Skips restore before test.

The optional target defaults to `src/DataLinq.sln`.

### `size-report`

Publishes the Phase 8C constrained-platform smoke targets and writes a repeatable compatibility payload report.

```bash
dotnet run --project src/DataLinq.Dev.CLI -- size-report --target phase8c
dotnet run --project src/DataLinq.Dev.CLI -- size-report --targets aot,trim --no-restore
dotnet run --project src/DataLinq.Dev.CLI -- size-report --targets wasm,wasm-aot --format markdown
```

The default `phase8c` target set includes:

- `aot`
  Native AOT publish of `src/DataLinq.AotSmoke`.
- `trim`
  trimmed self-contained publish of `src/DataLinq.TrimSmoke`.
- `wasm`
  no-AOT Blazor WebAssembly publish of `src/DataLinq.BlazorWasm`.
- `wasm-aot`
  Blazor WebAssembly AOT publish of `src/DataLinq.BlazorWasm`.

Each report includes total payload size, symbol-excluded size, file count, `.br` and `.gz` asset totals, largest files, publish warnings grouped by owner, smoke status, and banned Roslyn payload findings.

Useful options:

- `--targets`
  Limits the run to `aot`, `trim`, `wasm`, `wasm-aot`, or a comma-separated subset.
- `--runtime`
  Runtime identifier for native publish targets. Defaults to the current OS and architecture.
- `--top`
  Number of largest files to list per target.
- `--max-total-size-mb`, `--max-symbol-excluded-size-mb`, `--max-file-count`
  Advisory thresholds. Exceeding them is reported as a warning.
- `--fail-on-threshold`
  Makes advisory threshold findings fail the command.
- `--fail-on-banned-payload`
  Makes banned Roslyn payload findings fail the command. Keep this off until the runtime package split has landed.
- `--skip-smoke`
  Skips executable smoke runs after publish. Browser WebAssembly smoke is reported as not automated by this command.
- `--format summary|markdown|json`
  Controls console output. The JSON and Markdown artifacts are always written.

Reports are written under `artifacts/dev/compat-size-report/<timestamp>/` as `report.json` and `report.md`. Raw publish and smoke logs are also written under `artifacts/dev/`.

### `exec`

Runs an arbitrary `dotnet` command through the same repo-local execution profile.

```bash
dotnet run --project src/DataLinq.Dev.CLI -- exec -- --info
dotnet run --project src/DataLinq.Dev.CLI -- exec -- build src/DataLinq.sln -c Release
```

This is the escape hatch, not the main workflow.

Prefer the dedicated commands unless you actually need a command surface the wrapper does not expose directly.

## Execution Profiles

Supported profiles:

- `auto`
  Default. Resolves the best profile for the current environment.
- `repo`
  Normal repo-local execution.
- `sandbox`
  Intended for constrained or offline-ish environments.
- `ci`
  CI-oriented execution profile.

## Output Modes

Supported output modes:

- `quiet`
  Default. One-line success and concise failure.
- `summary`
  Adds a slightly richer summary.
- `errors`
  Focuses on distinct compiler and NuGet errors.
- `failures`
  Focuses on test failures and failing command summaries.
- `raw`
  Prints the underlying command output.
- `diag`
  Uses diagnostic verbosity and preserves full detail in artifacts.

## Targets and Additional Arguments

`restore`, `build`, and `test` all accept an optional target path.

If you omit it, the default target is `src/DataLinq.sln`.

Each command also accepts extra `dotnet` arguments after `--`.

Example:

```bash
dotnet run --project src/DataLinq.Dev.CLI -- build src/DataLinq.sln -- --no-incremental
```

## Artifacts

Artifacts are written under `artifacts/dev/`.

Build runs can also emit binary logs depending on the selected `--binlog` mode.

If you need the full raw output, the artifact logs are the first place to look. They are the source of truth, not whatever condensed line happened to print to the terminal.
