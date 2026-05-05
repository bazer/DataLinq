> [!WARNING]
> This document is roadmap and engineering planning material. It is not normative product documentation and should not be treated as shipped behavior.
# Immutable Metadata Definitions and Factory Plan

**Status:** Draft plan.

**Created:** 2026-05-05.

## Purpose

DataLinq's runtime metadata graph is currently mutable. That was useful while the model was still being discovered from Roslyn, generated declarations, provider schemas, and merge transforms, but it is now the wrong default. Metadata describes the shape of the database and generated model contract. Once built, it should be a stable snapshot.

The goal is to move mutation into a separate factory/builder layer and make the runtime definitions deeply immutable.

The blunt version:

> Runtime metadata should be data, not a half-built object graph that random construction code keeps poking until it works.

## Roadmap Placement

This work belongs inside the Phase 8B practical AOT and package-graph hardening lane, before the full generated metadata switch.

It should not block the small fail-fast generated-hook cleanup in [Generated Metadata Contract and Runtime Fallback Removal](Generated%20Metadata%20Contract%20and%20Runtime%20Fallback%20Removal.md). Workstreams 1 and 2 from that plan can land first:

1. remove stale generated hook compatibility
2. tighten generated declaration validation

But this immutable metadata work should happen before Workstream 3 from that plan, "Generate Complete Runtime Metadata".

Generating complete metadata into the current mutable graph would be backwards. It would create generated output targeting a shape we already know we need to replace, then force a second migration later. The better sequence is:

1. make the generated contract strict enough to fail clearly
2. introduce builder-built immutable metadata snapshots
3. prove current reflection/provider metadata and new snapshots are equivalent
4. generate complete metadata declarations against the immutable snapshot factory
5. switch ordinary generated-model startup away from runtime rediscovery

## Current State Inventory

The current metadata root is `DatabaseDefinition`, not a distinct `MetadataDefinitions` type.

Important mutable surfaces:

- `DatabaseDefinition` exposes setters, mutable `TableModels`, and live cache-policy lists.
- `ModelDefinition` exposes setters and live value/relation property dictionaries.
- `TableDefinition` exposes mutable columns, primary keys, indices, cache settings, and `UseCache`.
- `ColumnDefinition` exposes mutable names, type mappings, ordinals, key flags, nullability, and value-property links.
- `PropertyDefinition`, `ValueProperty`, and `RelationProperty` expose mutable attributes, names, C# type info, source info, column links, and relation links.
- `ColumnIndex` exposes a mutable `Table`, live `Columns`, and live `RelationParts`.
- `RelationDefinition` has settable sides, actions, type, and constraint name.
- `TableModel` wires circular references by mutating `TableDefinition` and `ModelDefinition` during construction.

Construction and mutation are also spread across too many components:

- `MetadataFactory` parses attributes, creates tables/columns, adds indices, synthesizes primary key indices, resolves relations, creates relation properties, and assigns column ordinals.
- `MetadataFromTypeFactory` still reflects over generated model types and builds mutable metadata at runtime.
- SQLite, MySQL, and MariaDB metadata factories create partial graphs, then mutate columns and properties with discovered index/foreign-key attributes before common parsing.
- `MetadataTransformer` mutates database-derived metadata in place when merging source-model names, types, defaults, enum values, and relation names.
- `DatabaseProvider` mutates loaded metadata by appending default cache policy when cache metadata is empty.
- Some tests mutate metadata cache limits directly to force cache behavior.

That is not just "mutable during construction". It is a public mutable graph with several post-construction mutation paths.

## Target State

Runtime metadata definitions should have these properties:

- no public setters or add/remove methods
- no exposed mutable `List<>`, `Dictionary<>`, or array that callers can mutate
- stable collection order where order has meaning, especially table order and column ordinal order
- object references resolved before the graph is returned
- column ordinals assigned before the graph is returned
- primary key, unique, foreign key, and relation structures complete before the graph is returned
- source locations retained for diagnostics but ignored by structural equality
- runtime cache policy represented as effective runtime configuration, not metadata mutation
- compatibility reflection paths explicit and quarantined

Good final collection choices:

- `ImmutableArray<T>` for ordered lists such as table models, columns, primary key columns, index columns, attributes, and relation parts
- `IReadOnlyDictionary<string, T>` or `FrozenDictionary<string, T>` for name lookup tables
- immutable value objects for `DatabaseColumnType`, enum metadata, source locations, and generated table declarations

Do not default to C# record equality for the graph. The metadata graph is cyclic and needs equality that ignores source spans. Manual equality or a dedicated metadata digest is safer.

## Builder and Factory Design

Introduce transient builder or draft types. Names are negotiable, but the separation is not:

- `DatabaseMetadataBuilder`
- `TableMetadataBuilder`
- `ModelMetadataBuilder`
- `ColumnMetadataBuilder`
- `ValuePropertyMetadataBuilder`
- `RelationPropertyMetadataBuilder`
- `IndexMetadataBuilder`
- `ForeignKeyMetadataBuilder`
- `MetadataBuildOptions`
- `MetadataDefinitionFactory`

Builders can be mutable. They can hold unresolved string references, source spans, diagnostics context, provider-specific raw data, and merge-time temporary state.

Definitions should be immutable. They should be created only by `MetadataDefinitionFactory` or by internal factory helpers.

The factory should own:

- attribute interpretation
- database/table/column C# name normalization
- table and column uniqueness validation
- provider include filtering validation
- column type normalization
- primary key and auto-increment interpretation
- index construction and validation
- foreign key grouping and ordinal ordering
- candidate key resolution
- relation definition and relation property construction
- source-location-aware failure creation
- column ordinal assignment
- generated immutable/mutable type and factory validation
- final freeze into immutable definitions

The factory should return `Option<DatabaseDefinition, IDLOptionFailure>` rather than throwing for expected invalid-model failures.

## Handling Cycles

The hard part is not read-only properties. The hard part is cycles:

- database owns table models
- table model points at database, table, and model
- table points at table model
- model points at table model
- column points at table
- value property points at model and column
- column index points at table and columns
- relation part points at column index and relation
- relation points at both relation parts

There are two acceptable implementation strategies:

1. Use internal two-phase construction inside the factory, then expose only immutable definitions.
2. Reduce cycles by replacing some back-references with root/context lookups or stable handles.

The pragmatic first slice should use internal two-phase construction. It minimizes runtime API churn while removing public mutability. Later generated handles can reduce lookup cost once the immutable contract is proven.

## Migration Plan

### Step 1: Add Builders Side By Side

Add builder types without deleting existing setters. Keep the current metadata graph working.

Exit criteria:

- builders can represent source-model metadata, generated table declarations, SQLite metadata, MySQL metadata, and MariaDB metadata
- builders retain source spans needed by current diagnostics
- no runtime path is switched yet

### Step 2: Add Factory and Equivalence Tests

Implement `MetadataDefinitionFactory.Build(...)` and compare its output to current metadata for representative models/providers.

Exit criteria:

- unit tests compare old and new metadata for generated `EmployeesDb`
- unit tests cover manual source-model parsing
- SQLite in-memory metadata roundtrip still matches current output
- MySQL/MariaDB server metadata tests can compare the supported metadata subset

### Step 3: Move Roslyn and Generated Declaration Paths

Switch `SyntaxParser`, `MetadataFromModelsFactory`, and generated declaration parsing to fill builders and call the factory.

Exit criteria:

- generator quick suite passes
- current source diagnostics remain as precise or better
- generated table declarations still enforce immutable factory hooks

### Step 4: Move Provider Introspection Paths

Switch SQLite, MySQL, and MariaDB metadata factories to emit builders directly.

Provider readers should stop mutating `ValueProperty.Attributes` as a transport mechanism for discovered indices and foreign keys. Provider-discovered facts should be first-class builder data.

Exit criteria:

- provider metadata roundtrip tests pass
- unsupported provider constructs still warn at the same boundary
- referential actions, column ordering, checks, comments, defaults, and view metadata remain preserved where currently supported

### Step 5: Replace MetadataTransformer

Replace in-place metadata transformation with a builder/snapshot merge that returns a new result.

Exit criteria:

- source names/types/defaults/enums can be merged into database-derived metadata without mutating an existing `DatabaseDefinition`
- relation-name merge tests pass
- no code path needs `ValueProperties.Remove(...)`, `RelationProperties.Clear()`, or equivalent runtime graph surgery

### Step 6: Move Runtime Cache Defaults Out of Metadata

`DatabaseProvider` should compute effective cache policy rather than appending defaults into `DatabaseDefinition`.

Tests that currently mutate metadata cache-limit lists should switch to provider/cache options or a test-only runtime policy override.

Exit criteria:

- metadata is not modified during provider initialization
- cache behavior tests still have a clean way to force small cache limits
- effective policy is observable enough for diagnostics

### Step 7: Seal the Definition API

Remove or obsolete public mutators, replace mutable collections, and make constructors internal where practical.

Exit criteria:

- ordinary consumers cannot mutate runtime metadata after build
- tests assert returned collection mutation is impossible
- remaining internal mutation is confined to the factory/freeze path

### Step 8: Generate Complete Metadata Against the Factory

After immutable snapshots exist, resume Workstream 3 from the generated metadata contract plan.

The generator can emit either:

- complete builder declarations consumed by `MetadataDefinitionFactory`
- a compact generated metadata declaration model consumed by the same factory
- direct calls into factory helpers if the generated output stays readable

Do not emit one giant unreadable object graph unless benchmarks prove it is worth the cost. Readable generated declarations plus a runtime-safe factory are the better default.

## Test Plan

Required tests:

- builder validation tests for duplicate databases, duplicate tables, duplicate columns, bad indices, missing primary keys, unresolved foreign keys, and unresolved relation properties
- structural equality tests proving source-span-only changes do not change logical metadata equality
- generated metadata equivalence tests for `EmployeesDb`, `AllroundBenchmark`, and platform smoke models
- provider metadata equivalence tests for SQLite, MySQL, and MariaDB
- transformer replacement tests for source/database metadata merge behavior
- cache policy tests proving provider defaults do not mutate metadata
- immutability tests proving runtime collections cannot be modified after build
- AOT/trim smoke tests after generated metadata startup switches to generated declarations

Useful verification commands:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- build src\DataLinq.sln --profile ci --output errors
dotnet run --project src\DataLinq.Testing.CLI -- run --suite generators --alias quick --output failures
dotnet run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures
dotnet run --project src\DataLinq.Testing.CLI -- run --suite mysql --alias latest --output failures
```

Use the repo's sandbox wrapper for broad local verification on Windows.

## Risks

- A shallow immutability pass would be worse than doing nothing. It would create confidence while live mutable collections and post-build mutation still exist.
- Relation construction is the highest-risk area because it relies on grouping, candidate key matching, generated relation property names, and bidirectional graph links.
- Tests currently use metadata mutability as a convenience. Those tests will need better fixture builders or runtime config hooks.
- Generated complete metadata can become hostile if emitted as raw object construction. Prefer generated declarations plus shared factory logic.
- Structural equality can recurse forever if implemented naively. Equality must deliberately avoid parent back-reference recursion.

## Done Means

This work is done when runtime `DatabaseDefinition` graphs are immutable snapshots built by one factory path, and all parser/provider/generator inputs feed that path through builders or declarations.

After that, generated metadata startup can be made strict and fast without targeting a dead-end mutable graph.
