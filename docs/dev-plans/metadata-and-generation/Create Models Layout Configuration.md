> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: Create Models Layout Configuration

**Status:** Draft implementation plan.
**Goal:** Let projects choose the generated C# model property layout used by `datalinq create-models`, with deterministic defaults stored in `datalinq.json` rather than transient command-line flags.

## Executive Position

This should be a config feature, not a CLI flag feature.

Generated model layout is project policy. If one developer runs `create-models` with a local ordering flag and another developer later runs the ordinary command, the generated files can churn even when the database schema did not change. That is exactly the kind of friction `datalinq.json` should prevent. The CLI should follow the config every time so regeneration is repeatable, reviewable, and visible in source control.

The right first version is deliberately small:

```json
{
  "Databases": [
    {
      "Name": "AppDb",
      "ModelLayout": {
        "PropertyOrder": "Column",
        "KeyPlacement": "Top",
        "RelationPlacement": "Bottom"
      }
    }
  ]
}
```

These defaults are opinionated:

- `PropertyOrder: "Column"` keeps generated models faithful to provider column order.
- `KeyPlacement: "Top"` keeps identity and key shape visible before ordinary data.
- `RelationPlacement: "Bottom"` keeps navigation properties out of the scalar column flow.

That is a good default for database-first generation. Users who primarily navigate C# by member name can opt into alphabetical scalar properties without turning layout into an arbitrary sorting DSL.

## Current Code Audit

### Config Shape

`src/DataLinq/Config/ConfigFile.cs` exposes database-level generation settings as flat nullable fields on `ConfigFileDatabase`, including:

- `SourceDirectories`
- `DestinationDirectory`
- `UseRecord`
- `UseFileScopedNamespaces`
- `UseNullableReferenceTypes`
- `CapitalizeNames`
- `RemoveInterfacePrefix`
- `SeparateTablesAndViews`

`src/DataLinq/Config/DataLinqConfig.cs` lowers those raw nullable config fields into non-null effective settings on `DataLinqDatabaseConfig`.

`datalinq.user.json` is merged over `datalinq.json` by database name. Existing scalar values are replaced when the override value is present.

Nested config is not used much today, but layout is a good reason to start. Three related top-level fields would work, but they would make the database object more of a junk drawer. A nested `ModelLayout` object is clearer and leaves room for future generated-model formatting settings.

### CLI Generation Flow

`src/DataLinq.CLI/Program.cs` handles `create-models`, resolves the selected database connection, creates `ModelGeneratorOptions`, and calls `ModelGenerator.CreateModels(...)`.

`src/DataLinq.Tools/ModelGenerator.cs` reads provider metadata, optionally reads and merges existing source model metadata, builds `ModelFileFactoryOptions`, and passes the final `DatabaseDefinition` to `src/DataLinq.SharedCore/Factories/Models/ModelFileFactory.cs`.

The layout settings should flow through that existing path:

1. `ConfigFileDatabase.ModelLayout`
2. `DataLinqDatabaseConfig.ModelLayout`
3. `ModelGeneratorOptions` or directly into `ModelFileFactoryOptions`
4. `ModelFileFactory`

### Current Model File Ordering

`ModelFileFactory.ModelFileContents(...)` currently orders value properties with:

```csharp
model.ValueProperties.Values
    .OrderBy(x => x.Column.Index)
    .ThenBy(x => x.Type)
    .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
    .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
    .ThenBy(x => x.PropertyName)
```

That means column index is the dominant sort key. The primary-key and foreign-key tie-breakers only matter when column indices collide, which should not normally happen. If generated files appear to have keys on top today, that is probably because the provider schema itself reports key columns early, not because the renderer is actively grouping them.

Relation properties are already emitted after value properties:

```csharp
model.RelationProperties.Values
    .OrderBy(x => x.Type)
    .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
    .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
    .ThenBy(x => x.PropertyName)
```

The new layout feature should make these rules explicit and testable.

### Source Generator Output

`src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs` has its own ordering rules for compiler-generated implementation files. It is not the primary target of this feature.

The source generator does not read `datalinq.json`, and it should not quietly depend on CLI-only config. This plan is about the abstract model files written by `datalinq create-models`. Source-generator output can keep its current deterministic ordering unless a separate generator-layout feature is designed later.

## Desired Config Contract

Add a nested optional object to each database entry:

```json
"ModelLayout": {
  "PropertyOrder": "Column",
  "KeyPlacement": "Top",
  "RelationPlacement": "Bottom"
}
```

All fields are optional. Missing fields use the defaults above.

Recommended raw config shape:

```csharp
public sealed record ConfigFileModelLayout
{
    public string? PropertyOrder { get; set; }
    public string? KeyPlacement { get; set; }
    public string? RelationPlacement { get; set; }
}
```

Use strings in the raw config model and parse them into internal enums. The current source-generated JSON context does not configure `JsonStringEnumConverter`, and accepting numeric enum values in user config would be a bad interface.

Recommended effective model:

```csharp
public sealed record DataLinqModelLayoutConfig
{
    public ModelPropertyOrder PropertyOrder { get; init; } = ModelPropertyOrder.Column;
    public ModelKeyPlacement KeyPlacement { get; init; } = ModelKeyPlacement.Top;
    public ModelRelationPlacement RelationPlacement { get; init; } = ModelRelationPlacement.Bottom;
}

public enum ModelPropertyOrder
{
    Column,
    Alphabetical
}

public enum ModelKeyPlacement
{
    Inline,
    Top
}

public enum ModelRelationPlacement
{
    Bottom,
    Top,
    WithForeignKey
}
```

Parsing should be case-insensitive and should reject unknown values with a clear config error. Silently falling back to defaults on typo would be hostile. `"Aplhabetical"` should fail loudly.

## Merge Semantics

Nested config needs deliberate merge behavior.

If `datalinq.json` contains:

```json
"ModelLayout": {
  "PropertyOrder": "Alphabetical",
  "KeyPlacement": "Top",
  "RelationPlacement": "Bottom"
}
```

and `datalinq.user.json` contains:

```json
"ModelLayout": {
  "RelationPlacement": "WithForeignKey"
}
```

the effective layout should be:

```json
"ModelLayout": {
  "PropertyOrder": "Alphabetical",
  "KeyPlacement": "Top",
  "RelationPlacement": "WithForeignKey"
}
```

Do not replace the whole nested object just because an override file supplies one field. Partial overrides should preserve the existing effective fields.

In practice, project-wide layout should live in committed `datalinq.json`. Local `datalinq.user.json` overrides should still work mechanically, but documentation should describe them as useful for experiments, not normal team workflow.

## Layout Semantics

### PropertyOrder

`Column` means scalar value properties are ordered by provider column index, then by generated C# property name as a deterministic tie-breaker.

`Alphabetical` means scalar value properties are ordered by generated C# property name using ordinal comparison, with provider column index as a deterministic tie-breaker.

The generated C# property name is the right alphabetical key. Users read C# files by C# member name, not raw database column name.

### KeyPlacement

`Inline` means key-bearing scalar properties stay in the normal `PropertyOrder` sequence. Their `[PrimaryKey]` and `[ForeignKey]` attributes remain attached to the generated properties, but there is no special grouping.

`Top` means key-bearing scalar properties are emitted before ordinary scalar properties:

1. primary-key columns
2. foreign-key-only columns
3. all remaining scalar columns

If a column is both primary key and foreign key, it belongs to the primary-key group.

Within key groups, prefer provider column order for composite key readability. Even though generated attributes preserve composite key ordinals, visual order should not obscure the shape of a composite primary key or composite foreign key. Users who want a fully alphabetical scalar property block can choose `KeyPlacement: "Inline"` together with `PropertyOrder: "Alphabetical"`.

### RelationPlacement

`Bottom` means relation properties are emitted after all scalar value properties. This is the default because it keeps the generated model's scalar schema easy to scan.

`Top` means relation properties are emitted before scalar value properties. This is useful for users who treat navigation as the primary domain surface, but it should not be the default for database-first output.

`WithForeignKey` means relation properties on the foreign-key side are emitted immediately after the local foreign-key scalar property or property group they belong to. Collection-side relations and other candidate-key-side relations still fall back to the bottom because they do not have a local foreign-key column to sit beside.

For composite foreign keys, emit the relation after the last participating local foreign-key column in the rendered scalar order. If multiple relations attach to the same local foreign-key group, order them by generated relation property name using ordinal comparison.

## Examples

### Default Layout

```json
"ModelLayout": {
  "PropertyOrder": "Column",
  "KeyPlacement": "Top",
  "RelationPlacement": "Bottom"
}
```

Generated shape:

```csharp
[PrimaryKey]
[Column("id")]
public abstract int Id { get; }

[ForeignKey("customers", "id", "fk_orders_customers")]
[Column("customer_id")]
public abstract int CustomerId { get; }

[Column("created_at")]
public abstract DateTime CreatedAt { get; }

[Column("order_number")]
public abstract string OrderNumber { get; }

[Relation("customers", "id")]
public abstract Customer Customer { get; }
```

### Alphabetical Scalar Layout

```json
"ModelLayout": {
  "PropertyOrder": "Alphabetical",
  "KeyPlacement": "Inline",
  "RelationPlacement": "Bottom"
}
```

Generated shape:

```csharp
[Column("created_at")]
public abstract DateTime CreatedAt { get; }

[ForeignKey("customers", "id", "fk_orders_customers")]
[Column("customer_id")]
public abstract int CustomerId { get; }

[PrimaryKey]
[Column("id")]
public abstract int Id { get; }

[Column("order_number")]
public abstract string OrderNumber { get; }

[Relation("customers", "id")]
public abstract Customer Customer { get; }
```

### Relation Beside Foreign Key

```json
"ModelLayout": {
  "PropertyOrder": "Column",
  "KeyPlacement": "Top",
  "RelationPlacement": "WithForeignKey"
}
```

Generated shape:

```csharp
[PrimaryKey]
[Column("id")]
public abstract int Id { get; }

[ForeignKey("customers", "id", "fk_orders_customers")]
[Column("customer_id")]
public abstract int CustomerId { get; }

[Relation("customers", "id")]
public abstract Customer Customer { get; }

[Column("created_at")]
public abstract DateTime CreatedAt { get; }

[Column("order_number")]
public abstract string OrderNumber { get; }
```

## Implementation Plan

### 1. Add Config Types

Add `ConfigFileModelLayout` to `src/DataLinq/Config/ConfigFile.cs` and a nullable property to `ConfigFileDatabase`:

```csharp
public ConfigFileModelLayout? ModelLayout { get; set; }
```

Add effective layout types and enums near the config layer or the model factory layer. The effective config should be non-null on `DataLinqDatabaseConfig`:

```csharp
public DataLinqModelLayoutConfig ModelLayout { get; private set; }
```

Default it to:

- `PropertyOrder = Column`
- `KeyPlacement = Top`
- `RelationPlacement = Bottom`

### 2. Add Case-Insensitive Parsing

Add parse helpers similar in spirit to `ConfigReader.ParseDatabaseType(...)`, but stricter:

```csharp
ConfigReader.ParseModelPropertyOrder(string? value)
ConfigReader.ParseModelKeyPlacement(string? value)
ConfigReader.ParseModelRelationPlacement(string? value)
```

Unknown non-empty values should produce a clear config failure. If the current config-loading path cannot return structured config failures cleanly, this slice should add the smallest safe path for reporting invalid layout values instead of throwing an unhandled exception from the CLI.

### 3. Implement Partial Nested Merge

Add merge logic so `DataLinqDatabaseConfig.MergeConfig(...)` applies only the nested fields present in the override object.

Recommended helper shape:

```csharp
ModelLayout = ModelLayout.Merge(database.ModelLayout);
```

where `Merge(...)` returns a new effective layout with only non-null raw fields replaced.

### 4. Add Factory Options

Extend `ModelFileFactoryOptions`:

```csharp
public DataLinqModelLayoutConfig ModelLayout { get; set; } = DataLinqModelLayoutConfig.Default;
```

If putting config-layer types into `DataLinq.SharedCore` creates an undesirable dependency direction, define neutral rendering enums in `DataLinq.SharedCore` and map from config enums to rendering enums in `ModelGenerator`.

The important thing is that `ModelFileFactory` receives already-validated enum values. The renderer should not parse strings.

### 5. Replace Ad Hoc Ordering in ModelFileFactory

Move layout ordering into small helper methods:

```csharp
private List<ValueProperty> OrderValueProperties(ModelDefinition model)
private List<RelationProperty> OrderRelationProperties(ModelDefinition model)
```

Then build the final emitted member sequence according to `RelationPlacement`.

This is worth isolating because relation placement is not just a sort key. `WithForeignKey` has to interleave relation members with scalar members.

Use `StringComparer.Ordinal` for generated C# member names. Culture-sensitive ordering would be wrong for generated source diffs.

### 6. Preserve Attribute and Enum Behavior

Do not change:

- property attributes
- relation attributes
- enum declaration behavior
- table-level attributes
- database file contents
- generated file headers
- nullable directives

Only change member ordering inside table/view model files.

### 7. Add Tests

Add focused TUnit tests in `src/DataLinq.Tests.Unit/Core/ModelFileFactoryTests.cs` and config tests in `src/DataLinq.Tests.Unit/Core/DataLinqConfigTests.cs`.

Config tests:

- omitted `ModelLayout` resolves to `Column`, `Top`, `Bottom`
- partial `ModelLayout` uses defaults for omitted fields
- `datalinq.user.json` style merge overrides one nested field without replacing the others
- invalid layout value returns a clear config failure

Rendering tests:

- default layout emits primary-key columns first, foreign-key-only columns next, ordinary columns afterward, and relations at the bottom
- `PropertyOrder: Alphabetical` plus `KeyPlacement: Inline` emits scalar properties by generated C# property name
- `RelationPlacement: WithForeignKey` emits foreign-key-side relation properties immediately after their local FK columns
- candidate-key-side collection relations remain at the bottom under `WithForeignKey`
- composite foreign-key relations are emitted after the last participating local FK column

Prefer ordering assertions over large exact-file baselines. Exact generated-file baselines are brittle and make unrelated formatting changes expensive.

### 8. Update Documentation After Implementation

After implementation, update:

- `docs/Configuration files.md`
- `docs/getting-started/Configuration and Model Generation.md` if it shows representative config
- `docs/CLI Documentation.md` only to clarify that `create-models` follows `ModelLayout` from config

Do not document command-line flags for this feature unless they are intentionally added later.

## Non-Goals

- Do not add `create-models` flags for property order, key placement, or relation placement in v1.
- Do not add arbitrary custom sort expressions.
- Do not add key placement at the bottom in v1. It is not a useful enough layout to justify the extra surface area.
- Do not make source-generator implementation file ordering depend on `datalinq.json` in this slice.
- Do not reformat generated method bodies, attributes, usings, namespaces, or file headers.
- Do not move relation attributes onto scalar properties or otherwise change metadata semantics.

## Compatibility and Migration Notes

The default layout should be treated as the intended model layout for regenerated files, but there is a compatibility wrinkle: current `ModelFileFactory` code sorts by column index before key status. If a database has ordinary columns before key columns, the new `KeyPlacement: "Top"` default can produce a one-time ordering diff.

That diff is defensible. Keys at the top are more useful, and the new behavior finally makes the intended policy explicit. Still, release notes should call out that regenerated model files may reorder members even when schema metadata is unchanged.

Projects that want the closest current behavior can set:

```json
"ModelLayout": {
  "PropertyOrder": "Column",
  "KeyPlacement": "Inline",
  "RelationPlacement": "Bottom"
}
```

## Acceptance Criteria

- `datalinq.json` accepts a nested `ModelLayout` object for each database.
- Omitted layout settings default to `PropertyOrder: Column`, `KeyPlacement: Top`, and `RelationPlacement: Bottom`.
- `datalinq.user.json` can partially override nested layout settings without replacing the whole layout object.
- Invalid layout values fail with a clear config error instead of silently falling back.
- `datalinq create-models` passes the effective layout to `ModelFileFactory`.
- `ModelFileFactory` emits value properties and relation properties according to the configured layout.
- No new CLI flags are required for layout in v1.
- Unit tests cover config defaults, nested merge behavior, and each supported layout mode.
