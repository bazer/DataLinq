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

Publishes the constrained-platform smoke targets and writes a repeatable compatibility payload report. `--target` selects a target set and still defaults to the historical `phase8c` set so existing commands keep their four-target SQLite behavior. Use `--target v0.9` for the explicit eight-target SQLite/Memory release catalog. That catalog uses source-project references by default; pair `--package-dir` with `--version` when the release gate must exercise an exact local package candidate instead.

```bash
dotnet run --project src/DataLinq.Dev.CLI -- size-report --target phase8c
dotnet run --project src/DataLinq.Dev.CLI -- size-report --target v0.9 --targets memory --format markdown
dotnet run --project src/DataLinq.Dev.CLI -- size-report --target v0.9 --targets aot,trim
dotnet run --project src/DataLinq.Dev.CLI -- size-report --target v0.9 --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
dotnet run --project src/DataLinq.Dev.CLI -- size-report --target v0.9 --package-dir artifacts/nuget-release/<exact-version> --version <exact-version> --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
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

Each newly generated report uses schema `v0.9.compatibility-size-report.v5` and records dependency source; the tooling profile and clean/restore/smoke/threshold/failure-policy invocation inputs; the entry CLI and DevTools assembly names, informational versions, embedded repository commits, and build-time repository states; start and end checkout commit, dirty state, and status SHA-256; runtime-graph identity; total payload size; symbol-excluded size; file count; `.br` and `.gz` asset totals; largest files; publish warnings grouped by owner; warning diagnostics; smoke status; an explicit payload-inspection status; and target-specific banned-runtime findings. Normal CLI and DevTools builds embed `clean`, `dirty`, or `unknown` from an explicit build-time Git status sample and make that value part of generated assembly-info inputs, so changing repository state forces recompilation even when source timestamps do not change. A missing, invalid, or non-clean build-state attestation, a missing or ambiguous assembly revision, an unexpected runner assembly, either assembly revision not matching the stable checkout commit, a repository-state capture failure, a dirty worktree at either report sample, or start/end drift is a hard report failure. This makes both an older `dotnet run --no-build` binary and a same-commit binary built from dirty source visible and invalid for evidence when the current checkout is clean. `TargetSet` records the canonical catalog id even when the CLI input uses different casing. `SelectedTargetIds` records the resolved request, `ExpectedTargetCount` records the complete selected-set cardinality, and `IsFullTargetSet` is true only when the reports actually produced exactly match that complete set; a selector subset or early stop is therefore never labeled full evidence. Summary failures are partitioned into product publish failures, product smoke failures, product inspection failures, environment failures, unsupported observations, and runner-state failures. Every failed or unsupported required target remains a hard report failure; environment classification explains the failure and does not turn incomplete release evidence green. A failed inspection preserves any publish, smoke, payload, threshold, warning, or package-provenance result that completed before the fault instead of relabeling it as a publish failure.

Every source-project catalog target publishes through a stable, canonical-target-set-qualified `--artifacts-path` under `artifacts/dev/compat-size-build/<target-set>/<target-id>`. Package-backed targets add a candidate-byte identity, producing `artifacts/dev/compat-size-build/v0.9/packed-pkg-<identity>/<target-id>`. This keeps different candidates and Native AOT, trimming, WebAssembly no-AOT, and WebAssembly AOT intermediates separate even when targets share one project. The report records that location as `BuildScratchDirectory`: it is mutable build cache, not timestamped release evidence. Source targets lock independently; package runs additionally hold one candidate-context lock across cache preparation, every selected publish/audit, and report creation, so the same candidate cannot be reset underneath another run. Different candidate identities remain independent. Each invocation receives a collision-resistant timestamp-and-GUID report root so concurrent processes cannot share or overwrite evidence.

Package mode validates the exact six public runtime packages before creating report artifacts. Every package must have the requested exact version and the same nonblank nuspec repository commit, and the report retains its canonical path, byte size, lowercase SHA-256, commit, and an aggregate candidate identity; symbol packages do not affect the runtime identity. Restore runs with an isolated generated `NuGet.Config`, package cache, user profile, temporary directories, and inherited MSBuild/NuGet redirection variables cleared. After publish and before smoke, the reporter audits the host `project.assets.json`: its active TFM/RID graph must contain the exact tracked shared smoke project plus exact core and graph-provider packages; no same-named substitute project is accepted; each package must resolve from the candidate directory; the cached archive must match the selected SHA-256; and every extracted package file listed by NuGet must match that archive byte-for-byte. Package contexts, cache paths, project references, and extracted files reject reparse traversal. Any provenance finding skips smoke and becomes a hard `PackageProvenance` inspection failure. The package directory must not overlap `artifacts/dev`, and package-backed mode is deliberately rejected for the historical `phase8c` graph.

Native executable targets run their published executable as the smoke. WebAssembly targets are served over local HTTP and opened in a headless Chromium-compatible browser through Playwright. Both SQLite and Memory browser hosts expose the same neutral smoke contract. The JSON and Markdown reports retain whether that contract was present, final status and stage, window-console entries, Playwright-console entries, and page errors. A no-AOT failure is a required-target failure rather than an automatic unsupported downgrade. Set `DATALINQ_BROWSER_PATH` when Edge, Chrome, or Chromium is not discoverable from the standard install paths or `PATH`.

Roslyn payload rules apply to every graph. Memory targets additionally scan both relative paths and binary/text content for `DataLinq.SQLite`, `DataLinq.MySql`, `Microsoft.Data.Sqlite`, `MySqlConnector`, `SQLitePCLRaw`, and `e_sqlite3`; the same provider tokens are legitimate in the SQLite graph and are not globally banned.

Useful options:

- `--targets`
  Limits the chosen set by exact target id or the `aot`, `trim`, `wasm`, `wasm-aot`, `sqlite`, `memory`, or `all` aliases. Comma-separated selectors may be combined.
- `--runtime`
  Runtime identifier for native publish targets. Defaults to the current OS and architecture.
- `--package-dir`, `--version`
  Enables package-backed evidence for `--target v0.9`. Supply both: the directory must contain exactly the six public runtime `.nupkg` files, and every nuspec must carry the requested exact version.
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
  Reuses restore assets already present in the selected targets' isolated scratch roots. Package mode reuses only the scratch and cache for the same candidate-byte identity. Run those targets once without this option first. It cannot be combined with `--clean-output`.
- `--clean-output`
  Deletes each selected source target's isolated scratch root before publishing, then restores and rebuilds its complete transitive graph. Package mode first empties the entire candidate-identity context, including its extracted package and HTTP caches, before recreating it and publishing the selected targets. Cleanup refuses a target or package context containing or reached through a symlink, junction, or other reparse point below the artifact root. Source-project `bin` and `obj` directories are not the release-evidence boundary.
- `--release-thresholds`
  Applies the shared, version-neutral compatibility guardrails by publish mode: Native AOT executable, Native AOT symbol-excluded folder, trimmed symbol-excluded folder, no-AOT Brotli assets, and WASM AOT Brotli assets.
- `--format summary|markdown|json`
  Controls console output. The JSON and Markdown artifacts are always written.

Reports are written under `artifacts/dev/compat-size-report/<timestamp>-<guid>/` as `report.json` and `report.md`. Raw publish logs are written under `artifacts/dev/`; target-specific browser smoke logs are written under the target folder inside the report directory. Catalog registration and focused tooling tests do not by themselves prove that all eight `v0.9` targets publish or execute. Source-project and package-backed runs are different evidence, and only a recorded fresh full report against the intended dependency source can make the corresponding release claim.

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

### `package-smoke`

Restores, builds, and executes the tracked external consumer using only an exact local package candidate:

```bash
dotnet run --project DataLinq.Dev.CLI -- package-smoke --package-dir artifacts/nuget-release/0.9.0-preview.N --version 0.9.0-preview.N
dotnet run --project DataLinq.Dev.CLI -- package-smoke --package-dir artifacts/nuget-release/0.9.0-preview.N --version 0.9.0-preview.N --output artifacts/release/v0.9/0.9.0-preview.N/packages/consumer-smoke --format markdown
```

`--package-dir` and `--version` are required. `--output` must name a missing or empty directory; when omitted, the command creates a unique directory under `artifacts/dev/package-smoke/`. Candidate, fixture, and output paths must not traverse reparse points, and output cannot equal or sit below either source directory. Once those path preconditions pass, the command retains `report.json` and `report.md` using schema `v0.9.package-consumer-smoke-report.v1`; a hard finding returns exit code `1`.

The tracked fixture lives under `test-infra/package-consumer`, outside `src`, and has no project references. It directly references exact bracketed versions of `DataLinq`, `DataLinq.Memory`, `DataLinq.SQLite`, and `DataLinq.MySql`. The direct core reference is deliberate: the provider packages exclude transitive build/analyzer assets, while the core package owns `DataLinq.Generators` and its analyzer dependencies.

The smoke fails closed unless all of these hold:

- the selected directory contains exactly one package for each consumed id at the requested version
- only the fixed four-file fixture manifest is copied, and the project must match the approved SDK/property/package/version-guard shape exactly; imports, direct references, analyzers, linked compile items, extra targets, extra packages, and extra source/build files are rejected before restore
- inherited MSBuild/NuGet redirect and import-hook variables are removed, automatic response and directory build/package imports are disabled, and restore/build explicitly pin the assets, project-extensions, package-cache, configuration, output, HTTP-cache, scratch, and temporary roots
- NuGet source mapping restricts `DataLinq*` to the selected candidate directory while external dependencies use NuGet.org
- the one pinned `project.assets.json` records only the generated configuration and isolated package cache, has no fallback folder or project library, covers `net8.0`, `net9.0`, and `net10.0`, and resolves all four DataLinq packages as packages at the exact version
- each restored DataLinq package records the selected local source and its cached `.nupkg` SHA-256 matches the selected candidate
- all three supported target frameworks build and emitted compiler-generated source contains the expected generated database and mutable row
- the net10 executable passes generated-model Memory seed/find/query, real shared-cache in-memory SQLite create/insert/query, and the MySQL public-surface compilation probe; the summary reports success only after the runner validates the exit code, schema, framework, and exact payload rather than trusting the fixture's aggregate bit

This is package-consumer evidence, not packaged Native AOT, trimming, or browser evidence. Run `package-report` against the same fresh candidate first, then run package-backed `size-report --target v0.9 --package-dir ... --version ...` for the separate constrained-runtime gate.

### `api-report`

Compares an exact freshly packed candidate with the locked published `0.8.0` package baseline by using the repo-local `Microsoft.DotNet.ApiCompat.Tool` manifest:

```bash
dotnet tool restore --tool-manifest ../.config/dotnet-tools.json
dotnet run --project DataLinq.Dev.CLI -- api-report --baseline-dir artifacts/api-baseline/nuget-org-0.8.0 --candidate-dir artifacts/nuget-release/0.9.0-preview.N --candidate-version 0.9.0-preview.N
dotnet run --project DataLinq.Dev.CLI -- api-report --baseline-dir artifacts/api-baseline/nuget-org-0.8.0 --candidate-dir artifacts/nuget-release/0.9.0-preview.N --candidate-version 0.9.0-preview.N --output artifacts/release/v0.9/0.9.0-preview.N/api --format markdown
```

`--baseline-dir`, `--candidate-dir`, and `--candidate-version` are required. `--baseline-version` defaults to `0.8.0`, and `--baseline-lock` defaults to `test-infra/api-compatibility/v0.8.0-packages.json`. That lock binds the baseline to the exact NuGet.org package-byte SHA-256 values, package repository URL and commit, and the local Git tag/commit identity. The baseline directory is explicit: the command never discovers a convenient copy in a global NuGet cache or silently downloads a replacement. `--output` must name a missing, non-overlapping path; omitting it creates a collision-resistant directory under `artifacts/dev/api-report/`.

The comparison set is `DataLinq`, `DataLinq.SQLite`, `DataLinq.MySql`, `DataLinq.Tools`, and the exact `tools/<tfm>/any/DataLinq.CLI.dll` assets for `net8.0`, `net9.0`, and `net10.0`. CLI assets are compared baseline-to-candidate per TFM and candidate net8 is compared bidirectionally with net9 and net10 so a framework-only addition is not mislabeled as a harmless baseline addition. `DataLinq.Memory` is new in 0.9, so the command validates its current package consistency and records its first three public surfaces instead of inventing a 0.8 baseline.

After source inspection, the command copies every exact nupkg into its fresh evidence root, verifies that the aggregate identities did not change during copying, holds the copied inputs against concurrent writes, and re-inspects them after all comparisons. Snapshots and ApiCompat consume those evidence-owned bytes rather than reopening mutable ignored source directories throughout the run.

Each run retains schema `v0.9.api-compatibility-report.v1` as `report.json` and `report.md`, raw standard output/error for every pinned ApiCompat invocation, generated suppression XML when ApiCompat emits it, and a human-readable metadata snapshot for every selected compile asset. A successful zero-diagnostic invocation is represented by its exit code and logs with a null suppression path because ApiCompat 10.0.302 intentionally creates no empty XML file. ApiCompat is authoritative for compatibility classification. The snapshots are supplemental review/provenance evidence: their semantic API hash excludes MVID and whole-file hash, and they are not presented as a home-grown replacement for ApiCompat.

Findings are deliberately separated:

- baseline diagnostics from the normal comparison are binary/API breaks; `CP0017` parameter-name changes are called out as source-sensitive breaks
- non-baseline diagnostics are inconsistent API across the candidate's current target frameworks and are hard failures
- strict-baseline-only diagnostics are compatible or additive changes that remain visible for release review without automatically failing the command
- each first `DataLinq.Memory` surface is a review item, while an inconsistent Memory package remains a hard failure

The report also binds evidence to the start/end Git state, the Dev CLI and DevTools embedded commits and clean-build attestations, the candidate nuspec commit, and the locked baseline tag. A dirty or drifting checkout, stale runner binary, dirty-built runner, candidate/checkout mismatch, baseline/tag mismatch, package-set fault, snapshot fault, tool-version mismatch, or ApiCompat execution fault is a hard failure and returns exit code `1`. This command does not prove generated-source, behavioral, wire-format, exception-behavior, or consumer-execution compatibility; those remain separate release gates.

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
