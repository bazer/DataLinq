> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 8B Implementation Plan: Generated Contract and Immutable Metadata Foundation

**Status:** Complete for the generated-contract and immutable metadata foundation.

## Purpose

Phase 8 proved that generated SQLite models can publish and run under Native AOT, trimmed publish, and Blazor WebAssembly AOT. That proof was valuable, but the generated runtime contract and metadata construction model were not yet sturdy enough for the next package/runtime work.

Phase 8B closed that foundation gap:

- stale generated output fails during initialization instead of falling back later
- malformed generated declarations fail before reflected metadata parsing and materialization
- metadata producers feed typed drafts into `MetadataDefinitionFactory`
- factory-built runtime metadata is frozen against ordinary setter and structural collection mutation paths
- public mutable metadata construction APIs are obsolete compatibility surface rather than the ordinary product path

The broader work that used to live in this oversized plan is now split:

- Phase 8C owns compatibility size reports, Roslyn/runtime package graph cleanup, complete generated metadata startup, generated indexed access, and package/public wording.
- Phase 17 owns the DataLinq query plan, Remotion adapter, supported-subset parser, AOT query-boundary switch, and SQLitePCLRaw WebAssembly warning disposition.

## Phase-Start Baseline

The planning baseline came from:

- [Phase 8 Compatibility Results](../phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md)
- [Generated Metadata Contract and Runtime Fallback Removal](../../metadata-and-generation/Generated%20Metadata%20Contract%20and%20Runtime%20Fallback%20Removal.md)
- [Immutable Metadata Definitions and Factory Plan](../../metadata-and-generation/Immutable%20Metadata%20Definitions%20and%20Factory%20Plan.md)
- [Practical AOT and Size Plan](../../../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md)

The relevant starting code shape was:

- `IDataLinqGeneratedDatabaseModel<TDatabase>` required `GetDataLinqGeneratedModel()` and `NewDataLinqDatabase(...)`, but stale generated table-model hook compatibility still existed.
- `GeneratorFileFactory` still emitted `GetDataLinqGeneratedTableModels()`.
- `MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(Type)` still searched for generated hooks through reflection and still fell back to the old table-model hook.
- `GeneratedTableModelDeclaration` could represent incomplete declarations that were only caught later by metadata parsing or materialization guards.
- `MetadataFromTypeFactory` still rebuilt ordinary table metadata from reflected attributes, interfaces, properties, enum values, and nullability after finding the generated bootstrap declaration.
- `DatabaseDefinition`, `ModelDefinition`, `TableDefinition`, `ColumnDefinition`, `ValueProperty`, `RelationProperty`, `ColumnIndex`, and `RelationDefinition` still exposed mutable construction surfaces.

The package-graph, generated startup, query-boundary, and SQLitePCLRaw warning items stayed real, but they no longer belong in Phase 8B. They are now split between Phase 8C and Phase 17.

## Goals

- make generated hooks and generated metadata a strict fail-fast runtime contract
- remove stale generated-hook compatibility shims that hide broken or stale generated output
- introduce builder-built immutable runtime metadata definitions before switching generated startup to complete metadata
- move normal metadata production onto typed drafts and the factory path
- freeze factory-built runtime snapshots against ordinary mutation
- obsolete public mutable construction APIs where the product no longer needs them
- keep package/runtime and query-boundary follow-ups separate from this foundation slice

## Non-Goals

- broad "DataLinq is AOT-compatible" marketing
- repeatable size reports and package graph gates
- splitting Roslyn/compiler dependencies out of runtime packages
- complete generated metadata startup
- generated indexed access and metadata handles
- replacing or isolating `Remotion.Linq`
- DataLinq query-plan/parser work
- no-AOT browser WebAssembly support
- MySQL/MariaDB browser support
- OPFS/file-backed browser storage
- full migration execution
- full cache, memory, and invalidation redesign
- arbitrary LINQ provider replacement beyond the current documented support matrix
- general non-SQL backend support before the query-plan boundary exists
- warning suppression as the final answer to Remotion or SQLitePCLRaw warnings

## Implementation Strategy

This phase landed in three foundation slices:

1. Remove stale generated-hook compatibility and make broken generated output fail early.
2. Tighten generated declaration validation before reflected metadata parsing begins.
3. Introduce typed metadata drafts and a factory-owned snapshot/freeze boundary.

This order mattered. Complete generated metadata should not target a mutable graph that the runtime still patches after startup, and package/runtime cleanup should not pretend old generated output remains a valid AOT path.

## Workstream A: Generated Hook Fail-Fast Cleanup

**Status:** Complete as of 2026-05-05.

Goals:

- remove obsolete generated-hook compatibility
- make stale generated output fail early and clearly
- ensure the generated-model path no longer pretends old hooks are acceptable

Completed tasks:

1. [x] Stop emitting `GetDataLinqGeneratedTableModels()` from `GeneratorFileFactory`.
2. [x] Remove tests that assert the old hook is generated, replacing them with tests that assert it is absent.
3. [x] Remove `MetadataFromTypeFactory` fallback to `GetDataLinqGeneratedTableModels()`.
4. [x] Keep `GetDataLinqGeneratedModel()` as the only generated metadata bootstrap hook.
5. [x] Add a runtime test for a database type with only the old hook and assert a clear initialization failure.
6. [x] Make the error message name the missing generated DataLinq metadata hook and the database type.
7. [x] Add migration-note material for consumers with stale generated output.

Implementation notes:

- `GeneratorFileFactory` now emits only `GetDataLinqGeneratedModel()` on generated database partials.
- `MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(Type)` no longer accepts the old `GetDataLinqGeneratedTableModels()` hook as a compatibility fallback.
- The missing-hook failure names the database type and the required `GetDataLinqGeneratedModel` hook.
- Generator and runtime tests assert that the stale hook is absent and that stale generated output fails during metadata initialization.

Migration note:

Projects carrying checked-in or otherwise stale generated DataLinq output must regenerate with a current `DataLinq.Generators` package. Generated database partials that only expose `GetDataLinqGeneratedTableModels()` are no longer accepted; the required bootstrap hook is `GetDataLinqGeneratedModel()`.

Exit criteria:

- [x] generated output contains `GetDataLinqGeneratedModel()` and does not contain `GetDataLinqGeneratedTableModels()`
- [x] the old hook alone is not accepted
- [x] generator and unit tests prove both absence and failure behavior
- [x] no Phase 8 smoke path depends on the stale hook

## Workstream B: Generated Declaration Validation

**Status:** Complete as of 2026-05-05.

Goals:

- move generated contract enforcement to provider initialization
- stop incomplete generated declarations from reaching materialization
- make model-specific failures actionable

Completed tasks:

1. [x] Tighten `GeneratedDatabaseModelDeclaration` and `GeneratedTableModelDeclaration` so complete generated output is the normal construction shape.
2. [x] Remove, obsolete, or internalize constructors that omit immutable type, mutable type, or immutable factory.
3. [x] Add declaration validation before reflected metadata parsing begins.
4. [x] Require `ImmutableType` for every generated table and view model.
5. [x] Require `MutableType` for `TableType.Table`.
6. [x] Treat `MutableType` for views as absent by design.
7. [x] Require exact immutable factory shape:

```csharp
Func<IRowData, IDataSourceAccess, IImmutableInstance>
```

8. [x] Make validation errors include the database type, model type, table/view classification, and missing or malformed member.
9. [x] Keep last-resort guards in `InstanceFactory.NewImmutableRow(...)`, but stop relying on them as primary contract enforcement.

Implementation notes:

- `GeneratedDatabaseModelDeclaration.Validate(...)` and `GeneratedTableModelDeclaration.Validate(...)` now run before reflected metadata parsing in `MetadataFromTypeFactory`.
- `GeneratedTableModelDeclaration` no longer exposes older construction shortcuts that omitted generated immutable type, mutable type, or immutable factory data.
- Validation requires an immutable type and exact immutable factory delegate shape for tables and views, and it requires a mutable type for `TableType.Table`.
- View declarations may keep `MutableType` absent; validation treats that absence as the expected view shape.
- `InstanceFactory.NewImmutableRow(...)` still keeps the materialization-time immutable-factory guard as a defensive backstop.

Exit criteria:

- [x] malformed generated declarations fail during provider initialization
- [x] tests cover missing immutable type, missing mutable table type, missing immutable factory, and wrong factory delegate shape
- [x] materialization-time failures become defensive backstops, not the first visible contract check

## Workstream C: Immutable Metadata Builder And Factory Foundation

**Status:** Complete for the Phase 8B foundation.

Goals:

- move mutation into a builder/factory layer
- make runtime metadata definitions stable snapshots
- prepare generated complete metadata to target the right runtime shape

Completed tasks:

1. [x] Add metadata builder/draft types side by side with the current metadata graph.
2. [x] Represent source-model metadata, generated declarations, SQLite metadata, MySQL metadata, and MariaDB metadata in builder form.
3. [x] Introduce `MetadataDefinitionFactory` as the owner of validation, normalization, relation resolution, column ordinal assignment, cache metadata interpretation, and freeze/finalization.
4. [x] Make expected invalid-model failures return `Option<DatabaseDefinition, IDLOptionFailure>` rather than arbitrary exceptions.
5. [x] Add equivalence tests comparing builder-built metadata against current metadata for generated `EmployeesDb`, `AllroundBenchmark`, and the platform smoke model.
6. [x] Add provider metadata equivalence tests for SQLite and the supported MySQL/MariaDB subset.
7. [x] Move runtime cache defaults out of metadata mutation.
8. [x] Replace in-place metadata transform behavior with a builder/snapshot merge path.
9. [x] Obsolete public mutators and block ordinary post-finalization mutation through setters, structural collection mutation, public arrays, generated declaration arrays, and cache-policy lists.

Implementation notes:

- Source-parsed, generated-runtime, SQLite, MySQL, and MariaDB metadata now enter through typed drafts before factory finalization.
- `MetadataDefinitionFactory` freezes successful snapshots after validation, relation finalization, and column ordinal assignment.
- The main structural metadata collections are freeze-aware after finalization.
- Public array surfaces and generated declaration arrays return defensive copies instead of live storage.
- Provider startup no longer appends runtime cache defaults into `DatabaseDefinition`; runtime cache policy is computed separately.
- Source/database metadata transforms use snapshot merge behavior instead of mutating provider-derived graphs in place.
- Public definition mutators, public structural collection writes, mutable factory inputs, and public mutable parser helpers are obsolete compatibility API.
- SharedCore construction, typed-draft lowering, snapshotting, transformation, provider import, and source parsing use internal construction-only helpers.
- Workstream C deliberately finishes as a factory-owned snapshot foundation rather than a total runtime metadata rewrite. Typed drafts still lower through the mutable graph internally as an implementation detail.

Design stance:

- Internal two-phase construction is acceptable where cyclic metadata references make one-pass immutable construction expensive.
- Prefer immutable or read-only runtime surfaces where compatibility allows it, but keep the first slice focused on blocking ordinary consumer mutation.
- Do not rely on record equality for the graph. Cycles and source-span-insensitive equality need deliberate metadata comparison or digest logic.

Exit criteria:

- [x] runtime metadata returned by the factory cannot be mutated by ordinary consumers
- [x] provider/database startup no longer mutates metadata after build
- [x] metadata equivalence tests are green across source, generated, and provider-derived metadata
- [x] cache policy tests prove defaults are not injected by mutating `DatabaseDefinition`

## Follow-Up Phases

Moved to [Phase 8C](../phase-8c-practical-aot-package-graph-and-generated-runtime-hardening/Implementation%20Plan.md):

- compatibility size reports and banned-payload gates
- runtime-safe metadata split from Roslyn/generator code
- complete generated metadata startup
- generated indexed access and metadata handles
- packaging and public compatibility wording

Moved to the version-scoped [DataLinq 0.8 Roadmap](../../../roadmap-implementation/v0.8/README.md):

- DataLinq query plan behind Remotion
- supported-subset expression parser and AOT boundary switch
- SQLitePCLRaw WebAssembly warning disposition

## Verification Plan

Routine verification for the completed Phase 8B foundation:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- build src\DataLinq.sln --profile ci --output errors
dotnet run --project src\DataLinq.Testing.CLI -- run --suite generators --alias quick --output failures
dotnet run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures
```

## Risk Register

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Generated-hook cleanup breaks stale consumer generated output | Medium | Make the failure explicit, document regeneration requirement, and keep the change tied to the generated/AOT support boundary. |
| Immutable metadata becomes shallow read-only theater | High | Test collection immutability, remove post-build mutation paths, and keep builders separate from definitions. |
| Relation metadata equivalence regresses | High | Add focused equivalence tests for relation parts, candidate keys, generated relation names, and provider-derived foreign keys. |

## Exit Criteria

Phase 8B is complete when:

- generated output no longer emits or relies on `GetDataLinqGeneratedTableModels()`
- missing generated hooks and malformed generated declarations fail during initialization with clear diagnostics
- runtime metadata definitions are immutable snapshots built through a dedicated builder/factory path
- ordinary provider/database startup no longer mutates metadata after build
- metadata equivalence tests are green across source, generated, and provider-derived metadata
- cache policy tests prove defaults are not injected by mutating `DatabaseDefinition`

Until Phase 8C and Phase 17 land, the accurate public statement remains:

> DataLinq has a proven generated SQLite Native AOT, trimmed publish, and Blazor WebAssembly AOT smoke boundary.

Not:

> DataLinq is broadly AOT-compatible.

The second sentence is earned only when the package graph, generated metadata startup, query dependency boundary, and warning story stop arguing with it.
