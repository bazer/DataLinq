> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Phase 8 Implementation Plan: Native AOT and WebAssembly Readiness

**Status:** Implemented for the generated SQLite Native AOT, trimming, and WebAssembly AOT boundary.

## Purpose

Phase 8 makes DataLinq honest about Native AOT, trimming, and browser WebAssembly. The point is not to sprinkle `<IsTrimmable>true</IsTrimmable>` into project files and declare victory. The point is to prove the supported path with executable smoke projects, remove hot-path dynamic code, and clearly fence off compatibility fallbacks that are not AOT-safe.

## Closeout Result

Phase 8 now has executable proof projects and compatibility results recorded in [Compatibility Results.md](Compatibility%20Results.md).

The implemented support boundary is intentionally narrow:

- generated SQLite database models
- generated metadata hooks
- generated mutable/immutable instance construction
- schema creation from generated metadata
- ordinary SQLite insert/query/relation/projection smoke behavior
- Native AOT publish and executable run
- trimmed publish and executable run
- Blazor WebAssembly AOT publish and browser run

The phase does not claim broad AOT/WASM support. No-AOT browser WebAssembly fails in the Mono interpreter for the SQLite/DataLinq path, `Remotion.Linq` still emits Native AOT/trimming warnings, SQLitePCLRaw emits WebAssembly native varargs warnings, and Roslyn assemblies are still present in constrained publish payloads because the runtime package references compiler APIs.

That result is good enough to close the phase because it proves the architecture direction and removes the dangerous silent fallbacks. It is not good enough to market as complete platform support.

## Audit Snapshot

Current repo state as of the planning audit:

- The roadmap and roadmap implementation index already identify Phase 8 as the next frontier unless full migration execution is deliberately prioritized first.
- Phase 7 is implemented for its planned support boundary: aggregates, computed projections, nullable predicates, joins, and relation-aware predicates.
- The active verification lane now passes for generators 31/31, unit 284/284, and SQLite compliance 389/389.
- Core projects target `net8.0`, `net9.0`, and `net10.0`, which is good for current AOT analyzer coverage.
- `Microsoft.Data.Sqlite` is on `10.0.6`; its package brings in `SQLitePCLRaw.bundle_e_sqlite3` `2.1.11`, and the restored assets include `browser-wasm` native assets selected from the `net9.0` asset group for the `net10.0` restore.
- The existing `src/DataLinq.Blazor` project is an ASP.NET Core web sample, not a standalone browser WebAssembly proof.
- Runtime analyzer coverage is enabled for `DataLinq` without setting `IsAotCompatible`; the broad compatibility flag remains intentionally withheld until dependency warnings are resolved.

Verdict: nothing blocks starting Phase 8. The blockers are the Phase 8 work.

## Known Compatibility Hazards

### Hot or repeated `Expression.Compile()`

These are the highest-value targets:

- `QueryExecutor.GetSelectFunc(...)` compiles client projection delegates for computed selectors.
- `QueryExecutor.GetJoinedSelectFunc(...)` compiles join result selectors.
- `LocalSequenceExtractor.CompileProjection(...)` compiles projection delegates for local sequence extraction.

The entity-selection path already avoids compilation for identity selectors. That is important: normal row materialization is better positioned than the older AOT strategy draft assumed.

### Startup, fallback, or once-per-query dynamic code

These should be classified and either replaced, annotated, or fenced behind explicit diagnostics:

- `InstanceFactory.CreateImmutableFactory(...)` prefers the generated `NewDataLinqImmutableInstance(...)` hook, then falls back to an expression-compiled constructor.
- `InstanceFactory.CreateDatabaseFactory(...)` expression-compiles the database model constructor.
- `Evaluator.SubtreeEvaluator.Evaluate(...)` uses expression compilation for remaining partial-eval shapes after a simple closure-field/property fast path.
- `LocalSequenceExtractor.EvaluateLocalExpression(...)` uses expression compilation for local expressions it cannot reduce to constants.
- `WhereVisitor` uses expression compilation for a simple binary constant evaluation case that should be replaced with direct evaluation.

The startup/fallback paths are not equally bad. A single interpreted constructor delegate is not the same problem as per-row projection interpretation. But for Native AOT, hidden fallback is still dangerous because it silently changes semantics and performance when generated hooks are missing or trimmed.

### Reflection and trimming

Relevant runtime reflection remains:

- `MetadataFromTypeFactory` uses the generated table-model bootstrap if present, then falls back to property scanning and `Assembly.GetTypes()`.
- `InstanceFactory` finds generated immutable hooks with `Type.GetMethod(...)` and creates delegates from reflection metadata.
- Metadata parsing still uses attribute/property/interface reflection in compatibility paths.
- `MetadataTypeConverter.GetType(...)` uses `Type.GetType(...)`.
- `TypeSystem` uses `MakeGenericType(...)` while analyzing query sequence types.
- Provider metadata code has limited reflection, such as the MySQL/MariaDB `IS_GENERATED` property check, but that is server/provider tooling rather than the browser SQLite proof.

The first AOT-safe contract should prefer generated metadata and generated factories. Reflection fallback can remain for non-AOT compatibility, but it should not be the path used by smoke projects.

### Browser/WASM execution model

The cache cleanup worker is not browser-friendly:

- `LongRunningTaskCreator` uses `Task.Factory.StartNew(..., TaskCreationOptions.LongRunning)`.
- `ThreadWorker.Wait(...)` blocks on `WaitHandle.WaitOne(...)`.
- `DatabaseCache` starts `CleanCacheWorker` by default when cache cleanup settings exist.

For browser WebAssembly, this needs an abstraction that can use `PeriodicTimer`, cooperative async scheduling, or a no-background-worker mode. Blocking waits are a bad fit for Blazor's synchronization model.

## Reference Basis

Current Microsoft guidance matters here:

- Native AOT has no JIT at runtime, requires trimming, and `System.Linq.Expressions` uses interpreted form, so expression-compiled code is not a free optimization in AOT mode: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/>
- Library AOT compatibility should be validated with `IsAotCompatible`/analyzers on modern TFMs, but the flag should not be set until warnings are understood: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/>
- Trimming work should use both library analyzer warnings and a trimming test app rooted on the library: <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming>
- `RequiresDynamicCode` warnings are real compatibility signals; the best fix is usually to avoid the dynamic-code API on the AOT path: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings>
- Blazor WebAssembly AOT uses `RunAOTCompilation`, requires the WebAssembly workload, improves CPU-bound runtime behavior, and increases payload size: <https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot>
- WebAssembly native dependencies are linked through browser-specific native assets and must be compatible with the WebAssembly toolchain: <https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-native-dependencies>
- Blazor code should avoid thread-blocking calls and marshal external notifications through the synchronization context: <https://learn.microsoft.com/en-us/aspnet/core/blazor/components/synchronization-context>
- `Microsoft.Data.Sqlite` is the ADO.NET SQLite provider DataLinq already uses: <https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/>

## Non-Goals

- Full query-provider rewrite or provider-independent query IR.
- Full async query/mutation API design.
- Full migration execution.
- MySQL/MariaDB browser support.
- Claiming `IsAotCompatible=true` while warnings are merely suppressed.
- Removing all reflection from non-AOT compatibility paths.
- Supporting arbitrary anonymous/client projections without expression interpretation in the first slice.

## Workstream A: Executable Compatibility Probes

Goals:

- make AOT/trimming/browser failures reproducible before changing behavior
- keep compatibility claims tied to build and runtime evidence

Tasks:

1. Add a small Native AOT smoke project, for example `src/DataLinq.AotSmoke`, using a tiny generated SQLite model.
2. Publish it with `PublishAot=true` for at least one local RID and treat IL/AOT warnings as findings.
3. Add a trim smoke project or publish profile with `PublishTrimmed=true` and `TrimmerRootAssembly` entries for `DataLinq` and `DataLinq.SQLite`.
4. Add a standalone Blazor WebAssembly sample, for example `src/DataLinq.BlazorWasm`, separate from the current ASP.NET Core web sample.
5. Start the browser sample with SQLite in-memory/shared-memory behavior, then add the OPFS/file persistence experiment once the basic runtime works.
6. Capture payload size, publish output, startup, first query, and repeated query measurements in a short results document.

Exit criteria:

- Native AOT publish exists and either passes cleanly or lists exact warnings/failures.
- Trim publish exists and either passes cleanly or lists exact warnings/failures.
- Blazor WebAssembly publish exists and proves SQLite/DataLinq can execute a trivial query in the browser.

## Workstream B: Dynamic-Code Inventory and Policy

Goals:

- classify every dynamic-code path before changing it
- avoid wasting time on harmless startup paths while hot paths still interpret per row

Tasks:

1. Keep a checked-in inventory table for `Expression.Compile()`, `MakeGenericType`, broad reflection, and dynamic type lookup.
2. Classify each item as one of: per row, per query, startup, fallback, tooling/provider-only, or test-only.
3. Add a runtime compatibility mode or internal feature flag that lets tests assert when an AOT-safe path is expected.
4. Decide the public diagnostic shape for unsupported AOT-safe execution, probably an exception that points to source generation or unsupported projection shape rather than a generic runtime failure.

Exit criteria:

- Every dynamic-code call site has an owner and disposition.
- The AOT-safe contract is explicit enough that future code can fail tests when it reintroduces dynamic hot-path work.

## Workstream C: Generated Metadata and Factory Contract

Goals:

- make generated metadata and generated construction the expected AOT path
- preserve compatibility fallbacks without making them look AOT-safe

Tasks:

1. Extend generated database metadata so runtime code can determine that a database model has complete generated metadata.
2. Prefer generated table-model declarations in `MetadataFromTypeFactory`; fallback reflection remains compatibility-only.
3. Add diagnostics when generated metadata is missing under AOT-safe mode.
4. Extend generator output or runtime registration so immutable instance creation can avoid reflective `GetMethod(...)` lookup where practical.
5. Add a generated or statically cached database model factory path for `ReadOnlyAccess<T>`.
6. Keep reflection/constructor fallback for non-generated models, but mark or guard it as not AOT-safe.

Design stance:

- Do not start with clever static abstract interface constraints unless they clearly simplify the call graph. A generated registry/switch is uglier but likely more predictable and easier to prove.
- Do not break existing non-generated compatibility in the first pass.

Exit criteria:

- Generated models can be materialized without expression-compiled constructor fallback.
- Missing generated metadata/factory paths produce clear diagnostics in AOT-safe mode.

## Workstream D: Projection and Local Evaluation Without Hot-Path Compilation

Goals:

- remove the dynamic-code paths that matter most in browser and Native AOT workloads
- keep unsupported projection shapes honest

Tasks:

1. Replace simple binary constant evaluation in `WhereVisitor` with direct operator evaluation.
2. Replace identity and direct-member projections with hand-built delegates or generated accessors where possible.
3. For computed projections, choose a narrow first boundary:
   - support direct member access, nullable `.Value`, simple string concatenation, and primitive arithmetic if implementation is clean
   - reject complex method calls or nested object graphs with `QueryTranslationException`
4. Replace join result selector compilation for supported anonymous/simple member projections.
5. Replace local-sequence projection compilation for the local `Contains`/`Any` cases already supported by the translator.
6. Leave `Evaluator.PartialEval` fallback for once-per-query expression evaluation if the AOT smoke numbers show it is not a practical problem, but annotate and track it.

Exit criteria:

- Common entity queries and documented computed projections do not rely on `Expression.Compile()` on the AOT-safe path.
- Unsupported projection shapes fail clearly instead of falling into interpreted dynamic execution silently.

## Workstream E: Trimming and Analyzer Hardening

Goals:

- move from "it runs locally" to "the SDK analyzers agree with the contract"
- avoid marking packages trimmable before the warnings are real

Tasks:

1. Enable `EnableTrimAnalyzer` and `EnableAotAnalyzer` on candidate runtime projects for `net8.0+` without immediately setting `IsAotCompatible`.
2. Fix or annotate warnings from `DataLinq` and `DataLinq.SQLite`.
3. Use `DynamicallyAccessedMembers`, `RequiresUnreferencedCode`, `RequiresDynamicCode`, or `UnconditionalSuppressMessage` only where the justification is precise and backed by tests.
4. Only after warning cleanup, set `IsAotCompatible` on the packages that can honestly claim it.
5. Keep `VerifyReferenceAotCompatibility` as a later strictness option because dependency metadata can be noisy.

Exit criteria:

- Analyzer warnings are either gone or intentionally surfaced to callers.
- `DataLinq` and `DataLinq.SQLite` have a defensible compatibility annotation story.

## Workstream F: WebAssembly SQLite Proof

Goals:

- prove the browser target as a product scenario, not just a package restore
- expose payload and runtime tradeoffs early

Tasks:

1. Create a standalone Blazor WebAssembly sample using `DataLinq.SQLite` and generated models.
2. Confirm `Microsoft.Data.Sqlite`/SQLitePCLRaw native assets publish for the selected `net10.0` browser target.
3. Start with a tiny in-memory database, then test file-backed or OPFS-oriented storage separately.
4. Disable or adapt background cache cleanup for the browser sample.
5. Add a page or benchmark endpoint that runs:
   - schema creation
   - seed insert
   - first query
   - repeated query
   - computed projection
   - relation predicate if the sample schema supports it
6. Publish with and without `RunAOTCompilation=true`, compare payload size and query timings, and record the numbers.

Exit criteria:

- The sample publishes in Release.
- The sample runs in a real browser.
- SQLite query and mutation work at the narrow sample boundary.
- AOT and non-AOT payload/runtime measurements are recorded.

## Workstream G: Cache Cleanup and Browser Scheduling

Goals:

- prevent background cache behavior from blocking or failing in browser WebAssembly
- keep server behavior stable

Tasks:

1. Replace `LongRunningTaskCreator` with a runtime scheduling abstraction.
2. Add a cooperative timer-backed implementation using `PeriodicTimer` or an equivalent async loop.
3. Add a no-background-worker mode for browser environments if timer-based cleanup is still too risky.
4. Make `DatabaseCache` choose the worker policy from provider/options rather than always constructing `LongRunningTaskCreator`.
5. Add tests for start/stop/dispose behavior and cache cleanup without blocking waits.

Exit criteria:

- Browser sample does not use `TaskCreationOptions.LongRunning` or blocking wait handles for cache cleanup.
- Existing server/unit behavior remains green.

## Verification Plan

Routine verification while implementing:

- `run --suite generators --alias quick --output failures --build`
- `run --suite unit --alias quick --output failures --build`
- `run --suite compliance --alias quick --output failures --build`
- Native AOT smoke publish
- Trim smoke publish
- Blazor WebAssembly Release publish

Before closing the phase:

- `run --suite all --alias quick --output failures --build`
- server-backed compliance for the latest MySQL/MariaDB targets if provider/runtime code changed
- browser smoke run from the published Blazor WebAssembly output
- a short results note with warnings, payload sizes, and timing interpretation

## Exit Criteria

Phase 8 is complete when:

- there is an executable Native AOT smoke path for generated SQLite models
- there is an executable trim smoke path for the runtime packages
- common entity materialization and documented projection paths avoid hot-path `Expression.Compile()`
- generated metadata/factory paths are the preferred AOT-safe path
- missing generated hooks produce clear diagnostics instead of silent runtime fallback
- `DataLinq` and `DataLinq.SQLite` have an honest AOT/trimming analyzer stance
- a standalone Blazor WebAssembly SQLite sample publishes and runs
- browser cache cleanup behavior is explicitly handled
- docs do not present Native AOT or WebAssembly support as shipped beyond the proven boundary
