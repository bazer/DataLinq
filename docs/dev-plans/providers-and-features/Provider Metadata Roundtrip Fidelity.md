> [!WARNING]
> This document is roadmap material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Provider Metadata Roundtrip Fidelity

**Status:** Implemented for the Phase 4 validation support boundary; remaining provider-fidelity ideas are future work.

## Purpose

DataLinq needs a provider metadata fidelity pass before serious schema validation can be trusted.

The immediate goal is not to support every DDL feature in MySQL, MariaDB, and SQLite. The goal is stricter and more useful:

1. know exactly which metadata features DataLinq can read from a live database
2. know exactly which of those features can be represented in `DatabaseDefinition`
3. know exactly which represented features can be emitted back as provider SQL
4. add tests for the supported roundtrip subset
5. explicitly document the important provider features that are not supported yet

Without that, schema validation will produce a dangerous mix of false confidence and noisy false positives.

## Why This Comes Before Validation

The validation and migrations plan depends on comparing code metadata to live database metadata. That comparison is only as good as the metadata readers and SQL generators underneath it.

If the provider reader drops a check constraint, a column comment, index details, or part of a composite foreign key, then the comparer cannot honestly say whether the schema matches. It can only compare the simplified shape DataLinq happened to preserve.

That is fine if it is explicit. It is not fine if we pretend it is full schema fidelity.

## Current Code Surface

The relevant implementation surface is:

- MySQL metadata reader: `src/DataLinq.MySql/MySql/MetadataFromMySqlFactory.cs`
- MariaDB metadata reader: `src/DataLinq.MySql/MariaDB/MetadataFromMariaDBFactory.cs`
- shared MySQL/MariaDB metadata parsing: `src/DataLinq.MySql/Shared/MetadataFromSqlFactory.cs`
- SQLite metadata reader: `src/DataLinq.SQLite/MetadataFromSQLiteFactory.cs`
- MySQL/MariaDB SQL generation: `src/DataLinq.MySql/Shared/SqlFromMetadataFactory.cs`
- SQLite SQL generation: `src/DataLinq.SQLite/SqlFromSQLiteFactory.cs`
- metadata index shape: `src/DataLinq.SharedCore/Metadata/ColumnIndex.cs`
- metadata relation shape: `src/DataLinq.SharedCore/Metadata/RelationDefinition.cs`
- relation construction: `src/DataLinq.SharedCore/Factories/MetadataFactory.cs`
- model generation attributes: `src/DataLinq.SharedCore/Factories/Models/ModelFileFactory.cs`

The current tests already cover useful basics:

- SQLite metadata import in `src/DataLinq.Tests.Unit/SQLite/MetadataFromSQLiteFactoryTests.cs`
- MySQL/MariaDB server metadata import in `src/DataLinq.Tests.MySql/MetadataFromServerFactoryTests.cs`
- MySQL/MariaDB default parsing in `src/DataLinq.Tests.MySql/MetadataFromSqlFactoryDefaultParsingTests.cs`
- reserved keyword mutation tests in `src/DataLinq.Tests.MySql/ReservedKeywordMutationTests.cs`

Those tests have since grown into the Phase 4 metadata roundtrip conformance suite for the supported subset. The authoritative current matrix is [Provider Metadata Support Matrix](../roadmap-implementation/phase-4-provider-metadata-roundtrip-fidelity/Provider%20Metadata%20Support%20Matrix.md).

## Current Implementation Status

Implemented for the validation boundary:

- create-read-generate-create-read roundtrip tests for SQLite, MySQL, and MariaDB supported metadata
- ordinary simple, unique, and composite index fidelity with ordered database column names
- composite foreign-key grouping into ordered relation metadata and generated provider SQL
- deterministic relation naming for duplicate same-target and composite-key cases covered by tests
- quoted identifier preservation through generated C# attributes and generated SQL
- MySQL/MariaDB raw check-expression attributes and comments
- explicit unsupported status for advanced provider-specific index options, generated columns, collation/charset, deferrable foreign keys, and referential actions

The sections below preserve the original audit rationale and design choices. Where they conflict with the Phase 4 support matrix, the support matrix is the current source of truth.

## Historical Known Gaps

### Constraints and Relations

- Composite foreign keys are currently represented through per-column `ForeignKeyAttribute` values. That is enough for simple relations, but it is a weak shape for constraint-level fidelity because order, grouping, and referenced candidate key semantics belong to the constraint, not to each column in isolation.
- Composite foreign keys should probably become constraint objects in metadata. Model properties should still carry attributes that connect each property to the constraint, but the canonical metadata should group the columns by constraint name and ordinal.
- A foreign key that is also part of a primary key needs explicit test coverage across MySQL, MariaDB, and SQLite. There is already a MySQL/MariaDB regression test for a primary-key column that is also a foreign key, but the broader composite and roundtrip behavior is not proven.
- Multiple foreign keys from one table to the same target table need deterministic relation identity, deterministic generated property names, and runtime relation loading tests. The current `_2` suffix behavior is pragmatic, but it is not a robust naming strategy if metadata ordering changes.
- Relation loading inside transactions needs coverage where several foreign keys point to the same table. This is a runtime correctness bug class, not just metadata cosmetics.
- `ON DELETE` and `ON UPDATE` rules are not currently first-class metadata in the relation model.
- SQLite deferrable foreign-key clauses are not represented.

### Indexes

- MySQL/MariaDB read non-primary indexes from `information_schema.STATISTICS`, but current parsing filters out columns that participate in foreign keys. That can hide user-created indexes that overlap foreign-key columns.
- Index fidelity is currently column/name/type/unique-oriented. It does not preserve all provider-specific details such as prefix length, expression indexes, partial predicates, descending order, visibility, parser options, or index comments.
- SQLite index parsing uses `pragma_index_list` and `pragma_index_info`, which is enough for simple and unique indexes but not enough for expression indexes or partial indexes. Those require `pragma_index_xinfo` and/or parsing `sqlite_schema.sql`.
- Primary keys are reconstructed mostly from column metadata and then normalized into a generated `ColumnIndex`; provider constraint names are not preserved.
- GitHub issue [#6](https://github.com/bazer/DataLinq/issues/6) reports a concrete composite-index roundtrip bug: CLI model generation emitted a unique two-column index attribute on only one property without the full ordered column list, and manual repair with `nameof(...)` exposed that the parser expects database column names rather than C# property names. Phase 4 should cover both the generated attribute shape and the accepted column-name contract.
- Decision: `IndexAttribute.Columns` should use database column names as the canonical stored form. Model parsing may optionally resolve C# property names as a convenience later, but generated attributes and metadata roundtripping should emit database names.

### Checks

- DataLinq does not currently have a first-class check constraint metadata shape.
- MySQL/MariaDB support check constraints, but the exact introspection path differs by version and provider. MySQL 8.0.16+ enforces checks; older versions parse but ignore them.
- SQLite supports `CHECK (...)`, but reliable introspection usually requires parsing the original `CREATE TABLE` SQL in `sqlite_schema.sql`.
- The Phase 4 implementation should start with raw provider-specific check expressions on attributes, with the expression string and its `DatabaseType` as the canonical roundtrip payload. A future first-class `CheckConstraintDefinition` is documented separately in `Check Constraint Metadata Design.md`.

### Comments and Descriptions

- MySQL/MariaDB information-schema models already expose table and column comment fields, but the metadata reader does not currently import them into a first-class DataLinq metadata concept.
- SQLite has no native table or column comments. Any SQLite comment support must be explicitly convention-based or declared unsupported.
- Generated C# XML documentation should be tied to imported comments only when the origin is reliable. A lossy provider comment should not silently overwrite better source comments.
- Comments should exist both as metadata descriptions and as attributes on generated model classes/properties. The attribute string should be the canonical roundtrip payload; XML docs should be source-generated from it where appropriate, not treated as the source of truth.

### Identifiers and Quoting

- Columns with spaces, punctuation, reserved keywords, and provider-specific quoting need a dedicated conformance suite.
- SQL generation already quotes identifiers, but metadata-to-C# naming needs deterministic escaping, source attributes, and generated property names that remain stable when ordering changes.
- Case sensitivity is provider- and collation-sensitive. Comparison rules for validation must not pretend all providers behave like the same filesystem.

### Defaults, Types, and Column Details

- Default parsing has improved, but expression defaults are still intentionally skipped in several cases.
- Generated columns/computed columns are not represented.
- Collation and character set are not first-class in column metadata.
- MySQL/MariaDB enum/set handling exists in a narrow form; full DDL fidelity is not guaranteed.
- SQLite type affinity makes exact type roundtripping impossible unless the original declared type is preserved deliberately.

## Provider Feature Matrix

This matrix was the first artifact this phase needed to make precise. The authoritative current matrix now lives in [Provider Metadata Support Matrix](../roadmap-implementation/phase-4-provider-metadata-roundtrip-fidelity/Provider%20Metadata%20Support%20Matrix.md). The entries below are retained as historical audit context, not final support claims.

| Feature | MySQL | MariaDB | SQLite | Desired stance |
| --- | --- | --- | --- | --- |
| Tables and views | Basic read/write exists | Basic read/write exists | Basic read/write exists | Support roundtrip for current subset |
| Columns, nullability, primary key, autoincrement | Basic read/write exists | Basic read/write exists | Basic read/write exists | Support and test |
| Defaults | Literal/current timestamp subset | Literal/current timestamp subset | Literal/current timestamp subset | Support subset; warn on skipped expressions |
| Composite primary keys | Basic metadata exists | Basic metadata exists | Basic metadata exists | Support and test ordering |
| Simple and unique indexes | Basic read/write exists | Basic read/write exists | Basic read/write exists | Support and test |
| Composite index model generation | Known bug in issue #6 | Known bug class | Needs coverage | Emit full ordered column list and define DB-name vs property-name contract |
| Foreign keys | Basic per-column metadata exists | Basic per-column metadata exists | Basic per-column metadata exists | Upgrade constraint grouping before validation relies on it |
| Composite foreign keys | Weak current representation | Weak current representation | Weak current representation | Add first-class constraint-level tests and likely metadata changes |
| Multiple FKs to same table | Risky naming/runtime area | Risky naming/runtime area | Risky naming/runtime area | Fix determinism and runtime loading |
| Check constraints | Not first-class | Not first-class | Not first-class | Start with raw `DatabaseType`-bound expression attributes; defer structured metadata |
| Table/column comments | Available from information_schema, not imported | Available from information_schema, not imported | No native support | Store as metadata and attributes; source-generate XML docs where useful |
| Column names with spaces | Quoting exists, full flow not proven | Quoting exists, full flow not proven | Quoting exists, full flow not proven | Support and test |
| Partial/expression indexes | Not represented | Not represented | Not represented | Explicitly unsupported initially |
| Generated/computed columns | Not represented | Not represented | Not represented | Explicitly unsupported initially |
| Collation/charset | Not first-class | Not first-class | Not first-class | Defer or compare only where metadata is added |

## Planned Workstreams

### Workstream A: Support Matrix and Audit

Deliverables:

- provider-by-provider feature matrix for MySQL, MariaDB, and SQLite
- explicit classification for each feature: supported, partially supported, unsupported, or unknown
- code references for each supported feature
- tests references for each supported claim

The outcome should be boring and precise. If a feature is not tested, it should not be called supported.

### Workstream B: Roundtrip Test Harness

Deliverables:

- provider metadata roundtrip tests that create schema, read metadata, generate SQL, recreate schema, read metadata again, and compare the supported subset
- focused fixtures for SQLite and server-backed MySQL/MariaDB
- deterministic comparison helpers that can ignore explicitly unsupported provider details

This is not the schema comparer from the validation phase. It is a lower-level conformance harness for provider metadata fidelity.

### Workstream C: Constraint and Relation Fidelity

Deliverables:

- tests for primary-key columns that are also foreign keys
- tests for composite primary keys and composite foreign keys, including column order
- tests for multiple foreign keys from one table to the same target table
- deterministic relation property naming rules
- runtime tests for relation loading inside transactions with several foreign keys to the same table
- metadata representation for composite foreign-key constraints, with generated attributes on model properties clearly linking each participating property to the constraint

This workstream may require metadata shape changes. If it does, make them here, before the schema comparer starts depending on the current weak shape.

### Workstream D: Index Fidelity

Deliverables:

- tests for simple, unique, composite, and foreign-key-overlapping indexes
- regression coverage for GitHub issue #6: CLI-generated composite index attributes must be class-level declarations carrying the full ordered column list, and model parsing must give a useful diagnostic if an index references property names where database column names are required
- MySQL/MariaDB handling for indexes that include foreign-key columns without dropping non-FK index metadata
- SQLite `pragma_index_xinfo` audit for expression/partial index detection
- explicit unsupported diagnostics or documentation for partial/expression indexes and provider-specific options

The goal is not to support every index option. The goal is to stop losing ordinary indexes and to know when advanced indexes are outside DataLinq's metadata contract.

## Example API Shapes

These examples are not final API commitments. They are sketches to make Phase 4 implementation choices concrete before code starts.

### Composite Index Attributes

Use a single class-level attribute with database column names in the ordered column list:

```csharp
[Index(
    "RakenskapsarFK_Kontonummer",
    IndexCharacteristic.Unique,
    IndexType.BTREE,
    "RakenskapsarFK",
    "Kontonummer")]
public abstract partial class Account { }

[Column("RakenskapsarFK")]
public abstract int AccountingYearId { get; }

[Column("Kontonummer")]
public abstract int AccountNumber { get; }
```

This should replace duplicate property-level attributes for composite indexes. Duplicated attributes create an unnecessary consistency problem: two declarations that should describe the same index can drift. A class-level declaration is the single source of truth, and `Column` attributes keep the property-to-database-column mapping visible.

### Comments

Comments should roundtrip through attributes and also populate metadata descriptions:

```csharp
[Comment("Customer invoices imported from the accounting system.")]
[Table("invoice")]
public abstract partial class Invoice { }

[Comment("Human-readable invoice number shown to users.")]
[Column("invoice_no")]
public abstract string InvoiceNumber { get; }
```

The `[Comment]` string is the roundtrip payload. XML docs should be generated by the source generator from that attribute and other metadata, rather than generated into model files as user-editable source text. That keeps the diffable schema source in one place: the attribute.

Generated XML docs can include more than the comment string where useful:

```csharp
/// <summary>
/// Human-readable invoice number shown to users.
/// </summary>
/// <remarks>
/// Database column: invoice_no.
/// Required.
/// </remarks>
public abstract string InvoiceNumber { get; }
```

The user should edit `[Comment(...)]`, not the generated XML doc output.

If provider-specific comments become necessary, the same pattern as checks can be extended later:

```csharp
[Comment(DatabaseType.MySQL, "Stored as a MySQL column comment.")]
```

### Raw Check Attributes

Check expressions are provider SQL, so they are bound to `DatabaseType`:

```csharp
[Check(DatabaseType.MySQL, "`start_date` <= `end_date`")]
[Check(DatabaseType.SQLite, "\"start_date\" <= \"end_date\"")]
public abstract partial class Subscription { }
```

Column names inside the expression are provider SQL identifiers, not C# property names.

### Composite Foreign-Key Attributes

The metadata model should group composite foreign keys as constraints. Generated model code still needs to show how properties participate.

One possible class-level shape:

```csharp
[ForeignKeyConstraint(
    "FK_OrderLine_Order",
    "orders",
    ["order_id", "tenant_id"],
    ["id", "tenant_id"])]
public abstract partial class OrderLine { }
```

One possible property-level companion shape:

```csharp
[ForeignKeyPart("FK_OrderLine_Order", 0)]
[Column("order_id")]
public abstract int OrderId { get; }

[ForeignKeyPart("FK_OrderLine_Order", 1)]
[Column("tenant_id")]
public abstract int TenantId { get; }
```

The class-level attribute carries the database relationship. The property-level attributes are mainly for readability, diagnostics, and source-location mapping.

The corresponding metadata could be shaped like:

```csharp
public sealed class ForeignKeyConstraintDefinition
{
    public string Name { get; }
    public TableDefinition ForeignKeyTable { get; }
    public TableDefinition CandidateTable { get; }
    public IReadOnlyList<ForeignKeyColumnMapping> Columns { get; }
    public ReferentialAction? OnDelete { get; }
    public ReferentialAction? OnUpdate { get; }
}

public sealed class ForeignKeyColumnMapping
{
    public int Ordinal { get; }
    public ColumnDefinition ForeignKeyColumn { get; }
    public ColumnDefinition CandidateColumn { get; }
}
```

That shape avoids pretending each column owns the full relation, while still letting generated model properties point back to the constraint.

### Workstream E: Checks, Comments, and Descriptions

Deliverables:

- raw provider-specific check expression attributes as the initial implementation, with a `DatabaseType` on each attribute
- design note for future first-class check constraint metadata
- MySQL/MariaDB table and column comment import
- metadata descriptions for imported comments
- comment attributes on generated model classes/properties as the canonical roundtrip payload
- source-generated XML documentation from database comments and metadata where reliable
- SQL generation of comments where metadata exists
- explicit SQLite comment stance

For SQLite, the honest default is probably unsupported native comments, with optional future support through conventions. Pretending SQLite has normal column comments would be fiction.

SQLite check constraints are different: SQLite supports checks, but extracting them means parsing `sqlite_schema.sql`. Keep that parser deliberately narrow. It should handle ordinary `CREATE TABLE` checks, quoted identifiers, string literals, and nested parentheses well enough for supported fixtures, and it should bail out with a warning instead of guessing on virtual tables, `CREATE TABLE AS SELECT`, generated columns, exotic conflict clauses, or expressions it cannot tokenize safely.

### Workstream F: Identifier Robustness

Deliverables:

- conformance cases for spaces in column names
- reserved words, punctuation, casing, and table names that require quoting
- stable generated C# property names with `[Column]` attributes preserving the original database name
- validation rules that compare database identifiers with provider-aware semantics

## First Implementation Slice

The first useful slice should be:

1. create the support matrix and roundtrip harness structure
2. add failing or pending tests for columns with spaces, overlapping foreign-key indexes, and multiple foreign keys to the same table
3. fix any simple metadata reader losses discovered by those tests
4. document unsupported advanced index/check/comment features instead of letting them stay ambiguous

Composite foreign-key metadata should follow immediately after that. It is too central to relation correctness to leave vague.

## Exit Criteria

This phase is complete when:

- MySQL, MariaDB, and SQLite have an explicit provider metadata support matrix
- all supported metadata features have provider tests
- create-read-generate-create-read roundtrip tests exist for the supported subset
- relation naming is deterministic for duplicate relation-name cases
- multiple foreign keys to the same table behave correctly in metadata and runtime relation loading
- primary-key-plus-foreign-key combinations are tested across active providers
- check constraints and comments are either implemented or explicitly documented as unsupported per provider
- column names with spaces and reserved words are covered by tests
- the validation/migrations plan has a clear support boundary to consume

## Non-Goals

- a full SQL parser for arbitrary provider DDL
- full EF-style migration support
- preserving unsupported provider-specific DDL clauses
- support for every MySQL/MariaDB/SQLite index option in the first pass
- changing LINQ translation behavior
- broad provider expansion beyond MySQL, MariaDB, and SQLite

## Decisions From Initial Review

- **Check constraints:** Start with raw provider-specific expression attributes that include `DatabaseType`. Keep `CheckConstraintDefinition` as a later design, documented in `Check Constraint Metadata Design.md`.
- **Comments:** Store comments both as metadata descriptions and as attributes on generated classes/properties. The attribute string is the roundtrip source of truth; XML docs are source-generated presentation.
- **Index column names:** Store and generate database column names in `IndexAttribute.Columns`; do not use `nameof(...)` as the canonical roundtrip form.
- **Composite indexes:** Use class-level `IndexAttribute` declarations for composite indexes, not duplicate property-level declarations.
- **XML docs for comments:** Generate XML docs from `[Comment]` and metadata through the source generator. Do not make XML doc source the roundtrip payload.
- **SQLite `CREATE TABLE` parsing:** Accept a narrow parser for supported fixtures and ordinary checks only. Avoid a general SQLite DDL parser; bail out visibly when syntax is outside the supported subset.
- **Composite foreign keys:** Represent them as constraint objects in metadata. Keep generated model attributes connected to the participating properties so the C# surface remains understandable.
- **Ambiguous relation names:** Derive generated relation property names from constraint names when table/column-derived names collide or are ambiguous.
