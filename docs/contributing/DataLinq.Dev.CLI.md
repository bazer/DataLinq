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

Publishes the constrained-platform smoke targets and writes a repeatable compatibility payload report. `--target` selects a target set and still defaults to the historical `phase8c` set so existing commands keep their four-target SQLite behavior. Use `--target v0.9` for the explicit eight-target SQLite/Memory release catalog.

```bash
dotnet run --project src/DataLinq.Dev.CLI -- size-report --target phase8c
dotnet run --project src/DataLinq.Dev.CLI -- size-report --target v0.9 --targets memory --format markdown
dotnet run --project src/DataLinq.Dev.CLI -- size-report --target v0.9 --targets aot,trim
dotnet run --project src/DataLinq.Dev.CLI -- size-report --target v0.9 --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
```

The default `phase8c` target set preserves these original target ids and project graphs:

- `native-aot`
  Native AOT publish of `src/DataLinq.AotSmoke`.
- `trimmed`
  trimmed self-contained publish of `src/DataLinq.TrimSmoke`.
- `wasm`
  no-AOT Blazor WebAssembly publish of `src/DataLinq.BlazorWasm`.
- `wasm-aot`
  Blazor WebAssembly AOT publish of `src/DataLinq.BlazorWasm`.

The `v0.9` target set adds backend identity and uses these exact ids:

- `sqlite-native-aot`
- `sqlite-trimmed`
- `sqlite-wasm-no-aot`
- `sqlite-wasm-aot`
- `memory-native-aot`
- `memory-trimmed`
- `memory-wasm-no-aot`
- `memory-wasm-aot`

`--targets` accepts any exact id from the selected set. The `aot`, `trim`, `wasm`, and `wasm-aot` mode aliases select matching targets in that set; `sqlite` and `memory` select a runtime graph when that graph exists in the set; and `all` or the selected set name selects the complete set. Selections are deduplicated and emitted in catalog order. Alias spellings keep their alias meaning even when they overlap a historical id, while an exact id that is neither present nor a recognized alias is rejected.

Each newly generated report uses schema `v0.9.compatibility-size-report.v2` and records runtime-graph identity, total payload size, symbol-excluded size, file count, `.br` and `.gz` asset totals, largest files, publish warnings grouped by owner, warning diagnostics, smoke status, an explicit payload-inspection status, and target-specific banned-runtime findings. `TargetSet` records the canonical catalog id even when the CLI input uses different casing. `SelectedTargetIds` records the resolved request, `ExpectedTargetCount` records the complete selected-set cardinality, and `IsFullTargetSet` is true only when the reports actually produced exactly match that complete set; a selector subset or early stop is therefore never labeled full evidence. Summary failures are partitioned into product publish failures, product smoke failures, product inspection failures, environment failures, and unsupported observations. Every failed or unsupported required target remains a hard report failure; environment classification explains the failure and does not turn incomplete release evidence green. A failed inspection preserves any publish, smoke, payload, threshold, or warning result that completed before the fault instead of relabeling it as a publish failure.

Every catalog target publishes through a stable, canonical-target-set-qualified `--artifacts-path` under `artifacts/dev/compat-size-build/<target-set>/<target-id>`. This keeps Native AOT, trimming, WebAssembly no-AOT, and WebAssembly AOT intermediates separate even when two targets share one project. The report records that location as `BuildScratchDirectory`: it is mutable build cache, not timestamped release evidence. Same-target clean/publish operations take an exclusive cross-process lock; different targets remain independent. Each invocation receives a collision-resistant timestamp-and-GUID report root so concurrent processes cannot share or overwrite evidence.

Native executable targets run their published executable as the smoke. WebAssembly targets are served over local HTTP and opened in a headless Chromium-compatible browser through Playwright. Both SQLite and Memory browser hosts expose the same neutral smoke contract. The JSON and Markdown reports retain whether that contract was present, final status and stage, window-console entries, Playwright-console entries, and page errors. A no-AOT failure is a required-target failure rather than an automatic unsupported downgrade. Set `DATALINQ_BROWSER_PATH` when Edge, Chrome, or Chromium is not discoverable from the standard install paths or `PATH`.

Roslyn payload rules apply to every graph. Memory targets additionally scan both relative paths and binary/text content for `DataLinq.SQLite`, `DataLinq.MySql`, `Microsoft.Data.Sqlite`, `MySqlConnector`, `SQLitePCLRaw`, and `e_sqlite3`; the same provider tokens are legitimate in the SQLite graph and are not globally banned.

Useful options:

- `--targets`
  Limits the chosen set by exact target id or the `aot`, `trim`, `wasm`, `wasm-aot`, `sqlite`, `memory`, or `all` aliases. Comma-separated selectors may be combined.
- `--runtime`
  Runtime identifier for native publish targets. Defaults to the current OS and architecture.
- `--top`
  Number of largest files to list per target.
- `--max-total-size-mb`, `--max-symbol-excluded-size-mb`, `--max-file-count`
  Advisory thresholds. Exceeding them is reported as a warning.
- `--fail-on-threshold`
  Makes advisory threshold findings fail the command.
- `--fail-on-banned-payload`
  Makes target-specific banned runtime payload findings fail the command. Use this for release payload gates after the package graph has been refreshed.
- `--stop-on-publish-failure`
  Stops the report after a publish failure instead of continuing to later targets.
- `--skip-smoke`
  Skips executable and browser smoke runs after publish.
- `--no-restore`
  Reuses restore assets already present in the selected targets' isolated scratch roots. Run those targets once without this option first. It cannot be combined with `--clean-output`.
- `--clean-output`
  Deletes each selected target's isolated scratch root before publishing, then restores and rebuilds its complete transitive graph. Cleanup refuses a target reached through a symlink, junction, or other reparse point below the artifact root. Source-project `bin` and `obj` directories are not the release-evidence boundary.
- `--release-thresholds`
  Applies the shared, version-neutral compatibility guardrails by publish mode: Native AOT executable, Native AOT symbol-excluded folder, trimmed symbol-excluded folder, no-AOT Brotli assets, and WASM AOT Brotli assets.
- `--format summary|markdown|json`
  Controls console output. The JSON and Markdown artifacts are always written.

Reports are written under `artifacts/dev/compat-size-report/<timestamp>-<guid>/` as `report.json` and `report.md`. Raw publish logs are written under `artifacts/dev/`; target-specific browser smoke logs are written under the target folder inside the report directory. Catalog registration and focused tooling tests do not by themselves prove that all eight `v0.9` targets publish or execute; only a recorded fresh report can make that evidence claim.

### `package-report`

Inspects packed NuGet output for the public package set.

```bash
dotnet run --project DataLinq.Dev.CLI -- package-report --package-dir artifacts/nuget-release/<timestamp>
dotnet run --project DataLinq.Dev.CLI -- package-report --package-dir artifacts/nuget-release/<timestamp> --format markdown
```

Use this after `publish-nuget.ps1 -PackOnly` or another fresh pack output directory. Use a new, empty output directory for each pack: `publish-nuget.ps1` rejects a non-empty output directory when it is packing so stale candidates cannot contaminate release evidence. `-SkipPack` is the explicit reuse path. Do not point `package-report` at a long-lived package cache; duplicate, unexpected, or version-skewed packages are findings on purpose.

The default expected package set is:

- `DataLinq`
- `DataLinq.SQLite`
- `DataLinq.MySql`
- `DataLinq.Memory`
- `DataLinq.CLI`
- `DataLinq.Tools`

The default runtime package set is narrower:

- `DataLinq`
- `DataLinq.SQLite`
- `DataLinq.MySql`
- `DataLinq.Memory`

For every package, the report checks:

- every expected public package is present
- expected public packages all use the same version
- no unexpected package ids are present
- duplicate package ids are rejected
- every `.nupkg` has a matching `.snupkg`
- `.snupkg` files are inventoried independently, and orphan or duplicate symbol-package ids are rejected
- package filenames match the nuspec id and version, and symbol-package id/version match the runtime package
- nuspec id, version, description, repository type/URL/commit, license type/file, and readme are present
- repository metadata identifies the DataLinq GitHub repository, the license is the root `LICENSE.md`, and the package readme is the root `README.md`
- both `LICENSE.md` and `README.md` are present as root package assets
- runtime package dependency groups do not reference `Microsoft.CodeAnalysis.*`
- runtime package dependency groups do not reference `Remotion.Linq`
- runtime package `lib/` and `runtimes/` assets do not contain Roslyn payloads
- runtime package `lib/` and `runtimes/` assets do not contain Remotion payloads
- the `DataLinq` source generator lives under `analyzers/dotnet/cs`
- analyzer payloads are not placed under runtime assets

`DataLinq.Memory` has an additional fail-closed package policy:

- its description must be exactly `Experimental read-only in-memory backend for generated DataLinq models.`
- its runtime archive must contain exactly `lib/net8.0/DataLinq.Memory.dll`, `lib/net9.0/DataLinq.Memory.dll`, and `lib/net10.0/DataLinq.Memory.dll`
- its symbol archive must contain exactly the corresponding three `DataLinq.Memory.pdb` files
- its runtime and symbol archives use explicit allowlists: the required assemblies or PDBs, their matching nuspec, the runtime license/readme, and standard NuGet structural or signature metadata are allowed; every other entry is rejected
- each expected runtime DLL must contain valid CLI assembly metadata and have the assembly definition name `DataLinq.Memory`
- it must have exactly one dependency group for each of `net8.0`, `net9.0`, and `net10.0`, with no other groups
- each dependency group must contain only one `DataLinq` dependency at the exact Memory package version, with exactly `Build,Analyzers` excluded
- analyzer, runtime, build, build-transitive, tool, and native assets are forbidden
- all non-empty runtime and symbol entries are checked for PE, ELF, Mach-O, WebAssembly, and static-archive signatures; only the validated managed DLLs at the three expected runtime paths are permitted executable images
- dependency ids, asset paths, and managed library contents are checked for `DataLinq.SQLite`, `DataLinq.MySql`, `Microsoft.Data.Sqlite`, `MySqlConnector`, `SQLitePCLRaw`, `e_sqlite3`, `Microsoft.CodeAnalysis`, `Remotion.Linq`, and `DataLinq.Generators`
- generator assets remain owned by the core `DataLinq` package; `DataLinq.Memory` must not duplicate them

The Memory-specific identity, metadata, framework, dependency, exclusion, asset, and banned-payload findings are always hard failures. The `--allow-*` switches below relax only their named general package-report policy; they do not weaken the `DataLinq.Memory` package contract.

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
- `--allow-runtime-remotion`
  Reports runtime Remotion package dependencies or payload assets without failing.
- `--allow-analyzer-leaks`
  Reports missing or misplaced analyzer assets without failing.
- `--format summary|markdown|json`
  Controls console output. The JSON and Markdown artifacts are always written.

Reports use schema `v0.9.package-inspection-report.v3` and are written under `artifacts/dev/package-report/<timestamp>/` as `report.json` and `report.md`.

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
