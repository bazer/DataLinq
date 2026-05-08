> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 8C Implementation Plan: Practical AOT Package Graph and Generated Runtime Hardening

**Status:** Planned follow-up after Phase 8B.

## Purpose

Phase 8 proved a generated SQLite Native AOT, trimmed, and Blazor WebAssembly AOT smoke boundary. Phase 8B then tightened the generated contract and built the immutable metadata/factory foundation needed for serious generated metadata startup.

Phase 8C is the remaining package/runtime part of that practical AOT cleanup:

- make constrained-platform evidence repeatable
- remove Roslyn/compiler dependencies from runtime outputs
- make generated startup consume complete generated metadata instead of rediscovering ordinary metadata through reflection
- use generated indexed access and metadata handles where they remove avoidable runtime lookup
- keep package and public compatibility claims honest

The Remotion replacement, DataLinq query-plan boundary, expression parser, and SQLitePCLRaw warning disposition are deliberately not in this slice. They now live in the later query-boundary phase.

## Phase-Start Baseline

The Phase 8B foundation changed the metadata side of the problem:

- generated hooks are strict enough to fail stale output early
- malformed generated declarations fail during provider initialization
- metadata producers feed typed drafts into `MetadataDefinitionFactory`
- factory-built runtime metadata is frozen against ordinary mutation and the main structural collection mutation paths
- mutable metadata construction APIs are now compatibility surfaces rather than the normal product path

The remaining package/runtime debt is still concrete:

- `src/DataLinq/DataLinq.csproj` still carries Roslyn/compiler dependencies through the runtime package graph
- constrained publish sizes and banned payloads need repeatable tooling instead of manual folder inspection
- generated startup still has too much ordinary metadata rediscovery work
- generated instance access can use indexed handles instead of repeated name/global metadata lookups
- package assets and public wording need to match the actual evidence

## Goals

- add repeatable compatibility size, warning, and banned-payload reports
- split runtime-safe metadata from Roslyn/generator code
- remove `Microsoft.CodeAnalysis.*` from `DataLinq.dll` runtime dependency groups and constrained publish outputs
- switch generated-model startup to complete generated metadata through the Phase 8B factory path
- generate indexed value access, relation handles, and mutable metadata handles
- verify packed NuGet assets, not only project references
- keep compatibility wording narrow until the later query-boundary phase removes or isolates Remotion and resolves SQLitePCLRaw warning disposition

## Non-Goals

- replacing or isolating `Remotion.Linq`
- introducing a DataLinq-owned query plan
- building the supported-subset expression parser
- claiming warning-clean generated/AOT query support
- resolving SQLitePCLRaw WebAssembly native varargs warnings
- claiming no-AOT browser WebAssembly support
- MySQL/MariaDB browser support
- OPFS/file-backed browser storage
- cache, memory, and invalidation redesign

## Workstream A: Compatibility Size Reports And Banned-Payload Gates

Goals:

- make constrained-platform evidence repeatable
- expose payload and warning regressions before release
- stop hand-counted folder sizes from becoming stale documentation

Tasks:

1. Add a repeatable local command for constrained compatibility reports.
2. Publish Native AOT, trimmed, no-AOT WASM, and WASM AOT targets from one command when workloads are available.
3. Report total publish size, symbol-excluded size, compressed WASM `.br` and `.gz` assets, top largest files, file count, warning summary, and smoke result.
4. Add banned-payload checks for:
   - `Microsoft.CodeAnalysis.dll`
   - `Microsoft.CodeAnalysis.CSharp.dll`
   - Roslyn satellite resource folders
   - `Microsoft.CodeAnalysis*.wasm`
5. Add warning classification for DataLinq-owned warnings, third-party dependency warnings, SDK/WebAssembly warnings, and intentionally unsupported no-AOT failures.
6. Store report output under `artifacts/` or print stable machine-readable output that can be attached to PRs.
7. Keep size thresholds configurable. Start with warning thresholds before hard-failing on size growth.

Candidate command shapes:

```powershell
dotnet run --project src\DataLinq.Dev.CLI -- size-report --target phase8c
```

or:

```powershell
dotnet run --project src\DataLinq.Testing.CLI -- compatibility size-report --targets aot,trim,wasm-aot
```

The exact host is less important than repeatability. The Testing CLI is attractive if the command naturally grows into smoke execution. The Dev CLI is attractive if this is primarily a build/report workflow.

Exit criteria:

- compatibility results can be refreshed without manual folder inspection
- Roslyn payload presence is reported and can fail the report once the runtime split lands
- AOT, trim, and WASM warnings are grouped by owner and warning code
- the report can reproduce the Phase 8 measurements with comparable numbers

## Workstream B: Split Runtime-Safe Metadata From Roslyn And Generator Code

Goals:

- remove compiler APIs from the runtime package graph
- keep generator/tooling functionality intact
- make runtime-safe metadata usable by constrained publishes

Tasks:

1. Create a runtime-safe shared surface that contains attributes, metadata DTOs, generated declarations, source-span structs that do not require Roslyn, provider-neutral enums, schema comparison types, and runtime type conversion.
2. Keep that runtime-safe surface free of `Microsoft.CodeAnalysis.*`.
3. Move Roslyn parsing and source-model factory code into generator/tooling-owned projects:
   - `SyntaxParser`
   - `MetadataFromModelsFactory`
   - `ModelFileFactory`
   - Roslyn-specific `CsTypeDeclaration` construction
   - Roslyn-specific source-location adapters
4. Split `CsTypeDeclaration` and any source-location types into runtime-safe representations plus Roslyn adapters.
5. Remove `Microsoft.CodeAnalysis.CSharp` from `src/DataLinq/DataLinq.csproj`.
6. Keep `DataLinq.Generators` packaged under `analyzers/dotnet/cs` without leaking analyzer dependencies into runtime dependency groups.
7. Inspect packed NuGet assets, not only project references.
8. Run the constrained size report and verify Roslyn files disappear from trimmed and WebAssembly publish outputs.

Exit criteria:

- `DataLinq.dll` has no runtime reference to `Microsoft.CodeAnalysis.*`
- trimmed and WASM publish outputs do not contain Roslyn assemblies or Roslyn `.wasm` assets
- package inspection confirms analyzer payload is under analyzer assets, not runtime dependencies
- generator, tooling, and source-model tests still pass
- measured trim and WASM sizes improve or have a documented reason if they do not

## Workstream C: Complete Generated Metadata Startup

Goals:

- stop generated-model startup from rediscovering metadata the generator already knew
- use the immutable metadata builder/factory path from Phase 8B
- preserve compatibility reflection only behind explicit compatibility surfaces

Tasks:

1. Add a generated hook or generated declaration that provides complete runtime metadata inputs.
2. Emit generated metadata in builder/declaration form instead of one giant unreadable object graph unless benchmarks prove direct construction is worth it.
3. Feed generated metadata declarations into the runtime-safe metadata factory.
4. Add metadata equivalence tests comparing generated complete metadata against current reflected metadata for representative models.
5. Switch generic generated-provider startup to prefer generated complete metadata.
6. Keep reflection parsing for explicit compatibility/tooling paths, not as the ordinary generated-model path.
7. Verify generated metadata startup does not call `Type.GetCustomAttributes(...)`, `Type.GetProperties(...)`, or `Type.GetInterfaces()` for ordinary metadata loading.
8. Re-run Native AOT and trimmed smoke publishes after the startup switch.

Exit criteria:

- generated model startup uses generated complete metadata for ordinary metadata loading
- generated metadata and reflected metadata are equivalent for active test models
- reflection-discovered metadata APIs are clearly compatibility APIs
- Phase 8 smoke projects still publish and run under Native AOT, trimming, and WASM AOT
- Roslyn types are not required by the runtime-safe generated metadata builder surface

## Workstream D: Generated Indexed Access And Metadata Handles

Goals:

- take advantage of dense indexed `RowData`
- remove name-based lookup from generated value and relation hot paths
- avoid global metadata discovery for generated mutable instances

Tasks:

1. Add or confirm runtime APIs for direct indexed row access, such as `IRowData.GetValue(int columnIndex)`.
2. Generate stable column-index constants per model.
3. Generate immutable getters that use the stable column index directly instead of `GetValue(nameof(Property))`.
4. Generate mutable getters/setters that use column-index APIs or generated static column handles instead of name lookup.
5. Generate relation identifiers or relation handles for generated relation properties.
6. Generate relation getters that use those handles instead of `GetImmutableRelation(nameof(...))`.
7. Generate mutable constructors that use generated metadata directly instead of `ModelDefinition.Find<T>()`.
8. Add tests for subset-column queries so indexed access does not assume every query selected every column.
9. Add benchmark or microbenchmark coverage for generated property getter access.

Exit criteria:

- generated immutable value getters do not use `GetValue(nameof(...))`
- generated mutable value access does not use name-based `GetValue`/`SetValue` on generated paths
- generated relation getters do not perform relation-name dictionary lookup
- `new MutableFoo()` does not search `DatabaseDefinition.LoadedDatabases`
- indexed access is correct for projected/subset-loaded rows
- property access benchmarks are neutral or better

## Workstream E: Packaging And Public Compatibility Wording

Goals:

- keep package assets honest
- prevent roadmap claims from leaking into product docs early
- define the first support statement DataLinq can defend without hiding deferred query-boundary caveats

Tasks:

1. Inspect packed NuGet output and dependency groups after the Roslyn split.
2. Verify analyzers live under analyzer assets and do not pull runtime dependencies into `lib/net*`.
3. Publish PDB/symbol output separately for Native AOT release artifacts when documenting sizes.
4. Keep smoke/sample projects out of shipped packages.
5. Update platform compatibility docs only after the package and generated-runtime gates are clean.
6. Keep compatibility wording narrow and explicit that the Remotion/query-parser and SQLitePCLRaw warning work remains deferred.
7. Do not expand wording to reflection-discovered models, arbitrary client projections, MySQL/MariaDB browser support, OPFS storage, or no-AOT browser WebAssembly.

Exit criteria:

- package inspection confirms runtime dependency groups are clean
- release notes can state realistic constrained-platform sizes without hiding symbol or browser payload caveats
- public docs and support matrices match the implementation evidence
- roadmap documents remain separate from shipped behavior docs
- product docs do not imply the later query-boundary phase has already happened

## Recommended Order

1. Add compatibility size reports and banned-payload checks.
2. Split runtime-safe metadata from Roslyn/generator code.
3. Remove Roslyn from the runtime package graph and verify size/report improvement.
4. Generate complete metadata against the Phase 8B immutable metadata factory.
5. Switch generated-model startup to generated complete metadata.
6. Generate indexed value access, relation handles, and mutable metadata handles.
7. Promote only the compatibility wording supported by package, smoke, and report evidence.

This order keeps the mechanical package evidence in front of the larger startup/access changes. It also avoids spending time polishing size numbers while Roslyn is still visibly leaking into constrained outputs.

## Verification Plan

Routine verification after generated/metadata/package slices:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- build src\DataLinq.sln --profile ci --output errors
dotnet run --project src\DataLinq.Testing.CLI -- run --suite generators --alias quick --output failures
dotnet run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures
```

Constrained-platform verification:

```powershell
.\scripts\dotnet-sandbox.ps1 publish src\DataLinq.AotSmoke\DataLinq.AotSmoke.csproj -f net10.0 -r win-x64 -c Release -v:minimal --self-contained true -p:PublishAot=true
.\scripts\dotnet-sandbox.ps1 publish src\DataLinq.TrimSmoke\DataLinq.TrimSmoke.csproj -f net10.0 -r win-x64 -c Release -v:minimal --self-contained true -p:PublishTrimmed=true
.\scripts\dotnet-sandbox.ps1 publish src\DataLinq.BlazorWasm\DataLinq.BlazorWasm.csproj -f net10.0 -c Release -v:minimal -p:RunAOTCompilation=true
```

Final phase verification:

- generator quick suite
- unit quick suite
- Native AOT publish and executable run
- trimmed publish and executable run
- Blazor WebAssembly AOT publish and browser smoke when the environment supports it
- compatibility size report with banned-payload checks
- package inspection for runtime dependency groups
- docs build if public docs or navigation changed

Environment caveat:

Blazor WebAssembly builds are known to be unreliable inside the Codex sandbox on native Windows because the WebAssembly/MSBuild task host can fail there. Verify outside the sandbox before treating `DataLinq.BlazorWasm` build failures as product bugs.

## Risk Register

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Roslyn split breaks tooling/generator reuse | Medium | Move Roslyn code to generator/tooling adapters and keep runtime-safe DTOs small and explicit. |
| Size reporting becomes noisy or environment-specific | Medium | Separate hard banned-file checks from advisory size thresholds; include workload/environment metadata in reports. |
| Complete generated metadata drifts from reflected metadata | High | Keep representative equivalence tests and compare source/generated/provider metadata digests. |
| Generated indexed access assumes full-column rows | High | Add subset-column query coverage before switching access paths broadly. |
| Public docs overclaim | High | Promote only wording that package inspection, smokes, and report evidence already support. |

## Exit Criteria

Phase 8C is complete when:

- compatibility size reports can be refreshed by tooling
- trimmed and WebAssembly outputs contain no Roslyn runtime payload
- `DataLinq.dll` runtime dependency groups no longer include `Microsoft.CodeAnalysis.*`
- generated model startup no longer rediscovers ordinary metadata through runtime reflection
- generated value, relation, and mutable access paths avoid avoidable name/global metadata lookup
- generated SQLite Native AOT and trimmed publishes run without DataLinq-owned AOT/trim warnings
- package inspection confirms analyzer assets do not leak runtime dependencies
- public docs can state the narrow package/generated-runtime evidence without pretending Remotion/query-parser or SQLitePCLRaw warning work is complete

