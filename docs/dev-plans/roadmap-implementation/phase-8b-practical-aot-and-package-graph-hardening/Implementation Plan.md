> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 8B Implementation Plan: Practical AOT and Package Graph Hardening

**Status:** Active recommended implementation slice after Phase 8.

## Purpose

Phase 8 proved that generated SQLite models can publish and run under Native AOT, trimmed publish, and Blazor WebAssembly AOT. That proof is valuable, but it is not yet a product-grade AOT story.

Phase 8B turns that smoke proof into something less embarrassing to ship:

- generated models fail fast when the source-generated contract is missing or stale
- runtime metadata moves toward immutable snapshots instead of mutable graphs patched together during startup
- runtime packages stop dragging Roslyn/compiler assemblies into trimmed and browser outputs
- compatibility reports become repeatable instead of hand-measured folder notes
- `Remotion.Linq` is replaced or isolated from the generated/AOT support boundary
- SQLitePCLRaw WebAssembly warnings get a call-path answer instead of a shrug

The blunt distinction is this:

> Phase 8 proved the direction. Phase 8B earns the right to document the direction as practical support.

## Phase-Start Baseline

The bullets in this section capture the audit state at the start of Phase 8B. Completed workstream sections below record the current implementation state as this phase progresses.

The planning baseline comes from:

- [Phase 8 Compatibility Results](../phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md)
- [Practical AOT and Size Plan](../../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md)
- [Generated Metadata Contract and Runtime Fallback Removal](../../metadata-and-generation/Generated%20Metadata%20Contract%20and%20Runtime%20Fallback%20Removal.md)
- [Immutable Metadata Definitions and Factory Plan](../../metadata-and-generation/Immutable%20Metadata%20Definitions%20and%20Factory%20Plan.md)
- [Remotion.Linq Replacement Plan](../../query-and-runtime/Remotion.Linq%20Replacement%20Plan.md)
- [Warning Cleanup Plan](../../tooling/Warning%20Cleanup%20Plan.md)
- [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)

The current code shape matters:

- `src/DataLinq/DataLinq.csproj` references `Microsoft.CodeAnalysis.CSharp` and `Remotion.Linq`.
- `src/DataLinq/DataLinq.csproj` compiles `src/DataLinq.SharedCore/**/*.cs` into the runtime package, so Roslyn-using shared-core files currently become runtime package baggage.
- `src/DataLinq.SharedCore/Metadata/CsTypeDeclaration.cs`, `PropertyDefinition.cs`, `SyntaxParser.cs`, `MetadataFromModelsFactory.cs`, `ModelFileFactory.cs`, and `GeneratorFileFactory.cs` still sit in the shared surface even though several of those responsibilities are generator/tooling concerns.
- `IDataLinqGeneratedDatabaseModel<TDatabase>` now requires `GetDataLinqGeneratedModel()` and `NewDataLinqDatabase(...)`, and the generic provider path uses the static abstract generated hook directly.
- `GeneratorFileFactory` still emits the stale `GetDataLinqGeneratedTableModels()` shim, and generator/unit tests still assert that shim.
- `MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(Type)` still searches for generated hooks through reflection and still falls back to the old table-model hook.
- `GeneratedTableModelDeclaration` can still represent incomplete declarations that are only caught later by metadata parsing or materialization guards.
- `MetadataFromTypeFactory` still rebuilds ordinary table metadata from reflected attributes, interfaces, properties, enum values, and nullability after finding the generated bootstrap declaration.
- `DatabaseDefinition`, `ModelDefinition`, `TableDefinition`, `ColumnDefinition`, `ValueProperty`, `RelationProperty`, `ColumnIndex`, and `RelationDefinition` still expose mutable construction surfaces.
- `src/DataLinq.AotSmoke` and `src/DataLinq.TrimSmoke` still root `Remotion.Linq` because the runtime query path depends on it.
- `src/DataLinq.BlazorWasm` publishes and runs under WebAssembly AOT, but SQLitePCLRaw emits `WASM0001` warnings for `sqlite3_config` and `sqlite3_db_config`.
- A current source search finds no `Expression.Compile()` in the checked DataLinq LINQ/instance runtime paths. Older plans that describe expression-compiled constructor fallbacks are stale on that specific point.

The measured Phase 8 output is also the starting evidence:

| Target | Current result | Current size | Main remaining problem |
| --- | --- | ---: | --- |
| Native AOT SQLite smoke | publish and executable run pass | 18.61 MiB executable, 76.76 MiB folder including PDBs | `Remotion.Linq` warnings remain; symbols dominate folder size |
| Trimmed SQLite smoke | publish and executable run pass | 32.10 MiB folder | Roslyn and `Remotion.Linq` remain in the runtime graph |
| Blazor WASM no-AOT | publish passes, browser run fails | 7.91 MiB Brotli assets | Mono interpreter fails on the SQLite/DataLinq path |
| Blazor WASM AOT | publish and browser smoke pass | 18.80 MiB Brotli assets | AOT runtime size, Roslyn payload, and SQLitePCLRaw warnings |

## Goals

- make generated metadata and generated factories a strict runtime contract for the generated-model path
- remove stale generated-hook compatibility that hides broken or old generated output
- introduce builder-built immutable metadata definitions before generating complete runtime metadata
- switch generated-model startup away from rediscovering ordinary model metadata through runtime reflection
- add repeatable size, warning, and banned-payload reporting for constrained publishes
- split runtime-safe metadata from Roslyn/generator code
- remove `Microsoft.CodeAnalysis.*` from `DataLinq.dll` runtime dependency groups and constrained publish outputs
- introduce a DataLinq-owned query plan behind the current Remotion parser
- move SQL generation and supported query diagnostics behind that plan
- build a supported-subset expression parser that can serve the generated/AOT path
- remove or isolate `Remotion.Linq` from the practical AOT support boundary
- investigate SQLitePCLRaw WebAssembly warnings with exact call-path evidence
- keep public compatibility wording narrow until the gates are clean

## Non-Goals

- broad "DataLinq is AOT-compatible" marketing
- no-AOT browser WebAssembly support
- MySQL/MariaDB browser support
- OPFS/file-backed browser storage as part of the first hardening pass
- full migration execution
- full cache, memory, and invalidation redesign
- arbitrary LINQ provider replacement beyond the current documented support matrix
- general non-SQL backend support before the query-plan boundary exists
- warning suppression as the final answer to Remotion or SQLitePCLRaw warnings

## Implementation Strategy

This phase should land in disciplined slices. The first slice should be small and corrective: remove stale generated-hook compatibility and make malformed generated declarations fail during initialization. That is the cleanest way to stop silent fallback before bigger metadata and package work changes the runtime shape.

After that, the phase splits into two related lanes:

1. **Metadata/package lane:** immutable metadata builders, complete generated metadata, Roslyn split, size reports, and package gates.
2. **Query/AOT lane:** DataLinq query plan, Remotion adapter, supported-subset expression parser, parser parity, and AOT boundary switch.

The lanes can overlap, but the dependency order is real:

- complete generated metadata should not target the current mutable metadata graph
- Remotion replacement should not start by rewriting SQL generation and parser behavior in one move
- Roslyn split should happen before anyone interprets WebAssembly size numbers as meaningful product data
- SQLitePCLRaw warning suppression should wait until the affected native exports are mapped to managed call paths

## Workstream A: Generated Hook Fail-Fast Cleanup

**Status:** Complete as of 2026-05-05.

Goals:

- remove obsolete generated-hook compatibility
- make stale generated output fail early and clearly
- ensure the generated-model path no longer pretends old hooks are acceptable

Tasks:

1. [x] Stop emitting `GetDataLinqGeneratedTableModels()` from `GeneratorFileFactory`.
2. [x] Remove tests that assert the old hook is generated, replacing them with tests that assert it is absent.
3. [x] Remove `MetadataFromTypeFactory` fallback to `GetDataLinqGeneratedTableModels()`.
4. [x] Keep `GetDataLinqGeneratedModel()` as the only generated metadata bootstrap hook.
5. [x] Add a runtime test for a database type with only the old hook and assert a clear initialization failure.
6. [x] Make the error message name the missing generated DataLinq metadata hook and the database type.
7. [x] Add release-note or migration-note material for consumers with stale generated output.

Implementation notes:

- `GeneratorFileFactory` now emits only `GetDataLinqGeneratedModel()` on generated database partials.
- `MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(Type)` no longer accepts the old `GetDataLinqGeneratedTableModels()` hook as a compatibility fallback.
- The missing-hook failure names the database type and the required `GetDataLinqGeneratedModel` hook.
- Generator and runtime tests now assert that the stale hook is absent and that stale generated output fails during metadata initialization.

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

Tasks:

1. [x] Tighten `GeneratedDatabaseModelDeclaration` and `GeneratedTableModelDeclaration` so complete generated output is the normal construction shape.
2. [x] Remove, obsolete, or internalize constructors that omit immutable type, mutable type, or immutable factory.
3. [x] Add declaration validation before reflected metadata parsing begins.
4. [x] Require `ImmutableType` for every generated table and view model.
5. [x] Require `MutableType` for `TableType.Table`.
6. [x] Treat `MutableType` for views consistently, either absent or ignored by design.
7. [x] Require exact immutable factory shape:

```csharp
Func<IRowData, IDataSourceAccess, IImmutableInstance>
```

8. [x] Make validation errors include the database type, model type, table/view classification, and missing or malformed member.
9. [x] Keep last-resort guards in `InstanceFactory.NewImmutableRow(...)`, but stop relying on them as primary contract enforcement.

Implementation notes:

- `GeneratedDatabaseModelDeclaration.Validate(...)` and `GeneratedTableModelDeclaration.Validate(...)` now run before reflected metadata parsing in `MetadataFromTypeFactory`.
- `GeneratedTableModelDeclaration` no longer exposes the older construction shortcuts that omitted generated immutable type, mutable type, or immutable factory data.
- Validation requires an immutable type and exact immutable factory delegate shape for tables and views, and it requires a mutable type for `TableType.Table`.
- View declarations may keep `MutableType` absent; the validation treats that absence as the expected view shape.
- `InstanceFactory.NewImmutableRow(...)` still keeps the materialization-time immutable-factory guard as a defensive backstop.

Exit criteria:

- [x] malformed generated declarations fail during provider initialization
- [x] tests cover missing immutable type, missing mutable table type, missing immutable factory, and wrong factory delegate shape
- [x] materialization-time failures become defensive backstops, not the first visible contract check

## Workstream C: Immutable Metadata Builder And Factory Foundation

**Status:** In progress. The primary metadata producers now feed typed drafts into the factory, and factory-built runtime graphs are frozen against setter-style mutation plus the main structural collection mutation paths. Public array surfaces, including generated declaration table-model arrays, now return defensive copies, cache-policy collection surfaces are freeze-aware after finalization, and the public definition mutator plus mutable-factory input surfaces are now obsolete compatibility API. SharedCore construction paths now use internal construction-only mutation helpers while the remaining mutable lowering graph is replaced.

Goals:

- move mutation into a builder/factory layer
- make runtime metadata definitions stable snapshots
- prepare generated complete metadata to target the right runtime shape

Tasks:

1. [in progress] Add metadata builder/draft types side by side with the current metadata graph. The typed database/table/model/property/column draft records now exist, carry source diagnostics, and are used by the primary metadata producers; the mutable graph remains the internal lowering/finalization target, but factory-built snapshots are now frozen against ordinary setter-style mutation and SharedCore lowering now goes through internal construction APIs.
2. [x] Represent source-model metadata, generated declarations, SQLite metadata, MySQL metadata, and MariaDB metadata in builder form. Source-model, generated runtime, SQLite, MySQL, and MariaDB metadata now build typed drafts before factory finalization.
3. [in progress] Introduce a `MetadataDefinitionFactory` or equivalent factory that owns validation, normalization, relation resolution, column ordinal assignment, cache metadata interpretation, and freeze/finalization. The factory now freezes successful snapshots after finalization; table index collections, model property dictionaries, index column/relation-part collections, and cache-policy collections are freeze-aware.
4. [in progress] Make expected invalid-model failures return `Option<DatabaseDefinition, IDLOptionFailure>` rather than arbitrary exceptions.
5. [x] Add equivalence tests comparing builder-built metadata against current metadata for generated `EmployeesDb`, `AllroundBenchmark`, and the platform smoke model. Source-parsed and generated-runtime metadata now both enter through typed drafts, and the representative equivalence coverage stays green across those paths.
6. [x] Add provider metadata equivalence tests for SQLite and the supported MySQL/MariaDB subset.
7. [x] Move runtime cache defaults out of metadata mutation. Provider initialization should compute effective cache policy rather than appending defaults into `DatabaseDefinition`.
8. [x] Replace in-place metadata transform behavior with a builder/snapshot merge path.
9. [in progress] After parity is proven, remove or obsolete public mutators and expose immutable or read-only collections. Structural list/dictionary mutation is now blocked after freeze, public arrays and generated declaration arrays no longer expose live storage, cache-policy lists now reject post-finalization mutation, direct global metadata registry mutation is obsolete, public definition mutators are obsolete, and SharedCore no longer builds through those public mutators; replacing the internal mutable lowering graph remains.

Foundation slice notes:

- Added `MetadataDefinitionFactory.Build(...)` as the first centralized finalization path for metadata drafts.
- The factory now owns duplicate table validation, duplicate column validation, index parsing, relation resolution, and column ordinal assignment for the paths wired so far.
- `MetadataFromTypeFactory` and source-generator `MetadataFromModelsFactory` now call the factory instead of open-coding those finalization steps.
- New unit coverage proves the factory assigns ordinals, creates primary/foreign-key indices, resolves bidirectional relation parts, and returns failures for duplicate columns and missing primary keys.
- Duplicate source relation properties that match the same database relation now return an `InvalidModel` option failure instead of escaping as a `SingleOrDefault` exception during relation resolution.
- Invalid relation-created foreign-key indices, such as an empty foreign-key constraint/index name, now return an `InvalidModel` option failure instead of escaping through the factory catch-all as a generic exception.
- SQLite, MySQL, and MariaDB metadata importers now delegate provider-style finalization to `MetadataDefinitionFactory.BuildProviderMetadata(...)`, keeping provider interface normalization while centralizing duplicate validation, index parsing, relation resolution, and column ordinal assignment.
- `DatabaseProvider` no longer appends default cache limits, cache cleanup intervals, or index-cache policies into `DatabaseDefinition` during startup.
- Added runtime `DatabaseCachePolicy` so the cache layer computes effective defaults without changing metadata: cache-enabled databases still get the prior 256 MiB, 30 minute, 10 minute cleanup, and 1,000,000-row index-cache defaults; cache-disabled databases keep the legacy 5 minute cleanup worker fallback without inheriting cache limits or index-cache behavior.
- New unit coverage proves provider startup leaves the mutable metadata lists empty while runtime cache defaults remain effective.
- Added an internal `MetadataDefinitionSnapshot` copier and `MetadataTransformer.TransformDatabaseSnapshot(...)` so source/database metadata merges can return a new graph instead of rewriting the database-derived graph in place.
- `ModelGenerator` now uses the snapshot-transform path when merging source model files into provider-derived metadata.
- Direct transformer tests now exercise `TransformDatabaseSnapshot(...)`; the old `TransformDatabase(...)` and `TransformTable(...)` mutating methods remain callable but are marked obsolete.
- Added source-parsed versus generated-runtime metadata equivalence coverage for `EmployeesDb`, `AllroundBenchmark`, and the platform smoke model. The digest covers database identity, cache metadata, table shape, column shape, index shape, relation shape, database types, enum shape, and C# nullability.
- The equivalence pass fixed source/generated drift: reflected `[Database]` metadata now normalizes `DatabaseDefinition.Name`, file-based source parsing now includes top-level enum declarations and enum-typed properties, and reflected runtime metadata now preserves nullable reference annotations on properties.
- Added provider-generated-source metadata equivalence checks for SQLite, MySQL, and MariaDB first-slice schemas using a shared metadata digest and generated-source roundtrip helper.
- The provider equivalence pass fixed generated model column ordering, provider auto-increment C# nullability, and provider-created nullable foreign-key relation metadata so generated source preserves the provider metadata shape.
- Added `MetadataDefinitionDraft` as the first explicit factory draft boundary. Generated runtime metadata, source-parsed metadata, SQLite metadata, MySQL metadata, and MariaDB metadata now hand drafts to `MetadataDefinitionFactory` instead of asking the factory to finalize the parser/provider graph directly.
- Added first typed metadata draft records for databases, table models, models, tables/views, value properties, relation properties, and columns. `MetadataDefinitionFactory` can now build from typed drafts by lowering them through the existing mutable graph internally, giving Workstream C a real non-parser/non-provider draft input to migrate toward without changing every metadata producer in one slice.
- Added first typed-draft equivalence coverage by building the relation-focused factory draft through the typed draft records and comparing its finalized digest against the mutable draft path.
- SQLite metadata import now uses a provider-local typed draft for discovered tables, views, columns, defaults, indexes, and foreign-key attributes, then calls `MetadataDefinitionFactory.BuildProviderMetadata(MetadataDatabaseDraft)` instead of building an intermediate mutable `DatabaseDefinition` graph.
- MySQL and MariaDB metadata import now share provider-local typed draft classes for discovered tables, views, columns, defaults, enum metadata, comments, check constraints, indexes, and foreign-key attributes, then call `MetadataDefinitionFactory.BuildProviderMetadata(MetadataDatabaseDraft)` instead of building intermediate mutable `DatabaseDefinition` graphs.
- Generated runtime metadata import now builds typed drafts from `GeneratedDatabaseModelDeclaration`, `GeneratedTableModelDeclaration`, and reflected model/property attributes before calling `MetadataDefinitionFactory.Build(MetadataDatabaseDraft)`.
- Source-model metadata import now builds typed drafts from Roslyn database/model/property syntax before calling `MetadataDefinitionFactory.Build(MetadataDatabaseDraft)`. Typed drafts now carry C# file declarations, syntax spans, property source info, and attribute source spans so source diagnostics survive the draft-to-runtime conversion.
- `MetadataDefinitionFactory` now snapshots the draft before finalization, so interface assignment, index creation, relation resolution, and column ordinal assignment happen on the returned runtime graph without mutating the draft graph. Stub table models are preserved across the snapshot boundary.
- `MetadataDefinitionFactory` now freezes successful metadata snapshots after validation, relation finalization, and column ordinal assignment. Database, table-model, table/view, model, property, column, index, relation, and provider type setter-style mutators reject post-finalization changes; public collection sealing remains the next compatibility cleanup.
- Table index collections, model value/relation property dictionaries, and index column/relation-part collections now use freeze-aware runtime collection types, closing the main direct structural collection mutation bypasses after factory finalization. Enum metadata now exposes read-only value lists.
- Factory freeze coverage now exercises all public mutating routes on the freeze-aware metadata list/dictionary wrappers, including add-range, insert, replacement, remove, remove-at, and key/value-pair dictionary writes.
- Array-shaped metadata properties now preserve their existing public API shape while returning defensive copies instead of live backing arrays. This closes element-assignment bypasses for database attributes/table models, table columns/primary keys, model interfaces/usings/attributes, property attributes, and column database types without forcing broad `Length`/indexer call-site churn.
- `GeneratedDatabaseModelDeclaration.TableModels` now follows the same defensive-copy pattern, so generated metadata declarations cannot be changed by mutating the constructor input array or a returned table-model array.
- Database and table cache-policy metadata lists now use the same freeze-aware runtime collection type, so finalized metadata no longer allows cache limit, cache cleanup, or index-cache policy edits through list mutation.
- `DatabaseCachePolicy` now snapshots table-specific cache limits and index-cache policies at cache construction. `RemoveRowsBySettings()` and table index-cache setup read the policy snapshot rather than live table metadata, and the compliance cache-setting test uses a scoped internal runtime override instead of mutating shared provider metadata.
- Runtime code no longer reads or writes the raw `DatabaseDefinition.LoadedDatabases` dictionary directly. The old public global registry remains for compatibility but is marked obsolete, while provider startup and metadata lookup now go through internal registry helpers.
- Public mutators on database, table-model, table/view, model, property, column, index, and relation metadata are now marked obsolete with a shared message pointing callers to typed drafts and `MetadataDefinitionFactory`. Reflection coverage locks the obsoletion surface, and legacy fixture code locally suppresses the warnings only where tests still intentionally exercise compatibility construction or corrupt existing graphs directly.
- `SyntaxParser`, `MetadataDefinitionFactory` lowering, snapshot copying, typed-draft conversion, and metadata transformation now call internal construction-only mutator helpers instead of the obsolete public compatibility surface, removing product-side CS0618 suppression from the Workstream C lowering path.
- Cache policy, generator-file, model-file generation, provider default parsing, metadata transformer, schema migration snapshot, schema diff script, and schema comparer tests now build their metadata fixtures through typed drafts and `MetadataDefinitionFactory`, trimming the remaining CS0618 test island to mutation/corruption-focused coverage.
- `MetadataDefinitionFactory` relation, freeze, provider-style, and stub positive-path tests now use typed drafts directly; the remaining factory-test compatibility suppression is reserved for obsolete-mutator reflection coverage and deliberately malformed mutable graphs.
- Cache-policy and provider-scoped attribute validation tests in `MetadataDefinitionFactoryTests` now build malformed inputs through typed drafts instead of post-construction runtime metadata mutation.
- C# symbol metadata validation tests in `MetadataDefinitionFactoryTests` now build malformed database/model/property/interface inputs through typed drafts, keeping that coverage on the builder boundary instead of mutating runtime definitions post-construction.
- Default, view, column database-type, and enum validation tests in `MetadataDefinitionFactoryTests` now build malformed inputs through typed drafts. Typed draft lowering also preserves null database type entries long enough for factory preflight to return `InvalidModel` instead of leaking a null-reference exception.
- Name, null-attribute, index-attribute, foreign-key attribute, and relation-attribute validation tests in `MetadataDefinitionFactoryTests` now build malformed inputs through typed drafts, further narrowing the mutable test surface to ownership/key corruption and half-wired relation/index graphs.
- Duplicate table-property, duplicate column, and missing-primary-key validation tests in `MetadataDefinitionFactoryTests` now use typed drafts, leaving the comparable mutable tests for duplicate object references and asymmetric table/column registration.
- `MetadataFactory` parser-helper tests no longer carry a file-wide obsolete-mutator suppression; legacy setup calls are routed through a locally suppressed compatibility helper block so new direct mutator use in those tests is visible.
- `MetadataDefinitionFactoryTests` no longer carries a file-wide obsolete-mutator suppression; remaining frozen-mutation checks and deliberately corrupted mutable graph fixtures route through a locally suppressed compatibility helper block.
- `DatabaseColumnType` setter-style provider-type mutators are now obsolete compatibility API as well. SQLite/MySQL metadata import computes final database-type values before construction instead of mutating provider type metadata after construction.
- Factory freeze coverage now matches the current obsolete setter-style mutator surface across database, table-model, table/view, model, property, column, index, relation, and provider type metadata. Reflection coverage proves the compatibility API is obsolete, and runtime freeze assertions prove those entry points cannot reopen finalized snapshots.
- Typed-draft lowering now preflights null table-model, value-property, relation-property, column, and attribute source-span entries before building the temporary mutable graph, keeping malformed typed drafts on the `InvalidModel` option path instead of leaking null-reference or argument exceptions.
- `MetadataList<T>` and `MetadataDictionary<TKey,TValue>` public write APIs are now obsolete compatibility surface. SharedCore construction, snapshotting, typed-draft lowering, and transformation code use internal construction-only collection methods, so new product-side structural metadata mutation is visible as a compatibility warning.
- Mutable factory inputs are now obsolete compatibility API as well. `MetadataDefinitionFactory` still accepts mutable `DatabaseDefinition` graphs and `MetadataDefinitionDraft.FromMutableMetadata(...)` for legacy/corruption coverage, but normal construction code and tests are steered toward typed drafts.
- Typed drafts now produce fresh factory-owned construction graphs directly, while mutable compatibility inputs still go through the defensive snapshot-copy path before finalization. This narrows snapshot copying to the legacy graph boundary instead of treating typed drafts as borrowed mutable metadata.
- `MetadataFactory` graph-finalization helpers that mutate runtime metadata (`ParseInterfaces`, `NormalizeDatabaseTypeName`, `ParseIndices`, `ParseRelations`, and `IndexColumns`) are now obsolete compatibility wrappers. `MetadataDefinitionFactory` calls internal core helpers directly, keeping ordinary finalization centralized while legacy parser-helper tests route deliberate calls through local suppressions.
- The remaining public `MetadataFactory` construction/parser helpers that mutate graph pieces (`ParseTable`, `ParseColumn`, `ParseAttributes`, `AttachValueProperty`, and `TryAttachValueProperty`) are now obsolete compatibility wrappers as well. Product-side construction uses internal core helpers, and legacy parser tests route intentional direct coverage through local suppressions.
- The relation finalization helper `MetadataFactory.AddRelationProperty(...)` is also now an obsolete compatibility wrapper. Relation resolution calls the internal core helper directly, so generated relation properties are still centralized in factory finalization without exposing another public mutation path.
- Public `SyntaxParser` methods that emit mutable runtime metadata (`ParseTableModel` and `ParseProperty`) are now obsolete compatibility wrappers. Internal source parsing uses core helpers or typed-draft outputs, and syntax parser tests keep legacy mutable output coverage behind local suppressions.
- The public `TableModel` constructor that parses table metadata from a mutable `ModelDefinition` is now obsolete compatibility API. `SyntaxParser` uses an internal construction helper for its remaining mutable output path, and reflection coverage pins this constructor with the rest of the mutable factory-input surface.
- The public `TableModel` shortcut constructor that creates a new mutable model from an existing table plus a C# name is now obsolete compatibility API as well. Remaining tests that intentionally use that hand-built graph path route through a local suppression helper, while the explicit model/table graph constructor remains available for legacy fixture construction.
- Generated runtime metadata bootstrap failures now return `InvalidModel` option failures for missing hooks, wrong hook return types, default declarations, and malformed table declarations instead of surfacing as arbitrary catch-all exceptions.
- Provider metadata import now returns `InvalidModel` option failures for unsupported SQLite, MySQL, and MariaDB column types with table/column context, while still skipping intentionally unsupported generated columns.
- MySQL and MariaDB provider metadata import now returns `InvalidModel` option failures for unsupported index types and malformed index rows, keeping provider index parsing in the expected-failure path instead of throwing from information_schema classification.
- MySQL and MariaDB foreign-key metadata import now validates required relation fields before attaching attributes, so malformed relation rows return `InvalidModel` option failures while excluded or unimported related tables continue to be skipped deliberately.
- SQLite, MySQL, and MariaDB metadata import now attach provider-created value properties through an option-returning factory path, so an unresolved provider C# type mapping returns `InvalidModel` with table/column context instead of throwing from `AttachValueProperty`.
- SQLite foreign-key metadata import now handles shorthand references to a parent table by resolving the single imported primary-key column, and returns `InvalidModel` when the shorthand is ambiguous instead of failing through a null pragma column read.
- Column index construction now enforces the empty-column invariant before reading the first column, preserving the intended "An index should have at least one column." diagnostic for factory callers instead of leaking a sequence error.
- Metadata collection integrity is now preflighted before draft snapshot copying and during finalization, so null database/model/property attributes, null value/relation property dictionary entries, and malformed value/relation property type tags return `InvalidModel` before C# symbol validation, attribute validation, or snapshot copying can fail late.
- Existing index and relation enum metadata is now preflighted before draft snapshot copying and during finalization, so unsupported `ColumnIndex` characteristic/type values, duplicate column-index names, null relation-part collections, and unsupported relation/referential action values return `InvalidModel` before snapshot copying, relation matching, or schema comparison can fail late.
- Table type metadata is now preflighted before draft snapshot copying and during finalization, so unsupported `TableType` values and mismatches between `TableType` and `TableDefinition`/`ViewDefinition` shape return `InvalidModel` before relation finalization, snapshot copying, or generated source emission can silently reclassify an object.
- The draft snapshot boundary now preflights existing column-index ownership before copying, so an index attached to the wrong table returns `InvalidModel` instead of leaking snapshot exceptions or preserving an inconsistent graph.
- Existing relation parts are now preflighted before draft snapshot copying and rechecked after relation finalization, so half-wired relation definitions or missing index back-references return `InvalidModel` instead of leaking snapshot/null-reference behavior.
- Existing table column and value-property bindings are now preflighted before draft snapshot copying and before duplicate-column/index parsing, so missing or asymmetric column/property links return `InvalidModel` instead of leaking null-reference behavior.
- Existing table-model ownership and primary-key registration are now preflighted before draft snapshot copying and checked again during finalization, so null table-model owners, cross-database table-model reuse, and asymmetric primary-key lists return `InvalidModel` instead of leaking snapshot map failures.
- Table-model database property names are now finalized centrally: duplicate property names return `InvalidModel`, and database type/property-name collisions are normalized on the built snapshot instead of only in the source parser.
- Existing relation-property bindings are now preflighted before draft snapshot copying and rechecked after relation finalization, so wrong dictionary keys, wrong owning models, empty stored relation names, value/relation property name collisions, and relation properties pointing at another table's relation part return `InvalidModel` instead of being silently normalized by snapshot copying.
- Value-property defaults are now validated before draft snapshot copying, so duplicate defaults, dynamic defaults on incompatible C# types, unsupported UUID versions, and ordinary defaults that cannot be formatted for the property type return `InvalidModel` instead of failing later during generated code or SQL emission.
- Missing view SQL definitions are now validated before draft snapshot copying, so view models without a definition return `InvalidModel` from the factory instead of failing later during SQL or model generation. Explicit empty definitions remain allowed for generated provider/system-view placeholders until those models get a richer external-view marker.
- Empty database, table/view, and column names plus duplicate table-property names, table names, and column names are now preflighted before draft snapshot copying, so invalid object registrations return the same `InvalidModel` diagnostics as ordinary duplicate metadata instead of leaking into snapshots or generated attributes.
- Existing column database-type metadata is now preflighted before draft snapshot copying and during finalization, so null type entries, empty type names, duplicate provider type entries, decimals without length metadata, and undefined or `Unknown` provider enum values return `InvalidModel` instead of leaking snapshot or SQL-generation failures.
- Enum value-property metadata is now preflighted before draft snapshot copying and during finalization, so missing enum metadata, empty enum value sets, invalid generated C# member names, and ambiguous database enum values return `InvalidModel` before model generation or data-reader enum mapping can fail.
- Database and table cache-policy metadata is now preflighted before draft snapshot copying and during finalization, so unsupported cache/index-cache enum values, duplicate cache limit or cleanup entries, conflicting index-cache policies, nonpositive cache amounts, missing `MaxAmountRows` index-cache amounts, and extraneous index-cache amounts return `InvalidModel` before runtime cache policy evaluation can become ambiguous or fail.
- Provider-scoped attribute metadata is now preflighted before draft snapshot copying and during finalization, so `Comment`, `Check`, and `DefaultSql` attributes with unsupported or `Unknown` database enum values return `InvalidModel` before generated attribute emission or schema comparison can fail.
- Schema annotation metadata is now preflighted before draft snapshot copying and during finalization, so `Comment` attributes with null text plus `Check` attributes with empty names, empty expressions, or duplicate provider-scoped names return `InvalidModel` before generated model output or schema comparison can fail late.
- Relational attribute metadata is now preflighted before draft snapshot copying and during finalization, so malformed `Index`, `ForeignKey`, and `Relation` attribute shapes such as unsupported enum values or empty index/relation targets return `InvalidModel` before index and relation parsing can fail late.
- C# symbol metadata used by generated code is now preflighted before draft snapshot copying and rechecked after relation generation, so unsupported or role-incompatible C# type-kind values plus invalid database/model type names, table property names, model using namespaces, declared model interface references, generated model type references, value/relation property C# type references, value property names, and generated relation property names return `InvalidModel` before generated source can become malformed. Declared model interface entries are now also required to use interface-shaped C# metadata instead of any valid type syntax, and the source parser tags base-list model interfaces accordingly.
- Model-file generation now formats metadata string arguments through Roslyn string literals, so database names, table/view names, definitions, index and column names, database type names, enum names, and relation attribute values containing quotes or escapes produce valid generated attributes.
- This slice deliberately does not claim fully immutable runtime definitions yet. The current graph is frozen against ordinary mutators and the main structural collection mutators after factory finalization, array-shaped properties and generated declaration arrays no longer expose live storage, cache-policy lists are sealed after freeze, the raw global metadata registry is obsolete, public definition mutators are obsolete, SharedCore construction uses internal-only mutators, and first generation/cache/provider-default/transformer/schema test fixtures use typed drafts; typed drafts still lower through the mutable graph internally.

Design stance:

- Use internal two-phase construction if it is the fastest way to handle cyclic metadata references without a huge public API rewrite.
- Prefer `ImmutableArray<T>` for ordered metadata and read-only or frozen dictionaries for lookups.
- Do not rely on record equality for the graph. Cycles and source-span-insensitive equality need deliberate metadata comparison or digest logic.

Exit criteria:

- runtime metadata returned by the factory cannot be mutated by ordinary consumers
- provider/database startup no longer mutates metadata after build
- metadata equivalence tests are green across source, generated, and provider-derived metadata
- cache policy tests prove defaults are not injected by mutating `DatabaseDefinition`

## Workstream D: Compatibility Size Reports And Banned-Payload Gates

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
dotnet run --project src\DataLinq.Dev.CLI -- size-report --target phase8b
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

## Workstream E: Split Runtime-Safe Metadata From Roslyn And Generator Code

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

## Workstream F: Complete Generated Metadata Startup

Goals:

- stop generated-model startup from rediscovering metadata the generator already knew
- use the immutable metadata builder/factory path from Workstream C
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

## Workstream G: Generated Indexed Access And Metadata Handles

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

## Workstream H: DataLinq Query Plan Behind Remotion

Goals:

- introduce a DataLinq-owned query plan without changing parser behavior all at once
- move SQL generation and diagnostics away from Remotion clause types
- create the migration target for a supported-subset expression parser

Tasks:

1. Treat [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md) as the parity contract.
2. Add missing tests for high-risk support-matrix shapes before changing the query boundary.
3. Define immutable plan nodes for source slots, predicates, orderings, paging, projections, joins, local sequences, and result operators.
4. Add a `RemotionQueryPlanAdapter` that converts existing `QueryModel` output to the DataLinq plan.
5. Move `QueryExecutor` and SQL generation to consume the DataLinq plan while Remotion remains the producer.
6. Replace `SqlQuery<T>.Where(WhereClause)` and `OrderBy(OrderByClause)` as the main translation boundary.
7. Preserve fixed true/false condition behavior, local sequence semantics, nullable comparison semantics, scalar aggregate behavior, join behavior, and relation `EXISTS` behavior.
8. Add plan snapshot tests for representative queries.

Design stance:

- The plan should represent query intent, not SQL text.
- Source slots should be explicit so joins and relation subqueries do not rely on visitor-global assumptions.
- Captured values should be separated from query shape so plan caching and parameter rebinding remain possible later.

Exit criteria:

- Remotion still parses queries, but SQL generation consumes DataLinq plan nodes
- supported single-source queries generate equivalent SQL/results
- plan tests cover local collections, nullable predicates, projections, scalar aggregates, joins, and relation predicates
- unsupported query diagnostics remain specific and DataLinq-owned

## Workstream I: Supported-Subset Expression Parser And AOT Boundary Switch

Goals:

- remove or isolate Remotion from the generated/AOT support path
- keep the parser scoped to the documented support matrix
- avoid silent client-side fallback for unsupported query shapes

Tasks:

1. Build a DataLinq expression parser over `System.Linq.Expressions` that emits the same DataLinq query plan.
2. Support the documented first slice:
   - `Where`
   - `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`
   - `Select`
   - `Skip`
   - `Take`
   - `Any`
   - `Count`
   - `Single`, `SingleOrDefault`
   - `First`, `FirstOrDefault`
   - `Last`, `LastOrDefault`
   - documented scalar aggregates
   - the narrow explicit `Join(...)` baseline if parity is practical in this phase
3. Rebuild local sequence handling against expression trees and plan bindings.
4. Add a projection interpreter or generated projection strategy for supported row-local projection shapes.
5. Inventory and remove reflection invocation from supported generated/AOT projection execution.
6. Add dual-run parity tests that parse with Remotion and with the DataLinq parser, then compare normalized plans, SQL templates, and results.
7. Route generated SQLite AOT, trimmed, and WASM AOT smoke projects through the DataLinq parser.
8. Remove `Remotion.Linq` roots from AOT and trim smoke projects.
9. Decide whether Remotion is deleted from the main runtime package or moved to a clearly named compatibility package.

Exit criteria:

- generated SQLite AOT smoke publishes without `Remotion.Linq` warnings
- trim smoke publishes without `Remotion.Linq` warnings
- WASM AOT browser smoke still passes
- the documented support matrix passes on the DataLinq parser for the enabled subset
- unsupported query shapes fail with `QueryTranslationException` or equivalent specific diagnostics
- main runtime package has no `Remotion.Linq` dependency, or Remotion is isolated outside the practical AOT support boundary

## Workstream J: SQLitePCLRaw WebAssembly Warning Disposition

Goals:

- understand whether `WASM0001` warnings are reachable in the supported browser path
- avoid library-level warning suppression without evidence
- keep no-AOT browser WebAssembly explicitly unsupported until it runs

Tasks:

1. Identify the managed SQLitePCLRaw methods that import `sqlite3_config` and `sqlite3_db_config`.
2. Determine whether `Microsoft.Data.Sqlite`, `SQLitePCLRaw.bundle_e_sqlite3`, or the selected provider calls those imports during:
   - provider registration
   - connection open
   - foreign-key configuration
   - schema creation
   - insert/query
   - relation loading
   - OPFS/file-backed configuration
3. Extend the WASM AOT smoke to cover provider registration, connection open, schema creation, foreign keys, insert, query, projection, and relation loading.
4. If the warning symbols are unreachable for the supported path, document the exact proof and keep any suppression local to the smoke/sample project with a comment.
5. If the symbols are reachable for realistic configuration, investigate a WebAssembly-safe provider or initialization path before claiming support.
6. Keep OPFS/file-backed browser storage as a separate experiment with separate warnings and behavior notes.
7. Keep no-AOT WebAssembly unsupported until the Mono interpreter failures are gone.

Exit criteria:

- SQLitePCLRaw warning disposition is documented with exact methods and call paths
- WASM AOT browser smoke still passes after the investigation
- any suppression is local, justified, and tied to call-path evidence
- no-AOT WebAssembly remains documented as unsupported unless it actually runs

## Workstream K: Packaging And Public Compatibility Wording

Goals:

- keep package assets honest
- prevent roadmap claims from leaking into product docs early
- define the first support statement DataLinq can defend

Tasks:

1. Inspect packed NuGet output and dependency groups after the Roslyn split.
2. Verify analyzers live under analyzer assets and do not pull runtime dependencies into `lib/net*`.
3. Publish PDB/symbol output separately for Native AOT release artifacts when documenting sizes.
4. Keep smoke/sample projects out of shipped packages.
5. Update platform compatibility docs only after the gates are clean.
6. Keep compatibility wording narrow:

> DataLinq supports generated SQLite models under Native AOT and Blazor WebAssembly AOT for the documented query subset.

7. Do not expand that wording to reflection-discovered models, arbitrary client projections, MySQL/MariaDB browser support, OPFS storage, or no-AOT browser WebAssembly.

Exit criteria:

- package inspection confirms runtime dependency groups are clean
- release notes can state realistic constrained-platform sizes without hiding symbol or browser payload caveats
- public docs and support matrices match the implementation evidence
- roadmap documents remain separate from shipped behavior docs

## Recommended Order

1. Remove stale generated-hook compatibility.
2. Tighten generated declaration validation.
3. Add compatibility size reports and banned-payload checks.
4. Introduce builder-built immutable metadata definitions and equivalence tests.
5. Split runtime-safe metadata from Roslyn/generator code.
6. Remove Roslyn from the runtime package graph and verify size/report improvement.
7. Generate complete metadata against the immutable metadata factory.
8. Switch generated-model startup to generated complete metadata.
9. Generate indexed value access, relation handles, and mutable metadata handles.
10. Lock down query support-matrix parity gaps.
11. Introduce `DataLinqQueryPlan` with a Remotion adapter.
12. Move SQL generation behind the DataLinq plan.
13. Build the supported-subset expression parser.
14. Add dual-run parser parity tests.
15. Move generated/AOT smoke projects to the DataLinq parser.
16. Remove or isolate Remotion from the generated/AOT support boundary.
17. Investigate SQLitePCLRaw WebAssembly warnings and document or eliminate them.
18. Promote narrow public compatibility wording only after reports, smokes, and package inspection are clean.

This order is deliberately not the shortest-looking path. Removing Remotion before there is a plan boundary is a rewrite. Generating complete metadata before immutable definitions is building on the wrong graph. Suppressing SQLitePCLRaw warnings before call-path analysis is pretending. None of those are good trades.

## Verification Plan

Routine verification after small generated/metadata slices:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- build src\DataLinq.sln --profile ci --output errors
dotnet run --project src\DataLinq.Testing.CLI -- run --suite generators --alias quick --output failures
dotnet run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures
```

Routine verification after query work:

```powershell
dotnet run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
dotnet run --project src\DataLinq.Testing.CLI -- run --suite mysql --alias latest --output failures
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
- SQLite compliance quick suite
- MySQL/MariaDB provider lanes when metadata or SQL generation changed
- Native AOT publish and executable run
- trimmed publish and executable run
- Blazor WebAssembly AOT publish and browser smoke
- compatibility size report with banned-payload checks
- package inspection for runtime dependency groups
- docs build if public docs or navigation changed

Environment caveat:

Blazor WebAssembly builds are known to be unreliable inside the Codex sandbox on native Windows because the WebAssembly/MSBuild task host can fail there. Verify outside the sandbox before treating `DataLinq.BlazorWasm` build failures as product bugs.

## Risk Register

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Generated-hook cleanup breaks stale consumer generated output | Medium | Make the failure explicit, document regeneration requirement, and keep the change tied to the generated/AOT support boundary. |
| Immutable metadata becomes shallow read-only theater | High | Test collection immutability, remove post-build mutation paths, and keep builders separate from definitions. |
| Relation metadata equivalence regresses | High | Add focused equivalence tests for relation parts, candidate keys, generated relation names, and provider-derived foreign keys. |
| Roslyn split breaks tooling/generator reuse | Medium | Move Roslyn code to generator/tooling adapters and keep runtime-safe DTOs small and explicit. |
| Size reporting becomes noisy or environment-specific | Medium | Separate hard banned-file checks from advisory size thresholds; include workload/environment metadata in reports. |
| Query plan becomes SQL-shaped by accident | Medium | Keep plan nodes backend-neutral and require SQL translation to be one consumer of the plan, not the plan itself. |
| Parser rewrite regresses supported LINQ behavior | High | Use the support matrix, plan snapshots, dual-run parity, and provider compliance tests before flipping defaults. |
| Projection execution reintroduces reflection invocation debt | High | Inventory reflection invocation and keep unsupported projection shapes rejected in generated/AOT mode. |
| Remotion compatibility path becomes permanent | Medium | Define removal/isolation exit criteria and keep it outside the practical AOT support statement. |
| SQLitePCLRaw warning suppression hides a real browser failure | High | Suppress only after managed/native call-path proof and keep suppression local. |
| Public docs overclaim | High | Promote only narrow compatibility wording after package, smoke, and report evidence are clean. |

## Exit Criteria

Phase 8B is complete when:

- generated output no longer emits or relies on `GetDataLinqGeneratedTableModels()`
- missing generated hooks and malformed generated declarations fail during initialization with clear diagnostics
- runtime metadata definitions are immutable snapshots built through a dedicated builder/factory path
- generated model startup no longer rediscovers ordinary metadata through runtime reflection
- generated value, relation, and mutable access paths avoid avoidable name/global metadata lookup
- compatibility size reports can be refreshed by tooling
- trimmed and WebAssembly outputs contain no Roslyn runtime payload
- `DataLinq.dll` runtime dependency groups no longer include `Microsoft.CodeAnalysis.*`
- generated SQLite Native AOT and trimmed publishes run without DataLinq-owned AOT/trim warnings
- `Remotion.Linq` is removed from, or isolated outside, the supported generated/AOT query path
- WASM AOT browser smoke still passes
- SQLitePCLRaw WebAssembly warning disposition is documented with call-path evidence
- public docs can state a narrow generated SQLite AOT/WASM AOT support boundary without caveat gymnastics

Until then, the accurate public statement remains:

> DataLinq has a proven generated SQLite Native AOT, trimmed publish, and Blazor WebAssembly AOT smoke boundary.

Not:

> DataLinq is broadly AOT-compatible.

The second sentence is earned only when the package graph, generated metadata contract, query dependency boundary, and warning story stop arguing with it.
