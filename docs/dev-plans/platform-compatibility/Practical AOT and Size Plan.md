> [!WARNING]
> This document is roadmap and engineering planning material. It is not normative product documentation and should not be treated as a support claim.
# Practical AOT and Size Plan

**Status:** Active compatibility follow-up. Phase 8C completed the package graph, size reporting, generated startup, and packaging work. The 0.8 roadmap then completed the Remotion/query-parser work through Phase 7. Playwright-backed browser automation, broader constrained query coverage, clean-output publishing, and release-threshold warnings are implemented in the local tooling. Phase 23 fixed the WebAssembly AOT browser runtime blocker, and the Phase 24 final clean-output report passes generated SQLite Native AOT, trimmed publish, WebAssembly no-AOT, and WebAssembly AOT smokes. Provider breadth and SQLitePCLRaw warning disposition remain future compatibility work.

**Created:** 2026-05-05.

**Update 2026-05-11:** The package graph, size reporting, generated startup, and packaging work landed in archived [Phase 8C](../archive/roadmap-implementation/phase-8c-practical-aot-package-graph-and-generated-runtime-hardening/Implementation%20Plan.md). The Remotion/query-parser and SQLitePCLRaw warning-disposition work moved to [Phase 17](../roadmap-implementation/phase-17-query-plan-and-remotion-isolation/Implementation%20Plan.md).

**Update 2026-06-27:** The Remotion/query-parser work is now sequenced under the version-scoped [DataLinq 0.8 Roadmap](../roadmap-implementation/v0.8/README.md).

**Update 2026-06-28:** The 0.8 parser-removal track closed through [Phase 7](../roadmap-implementation/v0.8/phase-7-remotion-dependency-removal/README.md). Production queries use DataLinq's expression parser, `Remotion.Linq` is gone from the main runtime package graph, and trimmed compatibility reporting no longer classifies the publish as a Remotion dependency failure.

**Update 2026-06-28 refresh:** After installing the Visual Studio C++ toolchain and refreshing workloads, `DataLinq.Dev.CLI size-report --targets phase8c` publishes all four constrained targets successfully. Native AOT and trimmed executable smokes pass with zero warnings and zero banned payloads. WebAssembly publish targets complete with zero banned payloads; browser smoke automation was then added to `size-report` through Playwright.

**Update 2026-06-28 browser evidence:** A host-side `wasm-aot` report at `artifacts/dev/compat-size-report/20260628-163740998/` publishes successfully, serves the app, opens Edge through Playwright, and fails at `opening-generated-database` with `MONO_WASM: function signature mismatch`. A clean-output host report at `artifacts/dev/compat-size-report/20260628-164853329/` fails earlier in publish with `MSB4057` for missing `ResolveWasmOutputs`, which is classified as WebAssembly SDK/toolchain evidence rather than a DataLinq query regression.

**Update 2026-06-29 Phase 23 browser evidence:** The `MONO_WASM` failure was narrowed to generic generated metadata startup: the runtime path wrapped static abstract generated metadata hooks in delegates before building runtime metadata. Calling `TDatabase.GetDataLinqGeneratedMetadata()` and `TDatabase.SetDataLinqGeneratedMetadata(...)` directly fixes the browser AOT startup failure. The fixed host-side `wasm-aot` report at `artifacts/dev/compat-size-report/20260629-210510424/` publishes successfully and passes the browser smoke at `verifying-strict-parser-projection`. The current host-side `wasm` report at `artifacts/dev/compat-size-report/20260629-205114951/` also passes the same generated SQLite smoke boundary. Clean-output `wasm-aot` and `wasm` reports still fail before browser execution with the Blazor SDK `ResolveWasmOutputs` issue.

**Update 2026-06-30 Phase 24 release evidence:** The final clean-output release report at `artifacts/dev/compat-size-report/20260630-131026977/report.md` passes all four `phase8c` targets with release thresholds and banned-payload failure enabled. Native AOT and trimmed publishes/smokes pass with zero warnings and zero banned payloads. WebAssembly no-AOT and WebAssembly AOT publish, browser-smoke, and stay under the 0.8 Brotli thresholds with zero banned payloads and the expected 13 `WASM0001` diagnostics each. The clean-output blocker is not a current release caveat on this machine; SQLitePCLRaw varargs warning disposition and provider/query breadth remain separate caveats.

## Purpose

Phase 8 proved something real: generated SQLite models can run under Native AOT, trimmed publish, and Blazor WebAssembly AOT. That is a serious milestone.

It did not prove that DataLinq is broadly AOT-compatible. Phase 8C fixed the Roslyn/compiler payload problem, and the 0.8 parser-removal track fixed the Remotion runtime dependency problem. Three caveats still matter:

1. Native AOT verification depends on the local platform toolchain being installed; missing linker/workload prerequisites should be reported as environment failures, not query-pipeline regressions.
2. WebAssembly publish success is not browser runtime proof. Current tooling automates browser execution for WebAssembly targets, so release evidence should come from a fresh browser smoke report rather than publish output alone.
3. The browser SQLite path has worked under WASM AOT, but SQLitePCLRaw native varargs warning disposition still needs a clean call-graph answer when fresh publishes emit `WASM0001`.

This plan describes what must happen before DataLinq can have a practical AOT story: one that works, publishes cleanly, and has file sizes a normal user would not immediately hate.

## Current Evidence

The historical source of truth for the original Phase 8 proof is [Compatibility Results](../archive/roadmap-implementation/phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md). The current repeatable report is:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets phase8c --format summary
```

2026-06-30 final clean-output report: `artifacts/dev/compat-size-report/20260630-131026977/report.md`.

| Target | Result | Current size | Current problem |
| --- | --- | ---: | --- |
| Native AOT SQLite smoke | publish ok, smoke ok, 0 warnings, 0 banned payloads | 7.47 MB executable; 9.29 MB symbol-excluded folder; 41.67 MB folder with PDBs | toolchain prerequisite must be documented; PDBs dominate raw folder size |
| Trimmed SQLite smoke | publish ok, smoke ok, 0 warnings, 0 banned payloads | 22.79 MB symbol-excluded folder; 23.05 MB total | slightly above the old 20 MB target; runtime payload is now honest but still worth watching |
| Blazor WASM no-AOT publish | publish ok, browser smoke ok for the generated SQLite in-memory path, 0 banned payloads, 13 `WASM0001` diagnostics | 3.7 MB Brotli assets; 22.19 MB total | treat as narrow smoke proof, not broad no-AOT browser support |
| Blazor WASM AOT publish | publish ok, browser smoke ok for the generated SQLite in-memory path, 0 banned payloads, 13 `WASM0001` diagnostics | 7.06 MB Brotli assets; 50.29 MB total | clean-output release report is green; warning disposition remains open |

The good news: the generated SQLite path is no longer blocked by Roslyn, Remotion, or local Native AOT toolchain setup on this machine.

The remaining bad news is narrower and more honest: browser runtime proof is automated and green for the generated SQLite smoke path, but the SQLitePCLRaw varargs warning boundary needs a broader disposition instead of silence or global suppression.

## 2026-06-28 Benchmark Refresh

Benchmarks were run on `sqlite-memory` through `DataLinq.Benchmark.CLI`. These are local evidence points, not release gates.

| Lane | Command shape | Result | Interpretation |
| --- | --- | --- | --- |
| Phase 3 query hot path, heavy profile | `run --phase3-query-hotpath --profile heavy` | `RepeatedScalarAny` 122.8 us/op, 14.25 KB/op; `RepeatedInPredicateFetch` 153.3 us/op, 24.52 KB/op; `RepeatedNonPrimaryKeyEqualityFetch` 157.3 us/op, 19.19 KB/op | Best current parser/query-hot-path evidence. `IN` row is stable enough to read; scalar/non-PK rows still show 11-19% noise and need history before trend claims. |
| Phase 3 query hot path, default profile | `run --phase3-query-hotpath --profile default` | scalar Any 121.9 us/op; non-PK equality 229.6 us/op; `IN` predicate 315.2 us/op | Harness passed, but non-PK and `IN` rows had 66-158% noise. Do not use this run as a regression verdict. |
| Phase 2 watchpoints, default profile | `run --phase2-watch --profile default` | warm PK 36.24 us/op; startup PK 689.05 us/op; provider init 1188.73 us/op | Telemetry shape is useful, but timing noise was 188-447%. Use heavy profile or repeated history before interpreting startup/provider changes. |
| Stable plus macro lane, smoke profile | `run --profile smoke -- --anyCategories stable macro-readwrite macro-bulk` | harness passed; CRUD small/batch telemetry showed expected query, transaction, mutation, row-cache, materialization, and relation activity | Smoke profile proves wiring and telemetry shape only. It is intentionally not trustworthy performance evidence. |

The blunt read: the parser-removal work did not leave an obvious benchmark crater in the heavy query-hot-path lane, but the default local runs are too noisy for fine-grained claims. Performance work should use heavy-profile history or comparison artifacts before calling a change faster or slower.

## Outstanding AOT Blocker Catalogue

| Area | Current status | Why it matters | Recommended action |
| --- | --- | --- | --- |
| Browser runtime automation | Implemented in `size-report` through Playwright, local HTTP serving, DOM status polling, console/page-error capture, and target-local `browser-smoke.log` artifacts. Final Phase 24 `wasm-aot` evidence passes at `verifying-strict-parser-projection` after the generated metadata startup fix. | A publishable WebAssembly app can still fail when loaded in a real browser. Phase 23 fixed the runtime path that current automation exposed. | Keep browser smoke in the release gate. |
| no-AOT browser runtime | Current non-clean publish and browser smoke pass for the same generated SQLite in-memory path as AOT. | The small no-AOT payload is interesting now, but it is still a separate support claim from the AOT release-priority path. | Document no-AOT only at the proven generated SQLite smoke boundary unless Phase 24 broadens evidence deliberately. |
| SQLitePCLRaw WebAssembly varargs | Fresh WASM and WASM AOT publishes emit `WASM0001` for `sqlite3_config`, `sqlite3_db_config`, and related SQLitePCLRaw imports. The browser smoke proves provider registration, raw SQLite open, version query, PRAGMA execution, generated database startup, schema creation, insert, relation/projection queries, and parser route evidence do not hit a failing import on the supported path. | Warning visibility still matters. If supported code can call those imports in another browser storage/provider configuration, runtime failure is possible. | Keep warnings visible, document the smoke proof, and do not add global suppression. Broader storage/provider work needs separate call-path evidence. |
| Provider breadth | AOT/trim/browser proof is generated SQLite only. | MySQL/MariaDB may pull different dependencies and cannot be assumed compatible from SQLite smoke evidence. | Add separate Native AOT/trim smoke projects or report targets for server providers where practical. Keep browser support SQLite-only unless a real browser provider story exists. |
| Query breadth | The constrained smoke path exercises only a representative documented subset. | A broad "AOT-compatible" claim would imply more LINQ shapes than the parser supports. | Expand constrained smoke coverage from the LINQ support matrix: aggregates, explicit joins, relation predicates, projection shapes, paging/result operators, and unsupported diagnostics. |
| Reflection compatibility fallbacks | Supported generated paths are strict, but compatibility paths still exist for non-generated or broader projection/member shapes. | Hidden fallback is how AOT support rots: it can work in JIT mode and fail or bloat in Native AOT/WASM. | Keep `AotStrict` style options as guardrails, add analyzer/runtime diagnostics for AOT-intended projects without generated hooks, and do not route constrained smokes through compatibility fallback. |
| Payload and symbols | Native AOT executable is small, but the raw folder is mostly PDB. Trimmed output is slightly above the old 20 MB target. WASM AOT Brotli is now under the old target. | Size claims are easy to make badly. Counting symbols as deployed runtime size is misleading, but ignoring symbols is also release sloppiness. | Package Native AOT symbols separately, keep symbol-excluded sizes in reports, and add threshold warnings only after stable baseline numbers settle. |
| Benchmark signal | Heavy query-hot-path evidence is usable; default local startup/query runs are noisy. | Noisy benchmarks create false confidence and false regressions. | Prefer heavy-profile history for parser/runtime claims. Keep smoke runs for harness validation only. |
| Toolchain prerequisites | Native AOT passes once Visual Studio C++ tooling is installed, even if the normal shell does not resolve `cl.exe`. | Contributors will otherwise misclassify environment setup as product failure. | Document the required workloads and keep `SdkOrWebAssemblyToolchain` classification. Consider a doctor check that distinguishes MSVC linker discovery from PATH resolution. |

## Practical Support Definition

DataLinq should not claim practical AOT support until all of this is true:

- generated model paths publish and run under Native AOT with no DataLinq-owned AOT warnings
- generated model paths publish and run under trimming with no DataLinq-owned trim warnings
- `DataLinq.dll` has no runtime dependency on `Microsoft.CodeAnalysis.*`
- browser publish output does not contain Roslyn assemblies or Roslyn `.wasm` assets
- `Remotion.Linq` is absent from the main runtime package dependency graph
- SQLitePCLRaw WASM warnings are either removed or documented with a verified "not called by this path" proof
- size reports are generated automatically for Native AOT, trimmed, and WASM publishes
- a CI or local release gate rejects accidental payload regressions

The first public support statement should be narrow:

> DataLinq supports generated SQLite models under Native AOT and Blazor WebAssembly AOT for the documented query subset.

Do not broaden that to reflection-discovered models, arbitrary client projection expressions, MySQL/MariaDB browser support, OPFS/file-backed storage, or no-AOT browser WebAssembly beyond the current generated SQLite smoke boundary until those paths are separately proven.

## Size Targets

These are engineering targets, not promises. They exist to stop us from declaring victory while the payload is still silly.

| Target | Current | Practical target | Notes |
| --- | ---: | ---: | --- |
| Native AOT executable, SQLite smoke | 7.47 MB | <= 20 MB | Good. Do not confuse executable size with symbol-inclusive folder size. |
| Native AOT publish folder without PDBs | 9.29 MB | <= 25 MB | Good. The 41.67 MB total folder is mostly the 32.04 MB native PDB. |
| Trimmed self-contained publish folder | 22.79 MB symbol-excluded | <= 20 MB | Close but still above the aspirational old target. Roslyn/Remotion are gone; future wins are ordinary runtime payload discipline. |
| WASM no-AOT Brotli assets | 3.7 MB | <= 6 MB for the smoke app | Size is fine and current browser smoke passes for the generated SQLite boundary. Do not turn that into a broad no-AOT browser support claim. |
| WASM AOT Brotli assets | 7.06 MB | 12-16 MB | Better than the old target, and current browser runtime proof passes. The remaining question is warning disposition, not raw Brotli size. |

The WASM AOT size result is now good enough that size is not the first blocker for the smoke app. SQLitePCLRaw warning disposition and broader query/provider coverage matter more.

## Workstream 1: Split Runtime From Roslyn

Status: implemented in [Phase 8C](../archive/roadmap-implementation/phase-8c-practical-aot-package-graph-and-generated-runtime-hardening/Implementation%20Plan.md).

This was the highest-return work and it is now done.

Historical problem:

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

Status: implemented by the 0.8 parser-removal track through [Phase 7](../roadmap-implementation/v0.8/phase-7-remotion-dependency-removal/README.md).

Historical problem:

- `src/DataLinq/DataLinq.csproj` references `Remotion.Linq`.
- Query code imports Remotion clauses and result operators across:
  - `src/DataLinq/Linq/Queryable.cs`
  - `src/DataLinq/Linq/QueryExecutor.cs`
  - `src/DataLinq/Linq/QueryBuilder.cs`
  - `src/DataLinq/Linq/LocalSequenceExtractor.cs`
  - `src/DataLinq/Linq/Visitors/*`
  - `src/DataLinq/Query/SqlQuery.cs`
- Native AOT and trim publishes pass but emit warnings from `Remotion.Linq`.

The 0.8 implementation did not suppress this. It moved production query execution to DataLinq's expression parser, deleted the Remotion runtime scaffolding, removed the package reference and central package version, rewrote active tests away from Remotion parser APIs, and verified packages/trimmed smoke output without Remotion dependency entries.

The design options below remain useful historical rationale for why the implementation chose the plan-adapter path before deleting Remotion.

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

The archived focused execution plan is [Generated Metadata Contract and Runtime Fallback Removal](../archive/metadata-and-generation/Generated%20Metadata%20Contract%20and%20Runtime%20Fallback%20Removal.md).

The next step is to turn generated models into a clean product contract:

- document generated models as the only AOT-supported model path
- remove runtime reflection metadata-discovery APIs from generated startup instead of marking them as compatibility APIs
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
- generated model startup requires complete generated metadata and has no runtime reflection metadata-discovery fallback
- docs clearly separate AOT-supported and compatibility-only paths
- no AOT smoke test depends on reflection fallback

## Workstream 4: SQLite WebAssembly Warning Hygiene

Current problem:

- WASM AOT publish passes.
- Current browser smoke passes for the generated SQLite AOT path.
- Fresh WebAssembly publishes emit `WASM0001` warnings for SQLitePCLRaw native varargs exports:
  - `sqlite3_config`
  - `sqlite3_db_config`
- The smoke path now explicitly proves provider registration, raw connection open, version query, PRAGMA execution, generated database startup, schema creation, inserts, relations, projections, and parser route evidence without calling a failing varargs import.

The warning says calls to those functions would fail at runtime. Our smoke path does not call them, but that proof is still bounded to the generated SQLite in-memory smoke configuration.

Required work:

1. Identify which managed SQLitePCLRaw methods import those symbols.
2. Determine whether `Microsoft.Data.Sqlite` or the selected SQLitePCLRaw provider can call them during:
   - provider initialization
   - connection open
   - foreign key configuration
   - connection string options
   - future OPFS/file-backed configuration
3. Keep the WASM smoke assertion that covers provider registration, connection open, schema creation, foreign keys, insert, query, projection, and relation loading.
4. If the symbols are unreachable for a broader browser storage/provider path, document the proof and consider a narrowly justified warning suppression at the smoke project level.
5. If they are reachable for realistic configuration, find a provider or initialization path that avoids them.

Do not suppress `WASM0001` in the library package until the call graph is understood beyond the smoke boundary. Suppressing unknown native warnings because one happy-path smoke passed is lazy.

Exit criteria:

- warning disposition is documented with exact methods and call paths
- WASM AOT smoke still passes in browser
- OPFS/file-backed experiment has a separate warning and behavior note before it is claimed

## Workstream 5: Browser Storage and no-AOT Reality

The no-AOT browser story is now supportable only as a narrow generated SQLite smoke claim.

Observed failures:

- `SqliteConnectionStringBuilder` failed under the Mono interpreter.
- after bypassing that, `SQLiteProvider.RegisterProvider` hit a Mono interpreter `NIY` failure.
- Phase 23 current evidence supersedes those historical failures for the generated SQLite in-memory smoke: `artifacts/dev/compat-size-report/20260629-205114951/` publishes and passes browser smoke.

Practical stance:

- keep WASM AOT as the supported browser path
- treat no-AOT as a separately documented smoke result, not as the main browser support story
- do not generalize no-AOT beyond the generated SQLite in-memory smoke without fresh evidence

Storage work should be staged:

1. Keep in-memory SQLite as the proof boundary.
2. Add file-backed or OPFS-oriented storage as a separate experiment.
3. Measure payload and startup impact separately.
4. Do not mix OPFS work with query parser or package graph work.

Exit criteria:

- in-memory browser smoke remains stable
- OPFS/file-backed behavior is documented separately
- no-AOT wording stays scoped to the actual browser smoke that runs

## Workstream 6: Size Reporting and Gates

Manual size notes are useful once. After that they rot.

Status: implemented for the current `phase8c` target set. The report publishes Native AOT, trimmed, WASM no-AOT, and WASM AOT outputs, captures payload sizes, groups warnings, checks banned Roslyn payload, applies optional 0.8 release thresholds, and runs browser smoke for WebAssembly targets through Playwright.

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

Status: still relevant. The current Native AOT folder is 65.32 MB total but only 8.8 MB excluding symbols; `DataLinq.AotSmoke.pdb` is 56.2 MB. Any release artifact that reports the raw folder as runtime size is misleading.

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

1. Automate browser runtime smoke for WebAssembly publish outputs.
2. Keep the current no-AOT browser smoke result recorded as a narrow generated SQLite proof.
3. Document or eliminate the SQLitePCLRaw varargs warnings.
4. Expand constrained smoke coverage across the documented query matrix instead of relying on one representative query path.
5. Add provider-specific Native AOT/trim smoke targets where the dependency graph makes sense.
7. Keep size reports and banned-file checks as release evidence, with symbol-excluded sizes treated as the runtime payload number.
8. Promote the narrow support claim into product docs only after the browser/runtime gates are boring.

This order matters. The dependency graph, parser blockers, current browser runtime blocker, and final clean-output report are already fixed for the release machine. The next honest risks are warning disposition and breadth, not more publish-only evidence.

## Definition of Done

Practical AOT support is ready to document publicly when:

- `DataLinq.TrimSmoke` publishes and runs without Roslyn output
- `DataLinq.AotSmoke` publishes and runs without DataLinq-owned AOT warnings
- `DataLinq.BlazorWasm` publishes and browser-runs under AOT with no Roslyn output
- clean-output WebAssembly publish is green in final release evidence or explicitly classified as an SDK/toolchain caveat
- `Remotion.Linq` is absent from the supported path and main runtime package dependency graph
- WASM warning disposition is documented and tested
- size reports are generated by tooling
- the support boundary says exactly what works and exactly what does not

The current wording is:

> DataLinq has a proven generated SQLite Native AOT, trimmed publish, and Blazor WebAssembly AOT smoke boundary for the documented query subset, keeps Roslyn and Remotion out of runtime package dependency groups, and keeps SQLitePCLRaw `WASM0001` warnings visible as a release-evidence caveat.

Not:

> DataLinq is AOT-compatible.

The second sentence is the one we earn after SQLitePCLRaw warning disposition and broader provider/query coverage stop being open questions.
