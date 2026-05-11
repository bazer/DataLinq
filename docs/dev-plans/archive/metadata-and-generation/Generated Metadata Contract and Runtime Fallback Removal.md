> [!WARNING]
> This document is roadmap and engineering planning material. It is not normative product documentation and should not be treated as a shipped support claim.
# Generated Metadata Contract and Runtime Fallback Removal

**Status:** Split plan. Generated-hook cleanup and declaration validation landed in Phase 8B; complete generated metadata startup landed in Phase 8C; generated indexed/handle access remains in the next Phase 8C workstream.

**Created:** 2026-05-05.

**Update 2026-05-11:** Treat the audit below as the original planning snapshot. Workstreams 1 and 2 are covered by the completed Phase 8B foundation. Workstreams 3, 4, and 5 are implemented in [Phase 8C](../roadmap-implementation/phase-8c-practical-aot-package-graph-and-generated-runtime-hardening/Implementation%20Plan.md): generated startup now uses `GetDataLinqGeneratedMetadata()`, no longer rediscovers application model metadata through runtime reflection, and generated value/relation/mutable paths use indexed access and generated handles where useful. Query/projection parser work remains separate in [Phase 17](../../roadmap-implementation/phase-17-query-plan-and-remotion-isolation/Implementation%20Plan.md).

## Purpose

DataLinq should stop treating missing generated model hooks as a recoverable runtime condition. Generated models are the center of the product: they are what make immutable materialization, cache identity, AOT, trimming, and future startup optimization plausible.

The policy should be blunt:

> If a hook should have been source generated, missing generated code is a build or initialization failure, not an invitation to improvise at runtime.

Compatibility fallbacks are useful only when they are explicit legacy APIs. They are harmful when they silently protect broken generated output, stale analyzer payloads, or trimmed-away members.

## Original Audit

This is the important correction: the worst old dynamic-code story is already gone from the checked runtime source. A search of `src` currently finds no `Expression.Compile()` call. Several planning docs still mention expression-compiled constructor fallbacks, but those notes are stale.

Generated path at plan creation:

- `IDataLinqGeneratedDatabaseModel<TDatabase>` requires `GetDataLinqGeneratedModel()` and `NewDataLinqDatabase(...)`.
- `GeneratorFileFactory` emits a database partial implementing that interface.
- The generic provider path calls `TDatabase.GetDataLinqGeneratedModel()` directly through the static abstract interface contract.
- Generated table declarations carry immutable type, mutable type, table type, and an immutable factory delegate.
- `MetadataFromTypeFactory` already throws if the generated table declaration is missing the immutable type or immutable factory.
- `InstanceFactory.NewImmutableRow(...)` throws if the immutable factory is absent or has the wrong delegate shape.

Remaining fallback and runtime-dynamic issues at plan creation:

- `GeneratorFileFactory` still emits the old `GetDataLinqGeneratedTableModels()` shim.
- `MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(Type)` still searches for `GetDataLinqGeneratedModel()` via reflection and then falls back to `GetDataLinqGeneratedTableModels()`.
- `GeneratedTableModelDeclaration` still has permissive constructors that allow declarations without generated immutable type, mutable type, or factory.
- `MetadataFromTypeFactory` still rebuilds database metadata from reflected attributes, interfaces, properties, enum values, and nullability after the generated table bootstrap is found.
- Table mutable type is not treated as required for ordinary tables.
- Generated immutable and mutable property access still goes through `GetValue(nameof(Property))`, which does a runtime name lookup before reaching indexed `RowData`.
- Generated relation access still goes through relation-name lookup.
- New mutable instances still call `ModelDefinition.Find<T>()`, which searches loaded database metadata globally.

The conclusion is simple: the remaining problem is less about runtime code generation and more about runtime discovery. The next improvement is to make the generated contract complete enough that runtime startup does not need to rediscover what the generator already knew.

## Design Stance

Generated models should have a strict contract:

- database model partial generated
- complete generated database hook present
- generated database factory present
- generated table declarations complete
- generated immutable factory present for every table and view model
- generated mutable type present for every real table model
- generated metadata either complete or explicitly marked as compatibility reflection metadata

Runtime should fail during provider initialization when this contract is broken. Materialization-time failures should become a last-resort guard, not the first place a missing hook is detected.

Do not make this configurable at first. A switch like `AllowRuntimeFallbacks` sounds friendly but mostly gives broken projects a longer fuse. If legacy reflection support is kept, expose it through clearly named compatibility APIs, not the ordinary generated-model path.

## Workstream 1: Remove Stale Hook Compatibility

This is the smallest useful slice and should land first.

Tasks:

1. Stop generating `GetDataLinqGeneratedTableModels()`.
2. Remove `MetadataFromTypeFactory` fallback to `GetDataLinqGeneratedTableModels()`.
3. Keep `GetDataLinqGeneratedModel()` as the only generated metadata bootstrap hook.
4. Update generator tests that currently assert the old shim is emitted.
5. Add a runtime test proving a database type with only the old hook fails with a clear initialization exception.

Exit criteria:

- generated output contains `GetDataLinqGeneratedModel()` but not `GetDataLinqGeneratedTableModels()`
- the old hook alone is not accepted
- the exception says the database model is missing the generated DataLinq metadata hook

## Workstream 2: Tighten Generated Declarations

`GeneratedTableModelDeclaration` should not make incomplete generated output easy to represent.

Tasks:

1. Remove or obsolete constructors that omit immutable type, mutable type, or immutable factory.
2. Add a validation method for `GeneratedDatabaseModelDeclaration`.
3. Validate during `MetadataFromTypeFactory` initialization, before parsing reflected properties.
4. Require exact immutable factory shape:

```csharp
Func<IRowData, IDataSourceAccess, IImmutableInstance>
```

5. Require `ImmutableType` for every table and view model.
6. Require `MutableType` for `TableType.Table`.
7. Require `MutableType == null` or ignore it consistently for `TableType.View`.
8. Produce model-specific messages that include the declared model type and missing hook field.

Exit criteria:

- malformed generated declarations fail at provider initialization
- `InstanceFactory.NewImmutableRow(...)` no longer owns primary contract enforcement
- tests cover missing immutable type, missing mutable type for a table, missing immutable factory, and wrong factory delegate shape

## Workstream 3: Generate Complete Runtime Metadata

**Status:** Implemented in Phase 8C.

This is the real startup win.

At plan creation, `GetDataLinqGeneratedModel()` avoided database-property scanning, but `MetadataFromTypeFactory` still reflected over every model type to recover attributes and properties. That was wasteful because the generator already parsed the source model to create the generated classes.

The implemented path now emits a generated `GetDataLinqGeneratedMetadata()` hook that returns a complete typed metadata draft. Generic generated-provider startup calls that static hook directly and feeds the draft into `MetadataDefinitionFactory`. The old runtime rediscovery of model attributes, properties, interfaces, enum declarations, and nullability is gone from the generated startup path.

Recommended direction:

1. Introduce the immutable metadata builder/factory foundation described in [Immutable Metadata Definitions and Factory Plan](Immutable%20Metadata%20Definitions%20and%20Factory%20Plan.md).
2. Add a generated hook that returns complete metadata builder declarations, or extend the existing generated declaration into a complete metadata declaration.
3. Require a generated metadata builder/declaration path instead of reflection in runtime startup.
4. Remove runtime reflection metadata-discovery compatibility; if tooling still needs reflective/source parsing, move it outside the runtime package and name it as tooling, not as fallback.
5. Move shared metadata construction helpers into runtime-safe code that does not depend on Roslyn.
6. Keep relation/index construction deterministic and use the old reflected path only as a temporary migration oracle while generated metadata is being built.

Potential generated hook:

```csharp
public static global::DataLinq.Metadata.DatabaseDefinition BuildDataLinqGeneratedMetadata()
```

That name is deliberately ugly and explicit. It should not collide with user code, and it should be obvious in stack traces.

The generator does not need to hand-write the whole object graph in one massive method if that gets brittle. A practical implementation can emit structured declaration records and let runtime-safe helpers build the graph. The critical part is that source truth comes from the generator, not runtime reflection.

Exit criteria:

- generated model startup does not call `Type.GetCustomAttributes(...)`, `Type.GetProperties(...)`, or `Type.GetInterfaces()` for metadata loading
- generated metadata matches source/provider metadata digests for the active test models
- missing, stale, malformed, or unreadable generated metadata fails during startup with a descriptive `InvalidModel` diagnostic
- Phase 8 smoke projects still pass after trimming and Native AOT publish
- Roslyn types remain outside the runtime-safe metadata builder surface

Implementation evidence:

- `MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel<TDatabase>()` builds from `TDatabase.GetDataLinqGeneratedMetadata()` through `MetadataDefinitionFactory`.
- `DatabaseProvider` no longer contains a runtime app-model metadata fallback when no metadata factory is supplied.
- The non-generic `ParseDatabaseFromDatabaseModel(Type)` path is retained only as a `RequiresUnreferencedCode` compatibility/test path that reflects over the generated hook itself.
- Source/generated digest tests cover `EmployeesDb`, `AllroundBenchmark`, and `PlatformSmokeDb`.
- Phase 8C size report `artifacts/dev/compat-size-report/20260508-133325155/report.md` passed with `Banned = 0` for Native AOT, trimmed, WASM, and WASM AOT targets.

## Workstream 4: Generate Indexed Value Access

`RowData` is already dense and indexed. Generated accessors should use that fact directly.

Current generated immutable value access resembles:

```csharp
public override string Name => _Name ??= (string)GetValue(nameof(Name));
```

That still performs a string lookup against `ModelDefinition.ValueProperties` before using `ColumnDefinition.Index`.

Recommended direction:

1. Add runtime APIs for direct indexed row access, for example `IRowData.GetValue(int columnIndex)`.
2. Generate stable column-index constants per model.
3. Generate immutable getters that use the constant index directly.
4. Generate mutable getters/setters that use either a column-index API or generated static column handles.
5. Keep the existing string/indexer APIs for compatibility and diagnostics, but remove them from generated hot paths.

Exit criteria:

- generated immutable value getters do not call `GetValue(nameof(...))`
- generated mutable value getters/setters do not call `SetValue(nameof(...))` or `GetValue(nameof(...))`
- property access benchmarks prove the change is measurable or at least neutral
- indexed access remains correct when a query reads a subset of columns

## Workstream 5: Generate Relation and Mutable Metadata Handles

Relations and mutable instances still depend on runtime name lookups and global metadata discovery.

Tasks:

1. Generate relation identifiers or relation handles for every generated relation property.
2. Generate relation getters that use those handles instead of `GetImmutableRelation(nameof(...))`.
3. Generate mutable constructors that use generated metadata directly instead of `ModelDefinition.Find<T>()`.
4. Consider generated static table/model metadata properties for each model if that keeps call sites simple.

Exit criteria:

- generated relation getters do not perform relation-name dictionary lookup
- `new MutableFoo()` does not search `DatabaseDefinition.LoadedDatabases`
- unloaded or partially loaded database metadata cannot produce surprising mutable-constructor behavior

## Workstream 6: Keep Query Interpretation Separate

Do not use this plan as an excuse to start generating arbitrary LINQ query plans. That is a different and harder problem.

Projection and local-expression evaluation still contain reflection and invocation for supported computed projections. That belongs with the query-plan and Remotion replacement work, not with the generated metadata contract.

Boundary:

- metadata startup, object construction, property access, and relation access belong in this plan
- arbitrary query/projection parsing belongs in `Remotion.Linq Replacement Plan`
- compatibility wording belongs in the Phase 8C package graph and generated runtime work

## Suggested Sequence

1. Remove stale hook compatibility.
2. Tighten declaration validation.
3. Introduce builder-built immutable metadata definitions.
4. Add generated metadata side by side with the current reflected path and use that path only as a temporary migration oracle.
5. Switch generated-provider startup to require generated metadata and remove the runtime reflection discovery path.
6. Generate indexed value access.
7. Generate relation and mutable metadata handles.
8. Delete or quarantine compatibility reflection paths that are no longer needed.

This order matters. Failing fast first gives clean errors before generated metadata changes the startup shape. Immutable builder-built definitions keep the generated metadata work from targeting the current mutable graph. Generated metadata then removes the biggest normal startup reflection. Indexed access attacks the hot path only after column index stability is proven.

## Verification

Required tests:

- generator output tests for emitted hooks and absence of old shim
- runtime initialization tests for missing generated hook pieces
- immutable metadata factory tests proving built definitions cannot be mutated after construction
- metadata equivalence tests comparing generated metadata against source/provider metadata digests for `EmployeesDb`, `AllroundBenchmark`, and platform smoke models
- mutation tests for generated mutable table metadata
- relation tests for generated relation handles
- AOT and trim smoke publishes after the generated metadata switch
- benchmark or microbenchmark coverage for property getter access before and after indexed access

Useful commands:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- build src\DataLinq.sln --profile ci --output errors
dotnet run --project src\DataLinq.Testing.CLI -- run --suite generators --alias quick --output failures
dotnet run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures
```

Use the repo's sandbox wrapper for broad local verification on Windows.

## Risks

- Generated full metadata can become unreadable if emitted as one giant method. Prefer small helpers or declaration records if the output becomes hostile.
- Runtime metadata objects currently have mutable construction patterns. Generated metadata may need helper APIs before the generator can build it cleanly.
- Removing the old hook may break stale checked-in generated code in consumer projects. That is acceptable for a major compatibility boundary, but release notes should be explicit.
- Direct indexed access depends on stable column ordering. The generator and runtime metadata builder must agree exactly.
- Mutable constructor changes can expose tests that relied on global loaded metadata ordering. Those tests should be fixed, not preserved.

## Done Means

This plan is complete when generated DataLinq models initialize, materialize, mutate, and access relation/value properties without hidden fallback to runtime metadata discovery. Reflection can remain for explicit tooling and compatibility surfaces, but the default generated-model path should not need it.

The end state is not "no reflection anywhere." That would be performative. The real target is sharper:

> No silent fallback in the generated-model path, and no runtime rediscovery of metadata the generator already knew.
