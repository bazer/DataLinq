> [!WARNING]
> This document is roadmap material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Provider Metadata Roundtrip Fidelity

**Status:** Draft; proposed as the next roadmap phase before schema validation and migrations.

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

Those tests are a good base, but they are not yet a metadata roundtrip conformance suite.

## Current Known Gaps

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

### Checks

- DataLinq does not currently have a first-class check constraint metadata shape.
- MySQL/MariaDB support check constraints, but the exact introspection path differs by version and provider. MySQL 8.0.16+ enforces checks; older versions parse but ignore them.
- SQLite supports `CHECK (...)`, but reliable introspection usually requires parsing the original `CREATE TABLE` SQL in `sqlite_schema.sql`.
- The Phase 4 implementation should start with raw provider-specific check expressions on attributes, with the expression string and its `DatabaseType` as the canonical roundtrip payload. A future first-class `CheckConstraintDefinition` is documented separately in `Check Constraint Metadata Design.md`.

### Comments and Descriptions

- MySQL/MariaDB information-schema models already expose table and column comment fields, but the metadata reader does not currently import them into a first-class DataLinq metadata concept.
- SQLite has no native table or column comments. Any SQLite comment support must be explicitly convention-based or declared unsupported.
- Generated C# XML documentation should be tied to imported comments only when the origin is reliable. A lossy provider comment should not silently overwrite better source comments.
- Comments should exist both as metadata descriptions and as attributes on generated model classes/properties. The attribute string should be the canonical roundtrip payload; XML docs should be generated from it where appropriate, not treated as the source of truth.

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

This matrix is the first artifact this phase should make precise. The entries below are initial audit categories, not final claims.

| Feature | MySQL | MariaDB | SQLite | Desired stance |
| --- | --- | --- | --- | --- |
| Tables and views | Basic read/write exists | Basic read/write exists | Basic read/write exists | Support roundtrip for current subset |
| Columns, nullability, primary key, autoincrement | Basic read/write exists | Basic read/write exists | Basic read/write exists | Support and test |
| Defaults | Literal/current timestamp subset | Literal/current timestamp subset | Literal/current timestamp subset | Support subset; warn on skipped expressions |
| Composite primary keys | Basic metadata exists | Basic metadata exists | Basic metadata exists | Support and test ordering |
| Simple and unique indexes | Basic read/write exists | Basic read/write exists | Basic read/write exists | Support and test |
| Foreign keys | Basic per-column metadata exists | Basic per-column metadata exists | Basic per-column metadata exists | Upgrade constraint grouping before validation relies on it |
| Composite foreign keys | Weak current representation | Weak current representation | Weak current representation | Add first-class constraint-level tests and likely metadata changes |
| Multiple FKs to same table | Risky naming/runtime area | Risky naming/runtime area | Risky naming/runtime area | Fix determinism and runtime loading |
| Check constraints | Not first-class | Not first-class | Not first-class | Start with raw `DatabaseType`-bound expression attributes; defer structured metadata |
| Table/column comments | Available from information_schema, not imported | Available from information_schema, not imported | No native support | Store as metadata and attributes; generate XML docs where useful |
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
- MySQL/MariaDB handling for indexes that include foreign-key columns without dropping non-FK index metadata
- SQLite `pragma_index_xinfo` audit for expression/partial index detection
- explicit unsupported diagnostics or documentation for partial/expression indexes and provider-specific options

The goal is not to support every index option. The goal is to stop losing ordinary indexes and to know when advanced indexes are outside DataLinq's metadata contract.

### Workstream E: Checks, Comments, and Descriptions

Deliverables:

- raw provider-specific check expression attributes as the initial implementation, with a `DatabaseType` on each attribute
- design note for future first-class check constraint metadata
- MySQL/MariaDB table and column comment import
- metadata descriptions for imported comments
- comment attributes on generated model classes/properties as the canonical roundtrip payload
- generated XML documentation from database comments where reliable
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
- **Comments:** Store comments both as metadata descriptions and as attributes on generated classes/properties. The attribute string is the roundtrip source of truth; XML docs are generated presentation.
- **SQLite `CREATE TABLE` parsing:** Accept a narrow parser for supported fixtures and ordinary checks only. Avoid a general SQLite DDL parser; bail out visibly when syntax is outside the supported subset.
- **Composite foreign keys:** Represent them as constraint objects in metadata. Keep generated model attributes connected to the participating properties so the C# surface remains understandable.
- **Ambiguous relation names:** Derive generated relation property names from constraint names when table/column-derived names collide or are ambiguous.
