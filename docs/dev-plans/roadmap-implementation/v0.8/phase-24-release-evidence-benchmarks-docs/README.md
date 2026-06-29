> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 24: Release Evidence, Benchmarks, and Docs

**Status:** Planned.

Execution plan: [Implementation Plan](Implementation%20Plan.md).

## Purpose

Phase 24 is the final 0.8 release-readiness pass.

By this point the parser cleanup should be complete and the browser AOT question has current Phase 23 evidence: the generated SQLite browser AOT runtime blocker is fixed, no-AOT passes the same narrow smoke boundary, clean-output WebAssembly publish still fails before browser execution with the SDK `ResolveWasmOutputs` issue, and `WASM0001` warnings remain visible. Phase 24 does not add product behavior. It collects final evidence, refreshes benchmark baselines, verifies package hygiene, and makes the public docs match what the artifacts prove.

## Scope

In scope:

- final compatibility size report
- final package report against freshly packed artifacts
- final focused AOT smoke
- final focused query/runtime benchmark history refresh
- final public docs pass for LINQ support, platform compatibility, query internals, and release wording
- final dev-plan closeout updates for Phases 22 through 24
- changelog or release-note draft updates where the repo keeps release material
- docs build verification when feasible

Out of scope:

- publishing NuGet packages
- adding new query features
- changing browser support based on hope instead of Phase 23 evidence
- adding broad benchmark scenarios that have not earned stable promotion
- large documentation rewrites unrelated to 0.8 behavior
- migration execution, mutable lifecycle, transaction isolation, scalar converters, or result caching

## Release Evidence Required

Compatibility evidence:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets phase8c --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
```

Package evidence after packing:

```powershell
.\publish-nuget.ps1 -PackOnly -PackageOutputPath artifacts\nuget-release\v0.8-final
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- package-report --package-dir artifacts\nuget-release\v0.8-final --format markdown
```

Smoke evidence:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.AotSmoke
```

Benchmark evidence:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile heavy --history-json artifacts\benchmarks\history\v0.8-final-query-hotpath.json
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile heavy --history-json artifacts\benchmarks\history\v0.8-final-phase2-watch.json
```

## Documentation Targets

At minimum, review and update:

- `docs/Platform Compatibility.md`
- `docs/Supported LINQ Queries.md`
- `docs/support-matrices/LINQ Translation Support Matrix.md`
- `docs/internals/LINQ Parser Architecture.md`
- `docs/internals/Query Translator.md`
- `docs/Benchmark Results.md` if benchmark evidence paths or interpretation changed
- 0.8 dev-plan READMEs and implementation plans for closeout evidence
- changelog/release notes if present in the repo

The docs should be boringly precise:

- generated SQLite Native AOT and trimmed support only if the final reports pass
- browser AOT support only inside the generated SQLite smoke boundary unless Phase 24 produces broader passing browser evidence
- no-AOT browser support only inside the generated SQLite smoke boundary unless Phase 24 produces broader passing browser evidence
- clean-output WebAssembly publish either fixed or explicitly classified as SDK/toolchain evidence
- documented LINQ subset only
- no arbitrary LINQ, no materialized `IGrouping`, no left joins, no non-SQL backends

## Exit Criteria

Phase 24 is done when:

- final compatibility and package reports are recorded
- final benchmark artifacts are recorded or the inability to run them is documented
- public docs match the final evidence
- dev-plan closeout records link to the evidence
- docs build passes where feasible
- worktree is clean except for intentional release artifacts ignored by git
- NuGet publishing remains a manual user action
