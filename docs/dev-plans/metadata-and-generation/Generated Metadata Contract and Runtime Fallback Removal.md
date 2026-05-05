> [!WARNING]
> This document is roadmap and engineering planning material. It is not normative product documentation and should not be treated as a shipped support claim.
# Generated Metadata Contract and Runtime Fallback Removal

**Status:** Draft plan for Phase 8B.

**Created:** 2026-05-05.

## Purpose

DataLinq should stop treating missing generated model hooks as a recoverable runtime condition. Generated models are the center of the product: they are what make immutable materialization, cache identity, AOT, trimming, and future startup optimization plausible.

The policy should be blunt:

> If a hook should have been source generated, missing generated code is a build or initialization failure, not an invitation to improvise at runtime.

Compatibility fallbacks are useful only when they are explicit legacy APIs. They are harmful when they silently protect broken generated output, stale analyzer payloads, or trimmed-away members.

## Current Audit

This is the important correction: the worst old dynamic-code story is already gone from the checked runtime source. A search of `src` currently finds no `Expression.Compile()` call. Several planning docs still mention expression-compiled constructor fallbacks, but those notes are stale.

Current generated path:

- `IDataLinqGeneratedDatabaseModel<TDatabase>` requires `GetDataLinqGeneratedModel()` and `NewDataLinqDatabase(...)`.
- `GeneratorFileFactory` emits a database partial implementing that interface.
- The generic provider path calls `TDatabase.GetDataLinqGeneratedModel()` directly through the static abstract interface contract.
- Generated table declarations carry immutable type, mutable type, table type, and an immutable factory delegate.
- `MetadataFromTypeFactory` already throws if the generated table declaration is missing the immutable type or immutable factory.
- `InstanceFactory.NewImmutableRow(...)` throws if the immutable factory is absent or has the wrong delegate shape.

Remaining fallback and runtime-dynamic issues:

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

This is the real startup win.

Today `GetDataLinqGeneratedModel()` avoids database-property scanning, but `MetadataFromTypeFactory` still reflects over every model type to recover attributes and properties. That is wasteful because the generator already parsed the source model to create the generated classes.

Recommended direction:

1. Introduce the immutable metadata builder/factory foundation described in [Immutable Metadata Definitions and Factory Plan](Immutable%20Metadata%20Definitions%20and%20Factory%20Plan.md).
2. Add a generated hook that returns complete metadata builder declarations, or extend the existing generated declaration into a complete metadata declaration.
3. Prefer a generated metadata builder/declaration path over reflection in `MetadataFromTypeFactory`.
4. Keep reflection parsing only behind an explicit compatibility method if it is still needed for tooling or tests.
5. Move shared metadata construction helpers into runtime-safe code that does not depend on Roslyn.
6. Keep relation/index construction deterministic and test it against current `MetadataFromTypeFactory` output.

Potential generated hook:

```csharp
public static global::DataLinq.Metadata.DatabaseDefinition BuildDataLinqGeneratedMetadata()
```

That name is deliberately ugly and explicit. It should not collide with user code, and it should be obvious in stack traces.

The generator does not need to hand-write the whole object graph in one massive method if that gets brittle. A practical implementation can emit structured declaration records and let runtime-safe helpers build the graph. The critical part is that source truth comes from the generator, not runtime reflection.

Exit criteria:

- generated model startup does not call `Type.GetCustomAttributes(...)`, `Type.GetProperties(...)`, or `Type.GetInterfaces()` for ordinary metadata loading
- generated metadata matches existing runtime metadata for the active test models
- Phase 8 smoke projects still pass after trimming and Native AOT publish
- Roslyn types remain outside the runtime-safe metadata builder surface

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
- compatibility wording belongs in the Phase 8B AOT package graph work

## Suggested Sequence

1. Remove stale hook compatibility.
2. Tighten declaration validation.
3. Introduce builder-built immutable metadata definitions.
4. Add generated metadata side by side with reflected metadata and assert equivalence.
5. Switch generated-provider startup to generated metadata.
6. Generate indexed value access.
7. Generate relation and mutable metadata handles.
8. Delete or quarantine compatibility reflection paths that are no longer needed.

This order matters. Failing fast first gives clean errors before generated metadata changes the startup shape. Immutable builder-built definitions keep the generated metadata work from targeting the current mutable graph. Generated metadata then removes the biggest normal startup reflection. Indexed access attacks the hot path only after column index stability is proven.

## Verification

Required tests:

- generator output tests for emitted hooks and absence of old shim
- runtime initialization tests for missing generated hook pieces
- immutable metadata factory tests proving built definitions cannot be mutated after construction
- metadata equivalence tests comparing generated metadata against current reflected metadata for `EmployeesDb`, `AllroundBenchmark`, and platform smoke models
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
