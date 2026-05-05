> [!WARNING]
> This document is roadmap and engineering planning material. It is not normative product documentation and should not be treated as a support claim.
# Practical AOT and Size Plan

**Status:** Draft follow-up plan after Phase 8.

**Created:** 2026-05-05.

## Purpose

Phase 8 proved something real: generated SQLite models can run under Native AOT, trimmed publish, and Blazor WebAssembly AOT. That is a serious milestone.

It did not prove that DataLinq is ready to advertise practical AOT support. The current implementation still has three ugly facts:

1. The runtime package pulls Roslyn into constrained publishes.
2. The query pipeline still depends on `Remotion.Linq`, which emits Native AOT and trim warnings.
3. The browser SQLite path works under WASM AOT, but the published payload is large and SQLitePCLRaw emits WebAssembly native varargs warnings.

This plan describes what must happen before DataLinq can have a practical AOT story: one that works, publishes cleanly, and has file sizes a normal user would not immediately hate.

## Current Evidence

The source of truth for the Phase 8 proof is [Compatibility Results](../roadmap-implementation/phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md).

Current measured output:

| Target | Result | Current size | Main problem |
| --- | --- | ---: | --- |
| Native AOT SQLite smoke | passes | 18.61 MiB executable, 76.76 MiB folder including PDBs | symbols dominate folder size; `Remotion.Linq` warnings remain |
| Trimmed SQLite smoke | passes | 32.10 MiB folder | Roslyn and `Remotion.Linq` remain in runtime graph |
| Blazor WASM no-AOT publish | publishes, runtime unsupported | 7.91 MiB Brotli assets | Mono interpreter fails on the SQLite/DataLinq path |
| Blazor WASM AOT | publishes and browser smoke passes | 18.80 MiB Brotli assets | AOT runtime size plus Roslyn payload plus SQLitePCLRaw warnings |

The good news: the generated SQLite path is viable.

The bad news: the package graph is still amateur-hour for constrained deployment. Shipping compiler assemblies in a browser database sample is the kind of thing users will notice, and not in a fond way.

## Practical Support Definition

DataLinq should not claim practical AOT support until all of this is true:

- generated model paths publish and run under Native AOT with no DataLinq-owned AOT warnings
- generated model paths publish and run under trimming with no DataLinq-owned trim warnings
- `DataLinq.dll` has no runtime dependency on `Microsoft.CodeAnalysis.*`
- browser publish output does not contain Roslyn assemblies or Roslyn `.wasm` assets
- `Remotion.Linq` is removed, isolated from the AOT support boundary, or warning-clean under the SDK analyzers
- SQLitePCLRaw WASM warnings are either removed or documented with a verified "not called by this path" proof
- size reports are generated automatically for Native AOT, trimmed, and WASM publishes
- a CI or local release gate rejects accidental payload regressions

The first public support statement should be narrow:

> DataLinq supports generated SQLite models under Native AOT and Blazor WebAssembly AOT for the documented query subset.

Do not broaden that to reflection-discovered models, arbitrary client projection expressions, MySQL/MariaDB browser support, or no-AOT browser WebAssembly until those paths are separately proven.

## Size Targets

These are engineering targets, not promises. They exist to stop us from declaring victory while the payload is still silly.

| Target | Current | Practical target | Notes |
| --- | ---: | ---: | --- |
| Native AOT executable, SQLite smoke | 18.61 MiB | <= 20 MiB | Already acceptable for a self-contained SQLite CLI. Publish symbols separately. |
| Native AOT publish folder without PDBs | about 20 MiB inferred | <= 25 MiB | The current 76.76 MiB folder is mostly PDBs. That is packaging discipline, not runtime bloat. |
| Trimmed self-contained publish folder | 32.10 MiB | <= 20 MiB | Removing Roslyn should be the first big win. |
| WASM no-AOT Brotli assets | 7.91 MiB | <= 6 MiB if it ever runs | Not worth prioritizing until the interpreter failures are gone. |
| WASM AOT Brotli assets | 18.80 MiB | 12-16 MiB | Sub-10 MiB with SQLite plus AOT is probably unrealistic on current .NET without more radical choices. |

The WASM AOT target is deliberately not fantasy. `dotnet.native.wasm` is already a large fixed cost. The practical goal is "respectable for an offline-capable browser database app", not "tiny marketing demo".

## Workstream 1: Split Runtime From Roslyn

This is the highest-return work. Do it first.

Current problem:

- `src/DataLinq/DataLinq.csproj` references `Microsoft.CodeAnalysis.CSharp`.
- `src/DataLinq/DataLinq.csproj` compiles all of `src/DataLinq.SharedCore/**/*.cs`.
- `src/DataLinq.SharedCore` mixes runtime-safe metadata types with generator/tooling code that directly uses Roslyn syntax types.
- `src/DataLinq.SharedCore/Metadata/CsTypeDeclaration.cs` has constructors and helpers that depend on `Microsoft.CodeAnalysis`.
- `src/DataLinq.SharedCore/Factories/SyntaxParser.cs`, `Factories/Generator/MetadataFromModelsFactory.cs`, and `Factories/Models/ModelFileFactory.cs` are generator/tooling concerns, not runtime concerns.

Required direction:

1. Create a runtime-safe shared surface.
   - Keep attributes, metadata DTOs, generated declarations, source-span structs, provider-neutral enums, and runtime type conversion.
   - Keep it free of `Microsoft.CodeAnalysis`.
2. Move Roslyn parsing and source-model factory code into generator/tooling-owned projects.
   - `SyntaxParser`
   - `MetadataFromModelsFactory`
   - `ModelFileFactory`
   - Roslyn-specific `CsTypeDeclaration` construction
3. Split `CsTypeDeclaration` into runtime and Roslyn adapters.
   - Runtime constructor from `System.Type` stays in the runtime surface.
   - Syntax constructors move to a Roslyn helper, extension, or generator-only partial type.
4. Remove `Microsoft.CodeAnalysis.CSharp` from `src/DataLinq/DataLinq.csproj`.
5. Keep the generator in the NuGet analyzer path without making Roslyn a runtime dependency.
   - `lib/net*/DataLinq.dll` must not reference Roslyn.
   - `analyzers/dotnet/cs/DataLinq.Generators.dll` can reference Roslyn.
6. Add a package/publish assertion that fails if constrained output contains:
   - `Microsoft.CodeAnalysis.dll`
   - `Microsoft.CodeAnalysis.CSharp.dll`
   - Roslyn satellite resource folders
   - `Microsoft.CodeAnalysis*.wasm`

Exit criteria:

- `dotnet publish` for `DataLinq.TrimSmoke` has no Roslyn files in the publish folder.
- `dotnet publish` for `DataLinq.BlazorWasm` has no Roslyn `.wasm` assets.
- `dotnet list src\DataLinq\DataLinq.csproj package --include-transitive` no longer shows `Microsoft.CodeAnalysis.*` as runtime dependencies.

Expected impact:

- Trimmed publish should drop meaningfully.
- WASM payload should drop meaningfully.
- WASM AOT compile time should drop, because the current publish AOTs compiler assemblies. That is absurd work.

## Workstream 2: Replace or Isolate Remotion.Linq

This is the hardest work, but it is probably necessary.

Current problem:

- `src/DataLinq/DataLinq.csproj` references `Remotion.Linq`.
- Query code imports Remotion clauses and result operators across:
  - `src/DataLinq/Linq/Queryable.cs`
  - `src/DataLinq/Linq/QueryExecutor.cs`
  - `src/DataLinq/Linq/QueryBuilder.cs`
  - `src/DataLinq/Linq/LocalSequenceExtractor.cs`
  - `src/DataLinq/Linq/Visitors/*`
  - `src/DataLinq/Query/SqlQuery.cs`
- Native AOT and trim publishes pass but emit warnings from `Remotion.Linq`.

Do not start by suppressing this. Suppression can be a temporary measuring tool, but it is not a product solution unless the dependency is audited and the exact warning paths are proven unreachable.

Practical options:

### Option A: Internal Supported-Subset Parser

Build a small query-shape parser directly over `System.Linq.Expressions`.

It should produce a DataLinq-owned intermediate model, for example:

- source table
- `Where` predicates
- `OrderBy` clauses
- `Select` projection
- scalar result operator
- join shape
- local sequence expansion data

This parser should cover the query subset DataLinq already claims after Phases 6 and 7. It should not attempt to be a general LINQ provider.

Pros:

- best AOT story
- smaller dependency graph
- clearer diagnostics
- no third-party query model leaking into runtime contracts

Cons:

- more implementation work
- easy to accidentally regress supported LINQ shapes without a strong support matrix

### Option B: Transitional Adapter

Introduce a DataLinq-owned query plan first, then keep Remotion only as one parser feeding that plan.

Steps:

1. Define `DataLinqQueryPlan`.
2. Convert existing Remotion output into `DataLinqQueryPlan`.
3. Move SQL generation and projection execution to consume `DataLinqQueryPlan`.
4. Add a new expression parser that also produces `DataLinqQueryPlan`.
5. Switch generated/AOT mode to the new parser.
6. Remove Remotion from the default runtime package when parity is good enough.

This is slower but safer. It lets tests compare Remotion-parsed plans to new-parser plans before flipping defaults.

### Option C: Split Legacy Query Provider Package

Move Remotion-backed query support behind a separate package or compatibility mode.

This only makes sense if the internal parser can cover the generated/AOT support subset. Otherwise it just relocates the warning.

Recommended path:

Use Option B, then finish at Option A for the supported subset. A direct rewrite is tempting, but the test surface is large enough that a plan adapter will save pain.

Exit criteria:

- generated SQLite AOT smoke publishes without `Remotion.Linq` warnings
- trim smoke publishes without `Remotion.Linq` warnings
- supported LINQ matrix remains green
- unsupported query diagnostics remain specific

## Workstream 3: Make the Generated Contract Explicit

Phase 8 already made generated hooks mandatory for the generic database/provider path. That was the correct call.

The focused execution plan is [Generated Metadata Contract and Runtime Fallback Removal](../metadata-and-generation/Generated%20Metadata%20Contract%20and%20Runtime%20Fallback%20Removal.md).

The next step is to turn generated models into a clean product contract:

- document generated models as the only AOT-supported model path
- mark reflection-discovered metadata APIs as compatibility APIs, not AOT APIs
- remove stale generated-hook compatibility shims instead of accepting old generated output
- validate generated table declarations during provider initialization
- move ordinary generated-model metadata startup away from attribute/property reflection
- add analyzer diagnostics when a database model is used without generated hooks in an AOT-intended project
- expose narrowly named APIs where useful, such as `OpenGeneratedDatabase<T>()` or equivalent factory helpers, if that improves discoverability
- make runtime exception messages point to source generation and the missing hook contract

The policy should be:

> If a hook should have been generated, failing fast is better than falling back.

Fallbacks are fine for desktop compatibility. They are poison for AOT confidence.

Exit criteria:

- missing generated hook diagnostics are covered by unit or generator tests
- malformed generated table declarations fail before materialization
- generated model startup does not rediscover ordinary metadata through runtime reflection
- docs clearly separate AOT-supported and compatibility-only paths
- no AOT smoke test depends on reflection fallback

## Workstream 4: SQLite WebAssembly Warning Hygiene

Current problem:

- WASM AOT publish passes.
- Browser smoke passes.
- Publish emits `WASM0001` warnings for SQLitePCLRaw native varargs exports:
  - `sqlite3_config`
  - `sqlite3_db_config`

The warning says calls to those functions would fail at runtime. Our smoke path does not appear to call them, but "appears" is not good enough for a clean support claim.

Required work:

1. Identify which managed SQLitePCLRaw methods import those symbols.
2. Determine whether `Microsoft.Data.Sqlite` or the selected SQLitePCLRaw provider can call them during:
   - provider initialization
   - connection open
   - foreign key configuration
   - connection string options
   - future OPFS/file-backed configuration
3. Add a WASM smoke assertion that covers provider registration, connection open, schema creation, foreign keys, insert, query, projection, and relation loading.
4. If the symbols are unreachable for our path, document the proof and consider a narrowly justified warning suppression at the smoke project level.
5. If they are reachable for realistic configuration, find a provider or initialization path that avoids them.

Do not suppress `WASM0001` in the library package until the call graph is understood. Suppressing unknown native warnings because the happy-path smoke passed is lazy.

Exit criteria:

- warning disposition is documented with exact methods and call paths
- WASM AOT smoke still passes in browser
- OPFS/file-backed experiment has a separate warning and behavior note before it is claimed

## Workstream 5: Browser Storage and no-AOT Reality

The no-AOT browser story is currently not supportable.

Observed failures:

- `SqliteConnectionStringBuilder` failed under the Mono interpreter.
- after bypassing that, `SQLiteProvider.RegisterProvider` hit a Mono interpreter `NIY` failure.

Practical stance:

- keep WASM AOT as the supported browser path
- do not spend serious time on no-AOT until runtime/package size work is done
- treat no-AOT as an opportunistic future win, not a blocker for a practical AOT story

Storage work should be staged:

1. Keep in-memory SQLite as the proof boundary.
2. Add file-backed or OPFS-oriented storage as a separate experiment.
3. Measure payload and startup impact separately.
4. Do not mix OPFS work with query parser or package graph work.

Exit criteria:

- in-memory browser smoke remains stable
- OPFS/file-backed behavior is documented separately
- no-AOT remains explicitly unsupported until it runs

## Workstream 6: Size Reporting and Gates

Manual size notes are useful once. After that they rot.

Add a repeatable size report command that publishes the constrained targets and emits:

- total publish folder size
- total size excluding symbols
- compressed size for WASM `.br` and `.gz`
- top 20 largest files
- banned dependency presence
- warning summary grouped by package

Candidate command shape:

```powershell
dotnet run --project src\DataLinq.Dev.CLI -- size-report --target phase8
```

or a Testing CLI command if it fits better:

```powershell
dotnet run --project src\DataLinq.Testing.CLI -- compatibility size-report --targets aot,trim,wasm-aot
```

The exact host does not matter. The important part is that the report is deterministic and easy to run before release.

Minimum gates:

- fail if Roslyn appears in trimmed or WASM output
- fail if Native AOT publish has new DataLinq-owned AOT warnings
- fail if trim publish has new DataLinq-owned trim warnings
- fail if WASM AOT browser smoke does not reach `passed`
- warn if WASM AOT Brotli size increases by more than a configured threshold

Exit criteria:

- size report is checked into artifacts or summarized in test output
- compatibility results can be refreshed without hand-counting files
- future payload regressions are visible immediately

## Workstream 7: Public Packaging Discipline

Native AOT folder size is currently inflated by symbols. That is normal during development and bad as a shipping default.

Required packaging rules:

- publish PDBs separately for Native AOT release artifacts
- document the difference between executable size and symbol-inclusive folder size
- avoid putting smoke/sample projects into shipped packages
- ensure analyzers are packaged as analyzers, not runtime assets
- verify package dependency metadata after `pack`, not only project references before `pack`

Exit criteria:

- package inspection confirms no analyzer dependencies leak into runtime dependency groups
- release notes can state realistic binary sizes without caveats hidden in footnotes

## Recommended Order

1. Add automated size reports and banned-file checks.
2. Split runtime-safe metadata from Roslyn/generator code.
3. Remove Roslyn from the runtime package and verify publish-size improvement.
4. Introduce a DataLinq-owned query plan behind the current Remotion parser.
5. Build a supported-subset expression parser that emits the same query plan.
6. Move generated/AOT mode to the new parser.
7. Remove or isolate Remotion from the practical AOT support boundary.
8. Investigate SQLitePCLRaw WASM warnings and document or eliminate them.
9. Add OPFS/file-backed browser storage as a separate proof.
10. Promote the narrow support claim into product docs only after the gates are boring.

This order matters. Replacing the query parser before removing Roslyn is backwards: Roslyn is the obvious payload problem, and it is easier to verify.

## Definition of Done

Practical AOT support is ready to document publicly when:

- `DataLinq.TrimSmoke` publishes and runs without Roslyn output
- `DataLinq.AotSmoke` publishes and runs without DataLinq-owned AOT warnings
- `DataLinq.BlazorWasm` publishes and browser-runs under AOT with no Roslyn output
- `Remotion.Linq` no longer produces AOT/trim warnings in the supported path
- WASM warning disposition is documented and tested
- size reports are generated by tooling
- the support boundary says exactly what works and exactly what does not

Until then, the correct wording is:

> DataLinq has a proven generated SQLite AOT/WASM AOT smoke path.

Not:

> DataLinq is AOT-compatible.

The second sentence is the one we earn after the package graph and query parser stop embarrassing us.
