> [!WARNING]
> This document is roadmap/design material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.

# Generated Column Support

**Status:** Future design note.
**Goal:** Add first-class support for database-generated/computed columns without importing them as ordinary mutable value properties.

## Current State

Generated columns are intentionally unsupported today.

The provider support matrix says generated/computed columns are unsupported for MySQL, MariaDB, and SQLite. MySQL/MariaDB metadata import detects generated columns and skips them with a warning instead of treating them as mutable columns. That boundary is correct until DataLinq has an explicit read-only/generated column shape.

Relevant current files:

- `docs/support-matrices/Provider Metadata Support Matrix.md`
- `src/DataLinq.MySql/Shared/MetadataFromSqlFactory.cs`
- `src/DataLinq.SQLite/MetadataFromSQLiteFactory.cs`
- `src/DataLinq.SharedCore/Metadata/ColumnDefinition.cs`
- `src/DataLinq.SharedCore/Factories/MetadataTypedDrafts.cs`
- `src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs`
- `src/DataLinq/Mutation/StateChange.cs`
- `src/DataLinq.SharedCore/Validation/SchemaComparer.cs`

## Design Position

Generated columns should become **readable, queryable, schema-validatable columns** that are excluded from ordinary insert/update mutation.

They should not be represented as:

- `[DefaultSql]`
- ordinary writable `[Column]` properties
- views
- client-side C# computed properties

Generated columns are database-side expressions. Their values are produced by the database, may be virtual or stored, may participate in indexes depending on provider rules, and generally cannot be assigned normal values in `INSERT` or `UPDATE`.

The public API should probably use `ComputedColumn` rather than `GeneratedColumn`. DataLinq already uses "generated" heavily for source generation, generated files, generated metadata hooks, and generated provider-key accessors. `ComputedColumn` is clearer in model code while still mapping to generated-column metadata internally.

## Public API Shape

Initial attribute:

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class ComputedColumnAttribute : Attribute
{
    public ComputedColumnAttribute(string expression)
        : this(DatabaseType.Default, expression)
    {
    }

    public ComputedColumnAttribute(DatabaseType databaseType, string expression)
    {
        DatabaseType = databaseType;
        Expression = expression;
    }

    public DatabaseType DatabaseType { get; }
    public string Expression { get; }
    public ComputedColumnStorage Storage { get; init; } = ComputedColumnStorage.Virtual;
}

public enum ComputedColumnStorage
{
    Virtual,
    Stored
}
```

Example:

```csharp
[Column("full_name")]
[ComputedColumn("CONCAT(first_name, ' ', last_name)")]
public abstract string FullName { get; }
```

Provider-specific expression example:

```csharp
[Column("normalized_email")]
[ComputedColumn(DatabaseType.MySQL, "LOWER(email)", Storage = ComputedColumnStorage.Stored)]
[ComputedColumn(DatabaseType.SQLite, "lower(email)", Storage = ComputedColumnStorage.Virtual)]
public abstract string NormalizedEmail { get; }
```

Rules:

- expressions are provider SQL expressions
- expressions refer to database column names, not C# property names
- multiple provider-specific attributes are allowed
- an exact provider match wins
- `DatabaseType.Default` is allowed only when the expression is intentionally provider-portable

## Metadata Shape

`ColumnDefinition` should gain first-class generated-column metadata:

```csharp
public sealed class GeneratedColumnDefinition
{
    public ComputedColumnStorage Storage { get; }
    public string Expression { get; }
    public DatabaseType DatabaseType { get; }
}
```

`ColumnDefinition` can then expose:

```csharp
public bool IsGenerated { get; }
public GeneratedColumnDefinition? GeneratedColumn { get; }
```

The exact class names can change, but the metadata needs these concepts:

- generated/computed flag
- storage kind: virtual or stored/persistent
- provider dialect
- raw expression text
- source-span support for diagnostics
- deterministic generated metadata emission

Do not hide this inside attributes only. The mutation path, SQL generation path, validation path, source generator, metadata snapshots, and provider readers all need to answer "is this column database-generated?" without scanning attribute bags repeatedly.

## Generated C# Surface

Immutable model:

- generated columns should appear as normal read properties
- nullability should follow the column nullability metadata
- generated columns should be materialized from query results like ordinary columns

Mutable model:

- generated columns should not be required constructor parameters
- generated columns should not expose ordinary public setters
- generated columns may expose a get-only property for existing rows
- for new unsaved mutable rows, the computed value should be treated as unknown until insert/rehydration

The important rule: **the API must make illegal writes hard to express.**

Bad generated mutable shape:

```csharp
public virtual string FullName
{
    get => ...
    set => SetValue(..., value);
}
```

Better generated mutable shape:

```csharp
public virtual string? FullName => (string?)GetValue(Employee.FullNameColumn);
```

or, if the nullability story is too awkward for new mutable instances:

```csharp
public bool TryGetFullName(out string fullName)
```

The first slice should pick the simplest honest surface. Returning nullable from mutable generated properties is less pretty, but it accurately communicates that a newly-created mutable object does not yet have a database-computed value.

## Mutation Behavior

Generated columns must be excluded from mutation write sets.

Current mutation behavior needs changes:

- insert currently iterates `Table.Columns` and sets every column
- update currently sets every changed mutable value
- mutable generated-column setters currently would make it possible to mark generated columns as changed

Required changes:

- `BuildInsertQuery` must skip `column.IsGenerated`
- `BuildUpdateQuery` must skip or reject `column.IsGenerated`
- `MutableRowData.SetValue` or generated mutable setters should prevent generated-column changes
- generated columns must not count as required values for new mutable rows
- transaction/cache refresh should still rehydrate generated column values after insert/update

For updates, silently ignoring a changed generated column is dangerous because it hides a user bug. Prefer preventing the setter from existing. If a low-level path still manages to set a generated column, throw a targeted exception rather than dropping the change quietly.

## SQL Generation

Provider create-table generation needs to emit generated-column SQL after the physical type:

MySQL:

```sql
`full_name` VARCHAR(255) GENERATED ALWAYS AS (CONCAT(first_name, ' ', last_name)) VIRTUAL
```

MariaDB:

```sql
`full_name` VARCHAR(255) GENERATED ALWAYS AS (CONCAT(first_name, ' ', last_name)) PERSISTENT
```

or `STORED` where the target version/provider accepts that alias.

SQLite:

```sql
"full_name" TEXT GENERATED ALWAYS AS (first_name || ' ' || last_name) VIRTUAL
```

Rules:

- generated columns cannot also have `[Default]` or `[DefaultSql]`
- generated columns cannot be auto-increment
- generated columns should not be primary keys in the first slice
- generated-column expressions should be parenthesized by the provider SQL generator
- comments, nullability, and ordinary indexes can be supported only where the provider roundtrip proves them

## Provider Introspection

### MySQL and MariaDB

MySQL/MariaDB should stop skipping generated columns once first-class metadata exists.

The metadata reader should import:

- generated flag from `EXTRA`
- storage kind from `EXTRA`
- expression from `GENERATION_EXPRESSION`
- type, nullability, index, comment, and relation metadata through the existing paths where valid

Provider differences to preserve:

- MySQL uses `VIRTUAL` and `STORED`
- MariaDB uses `VIRTUAL` and `PERSISTENT`, with `STORED` as an alias in current versions
- generated-column expressions are SQL-mode sensitive in places; do not over-normalize them
- MySQL and MariaDB differ in some generated-column DDL and optimizer details

### SQLite

SQLite support should probably be a second provider slice.

The current reader uses `PRAGMA table_info`, which does not include generated columns. SQLite requires `PRAGMA table_xinfo` to see generated and hidden columns, but `table_xinfo` does not give the generation expression. The expression has to be recovered from the original table SQL in `sqlite_schema`/`sqlite_master`.

That means SQLite has two options:

1. Import generated columns only when DataLinq can safely parse the column definition from `sqlite_schema.sql`.
2. Support SQL generation from model metadata first, but keep reverse-import validation limited until the parser exists.

Option 1 is better if the goal is validation and roundtrip fidelity. Option 2 is acceptable only if docs and validation capabilities make the limitation explicit.

## Validation and Drift Detection

Schema validation needs explicit difference kinds:

- `ColumnGeneratedFlagMismatch`
- `ColumnGeneratedStorageMismatch`
- `ColumnGeneratedExpressionMismatch`

Suggested severity/safety:

- missing generated column: error, additive
- extra generated column: warning, destructive
- generated flag mismatch: error, ambiguous
- storage mismatch: error, ambiguous
- expression mismatch: error, ambiguous

Expression comparison should be conservative:

- trim whitespace
- collapse repeated whitespace outside string literals only if a safe helper exists
- normalize a single layer of redundant parentheses only if safe
- do not attempt semantic equivalence

`LOWER(email)` and `lower(email)` may be equivalent in one provider and not worth betting schema validation on globally. False confidence here is worse than noisy drift.

## Diff and Migration Implications

Stateless `datalinq diff` can safely suggest only a small subset:

- add missing virtual generated column where the provider supports it
- add missing stored generated column only with a warning that data may be recomputed or table storage may be rewritten
- comment out expression changes by default
- comment out storage changes by default
- comment out generated/non-generated conversion by default

Do not auto-script generated column changes when:

- another generated column depends on it
- an index depends on it and provider DDL behavior is unclear
- a foreign key depends on it
- SQLite needs a table rebuild

Full migration support should eventually model these as explicit operations, not inferred drop/add guesses.

## Provider Capability Matrix Changes

When implemented, the provider support matrix should distinguish:

- generated column DDL generation
- generated column metadata import
- expression roundtrip
- virtual generated columns
- stored/persistent generated columns
- generated column indexes
- generated column validation
- generated column diff scripting

This should not become one "Generated columns: Supported" checkbox. That would be inaccurate.

## Testing Strategy

Core/unit tests:

- metadata factory accepts computed-column attributes
- generated metadata bootstrap includes generated-column metadata
- generated mutable models do not expose setters for generated columns
- generated columns are excluded from required constructors
- insert/update query construction excludes generated columns
- low-level attempts to set generated columns fail clearly
- schema comparison reports flag/storage/expression mismatches

Provider tests:

- MySQL imports virtual generated columns
- MySQL imports stored generated columns
- MariaDB imports virtual generated columns
- MariaDB imports persistent/stored generated columns
- generated columns roundtrip through SQL generation and metadata import
- generated indexed columns roundtrip only when explicitly supported
- generated columns are not imported as ordinary writable properties
- SQLite generated-column behavior is covered once the `table_xinfo`/`sqlite_schema` parser decision is made

Mutation tests:

- insert ignores generated columns and rehydrates computed values
- update ignores generated columns but rehydrates changed computed values after base column changes
- generated column setter is absent or throws through low-level APIs
- cache invalidation treats base-column updates as ordinary row updates; no special generated-column invalidation should be needed for row-local expressions

## Implementation Slices

### Slice 1: Core Metadata and Attribute

- Add `ComputedColumnAttribute`.
- Add storage enum.
- Add generated-column metadata to typed drafts and `ColumnDefinition`.
- Add validation that generated columns cannot have defaults or auto-increment.
- Add diagnostics for duplicate provider-specific computed expressions.

### Slice 2: Generated C# and Mutation Safety

- Emit immutable generated-column properties normally.
- Emit mutable generated-column properties as get-only/unknown-until-hydrated.
- Exclude generated columns from required constructor params.
- Exclude generated columns from insert/update write sets.
- Add low-level mutation guardrails.

### Slice 3: MySQL/MariaDB Roundtrip

- Import generated columns from `INFORMATION_SCHEMA.COLUMNS`.
- Preserve virtual vs stored/persistent.
- Emit generated-column SQL.
- Add provider roundtrip tests.
- Update the provider support matrix.

### Slice 4: Validation and Diff

- Add generated-column schema difference kinds.
- Compare generated flag, storage, and expression.
- Add conservative diff-script support for safe additive cases.
- Comment out ambiguous/destructive generated-column changes.

### Slice 5: SQLite Support

- Switch generated-column discovery from `PRAGMA table_info` to `PRAGMA table_xinfo`.
- Decide whether to parse `sqlite_schema.sql` for expressions.
- Support SQLite create-table generation.
- Add validation only for the metadata that can be read honestly.

## Non-Goals for the First Implementation

- No generated columns as primary keys.
- No generated columns as foreign keys.
- No automatic expression translation between providers.
- No semantic SQL expression comparison.
- No table-rebuild automation for SQLite generated-column changes.
- No client-side recomputation of generated-column expressions.
- No hidden writes of `DEFAULT` to generated columns during ordinary insert/update.

## References

- [MySQL 8.4: CREATE TABLE and Generated Columns](https://dev.mysql.com/doc/refman/8.4/en/create-table-generated-columns.html)
- [MySQL 8.4: INFORMATION_SCHEMA COLUMNS](https://dev.mysql.com/doc/refman/8.4/en/information-schema-columns-table.html)
- [MariaDB: Generated Columns](https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/generated-columns)
- [MariaDB: INFORMATION_SCHEMA COLUMNS](https://mariadb.com/docs/server/reference/system-tables/information-schema/information-schema-tables/information-schema-columns-table)
- [SQLite: Generated Columns](https://www.sqlite.org/gencol.html)
- [SQLite: PRAGMA table_xinfo](https://sqlite.org/pragma.html#pragma_table_xinfo)
