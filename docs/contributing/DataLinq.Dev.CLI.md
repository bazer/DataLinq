# DataLinq.Dev.CLI

`DataLinq.Dev.CLI` is the repo-local wrapper for `dotnet` restore, build, test, environment diagnosis, and controlled passthrough execution.

Use it when you want a stable execution profile, concise output, and predictable artifacts.

## Why It Exists

Raw `dotnet` is a bad default for this repo when you care about repeatability.

The wrapper normalizes repo-local execution roots, keeps logs under `artifacts/dev/`, and gives you output modes that are usable in both normal terminal work and agent-driven workflows.

## Commands

The command examples assume your current directory is the repo's `src` folder. The Dev CLI runs inner `dotnet` commands from the repo root, so explicit target paths passed to the Dev CLI are still repo-root-relative.

### `doctor`

Diagnoses the local `dotnet` and NuGet execution environment.

```bash
dotnet run --project DataLinq.Dev.CLI -- doctor --profile repo
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
dotnet run --project DataLinq.Dev.CLI -- restore
dotnet run --project DataLinq.Dev.CLI -- restore --output summary
```

### `build`

Runs `dotnet build` with concise default output.

```bash
dotnet run --project DataLinq.Dev.CLI -- build
dotnet run --project DataLinq.Dev.CLI -- build --output errors
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
dotnet run --project DataLinq.Dev.CLI -- test src/DataLinq.Tests.Unit/DataLinq.Tests.Unit.csproj
dotnet run --project DataLinq.Dev.CLI -- test src/DataLinq.Generators.Tests/DataLinq.Generators.Tests.csproj --output failures
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
dotnet run --project DataLinq.Dev.CLI -- size-report --target phase8c
dotnet run --project DataLinq.Dev.CLI -- size-report --targets aot,trim --no-restore
dotnet run --project DataLinq.Dev.CLI -- size-report --targets wasm,wasm-aot --format markdown
dotnet run --project DataLinq.Dev.CLI -- size-report --targets wasm-aot --clean-output --release-thresholds
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

Each report includes total payload size, symbol-excluded size, file count, `.br` and `.gz` asset totals, largest files, publish warnings grouped by owner, warning diagnostics, smoke status, and banned Roslyn payload findings.

Native executable targets run their published executable as the smoke. WebAssembly targets are served over local HTTP and opened in a headless Chromium-compatible browser through Playwright. Set `DATALINQ_BROWSER_PATH` when Edge, Chrome, or Chromium is not discoverable from the standard install paths or `PATH`.

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
  Makes banned Roslyn payload findings fail the command. Use this for the Phase 8C runtime payload gate after the package graph has been refreshed.
- `--skip-smoke`
  Skips executable and browser smoke runs after publish.
- `--clean-output`
  Deletes `bin` and `obj` for the selected target projects before publishing. Use this for fresh WebAssembly warning evidence because incremental publishes can hide `WASM0001`.
- `--release-thresholds`
  Applies the 0.8 target-specific payload thresholds: Native AOT executable, Native AOT symbol-excluded folder, trimmed symbol-excluded folder, no-AOT Brotli assets, and WASM AOT Brotli assets.
- `--format summary|markdown|json`
  Controls console output. The JSON and Markdown artifacts are always written.

Reports are written under `artifacts/dev/compat-size-report/<timestamp>/` as `report.json` and `report.md`. Raw publish logs are written under `artifacts/dev/`; target-specific browser smoke logs are written under the target folder inside the report directory.

### `package-report`

Inspects packed NuGet output for the public package set.

```bash
dotnet run --project DataLinq.Dev.CLI -- package-report --package-dir artifacts/nuget-release/<timestamp>
dotnet run --project DataLinq.Dev.CLI -- package-report --package-dir artifacts/nuget-release/<timestamp> --format markdown
```

Use this after `publish-nuget.ps1 -PackOnly` or another fresh pack output directory. Do not point it at a long-lived package cache if you want release evidence; stale packages make the report noisy on purpose.

The default expected package set is:

- `DataLinq`
- `DataLinq.SQLite`
- `DataLinq.MySql`
- `DataLinq.CLI`
- `DataLinq.Tools`

The default runtime package set is narrower:

- `DataLinq`
- `DataLinq.SQLite`
- `DataLinq.MySql`

The report checks:

- every expected public package is present
- no unexpected package ids are present
- every `.nupkg` has a matching `.snupkg`
- runtime package dependency groups do not reference `Microsoft.CodeAnalysis.*`
- runtime package `lib/` and `runtimes/` assets do not contain Roslyn payloads
- the `DataLinq` source generator lives under `analyzers/dotnet/cs`
- analyzer payloads are not placed under runtime assets

Useful options:

- `--expected-packages`
  Overrides the public package set with a comma-separated list, or `public`.
- `--runtime-packages`
  Overrides the runtime package set with a comma-separated list, or `runtime`.
- `--allow-unexpected-packages`
  Reports unexpected package ids without failing.
- `--allow-missing-symbols`
  Reports missing `.snupkg` files without failing.
- `--allow-runtime-roslyn`
  Reports runtime Roslyn package dependencies or payload assets without failing.
- `--allow-analyzer-leaks`
  Reports missing or misplaced analyzer assets without failing.
- `--format summary|markdown|json`
  Controls console output. The JSON and Markdown artifacts are always written.

Reports are written under `artifacts/dev/package-report/<timestamp>/` as `report.json` and `report.md`.

### `exec`

Runs an arbitrary `dotnet` command through the same repo-local execution profile.

```bash
dotnet run --project DataLinq.Dev.CLI -- exec -- --info
dotnet run --project DataLinq.Dev.CLI -- exec -- build src/DataLinq.sln -c Release
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
dotnet run --project DataLinq.Dev.CLI -- build src/DataLinq.sln -- --no-incremental
```

## Artifacts

Artifacts are written under `artifacts/dev/`.

Build runs can also emit binary logs depending on the selected `--binlog` mode.

If you need the full raw output, the artifact logs are the first place to look. They are the source of truth, not whatever condensed line happened to print to the terminal.
