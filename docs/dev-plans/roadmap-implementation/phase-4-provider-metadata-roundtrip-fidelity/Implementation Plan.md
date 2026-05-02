> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Phase 4 Implementation Plan: Provider Metadata Roundtrip Fidelity

**Status:** Implemented for the Phase 5 validation support boundary.

## Purpose

This document turns the Phase 4 provider metadata fidelity goal from [Roadmap.md](../../Roadmap.md) into an execution plan.

The point of this phase is to make the metadata layer honest enough for schema validation to build on.

DataLinq does not need perfect provider DDL coverage before validation. It does need a tested and documented support boundary for MySQL, MariaDB, and SQLite.

## Starting Baseline

Useful foundations already exist:

- C# model metadata can become `DatabaseDefinition` graphs
- SQLite can read live metadata through `MetadataFromSQLiteFactory`
- MySQL and MariaDB can read live metadata through provider-specific information-schema factories
- SQLite, MySQL, and MariaDB can emit create-table SQL from metadata
- index and relation metadata exists in `ColumnIndex`, `RelationDefinition`, and `RelationPart`
- provider tests already cover basic metadata import and some default handling

The original gaps have been narrowed to an explicit support boundary:

- the provider metadata support matrix exists
- read/generate/re-read roundtrip tests exist for the supported subset
- composite foreign keys are grouped by constraint into ordered relation metadata
- check constraints use documented raw provider-specific expressions
- MySQL/MariaDB comments survive metadata, generated code, generated SQL, and provider roundtrips
- SQLite advanced index and check/comment details are either skipped with warnings or documented as unsupported
- duplicate relation-name cases have deterministic generated names for the supported shapes

## Phase Objective

By the end of this phase, DataLinq should be able to answer:

1. Which MySQL/MariaDB/SQLite metadata features are supported?
2. Which features are intentionally unsupported?
3. Can supported features survive create-read-generate-create-read roundtrips?
4. Do relation and index edge cases behave deterministically?
5. Can the schema validation phase consume this support boundary without guessing?

Current answer: yes, for the supported SQLite/MySQL/MariaDB subset recorded in [Provider Metadata Support Matrix.md](Provider%20Metadata%20Support%20Matrix.md). Phase 5 now consumes that boundary through `SchemaValidationCapabilities` and provider verification tests.

## Workstream A: Provider Support Matrix

Deliverables:

- feature matrix for tables, columns, primary keys, foreign keys, indexes, defaults, checks, comments, identifiers, views, and provider-specific options
- support labels: supported, partially supported, unsupported, and unknown
- links or notes pointing to implementation files and tests
- initial artifact: [Provider Metadata Support Matrix.md](Provider%20Metadata%20Support%20Matrix.md)

Tasks:

1. Audit metadata readers for SQLite, MySQL, and MariaDB.
2. Audit SQL generators for the same providers.
3. Audit `DatabaseDefinition` metadata shape for representable features.
4. Record unsupported provider syntax explicitly.

## Workstream B: Roundtrip Harness

Deliverables:

- test helpers that create schema, read metadata, emit SQL, recreate schema, read metadata again, and compare the supported subset
- SQLite fixture using local database files or in-memory keep-alive behavior
- MySQL/MariaDB fixture using existing server test infrastructure
- deterministic comparison output when a supported roundtrip field changes

Tasks:

1. Define the supported-subset comparison helper.
2. Add a small SQLite roundtrip smoke test.
3. Add MySQL/MariaDB roundtrip smoke tests.
4. Expand fixtures as features are fixed.

## Workstream C: Relations and Constraint Identity

Deliverables:

- coverage for primary-key columns that are also foreign keys across active providers
- coverage for multiple foreign keys from one table to the same target table
- deterministic relation property naming for duplicate relation-name cases
- runtime relation loading tests inside transactions for multiple FKs to the same table
- decision on first-class composite foreign-key metadata
- generated model attributes that keep each participating property visibly connected to the composite constraint

Tasks:

1. Add regression tests for duplicate relation naming and ordering.
2. Add runtime tests for multiple same-target foreign keys inside transactions.
3. Add composite primary-key and composite foreign-key cases.
4. Introduce constraint-level metadata for composite foreign keys if per-column `ForeignKeyAttribute` proves too weak.
5. Use constraint names to derive relation property names when table/column-derived names collide or are ambiguous.

Phase 4 boundary note: composite foreign keys remain visible on each participating value property through `[ForeignKey]` attributes, but relation metadata groups matching constraint names into a single ordered `RelationDefinition`. Generated model files preserve the column order with ordinal foreign-key attributes and array-based `[Relation]` attributes. Generated provider SQL emits one composite `FOREIGN KEY` constraint instead of one invalid per-column constraint. Provider-specific referential actions such as cascade, restrict, deferrable, and match options are still outside the metadata contract.

## Workstream D: Index Fidelity

Deliverables:

- tests for simple, unique, composite, and foreign-key-overlapping indexes
- regression coverage for GitHub issue #6, where CLI model generation lost the full column list for a composite unique index
- class-level `IndexAttribute` generation for composite indexes to avoid duplicate property-level declarations that can drift
- clear validation/diagnostics for the `IndexAttribute.Columns` name contract: database column names are canonical; C# property names may only be a future convenience if explicitly resolved
- MySQL/MariaDB index parsing that does not discard ordinary indexes just because a column participates in a foreign key
- SQLite audit using `pragma_index_xinfo` where needed
- unsupported status for expression, partial, invisible, descending, prefix-length, and provider-specific index options unless implemented

Tasks:

1. Add fixture schemas for overlapping FK/index cases.
2. Add a fixture for a unique composite index like `RakenskapsarFK_Kontonummer` and verify generated models use one class-level attribute with the full ordered column list.
3. Replace generic composite-index parse failures with source-located diagnostics that name the missing column and state that `IndexAttribute.Columns` expects database column names.
4. Verify generated SQL preserves supported index shape.
5. Decide which advanced index features are explicitly out of scope.

Phase 4 boundary note: ordinary simple, unique, and composite indexes preserve ordered database column names through metadata, generated model attributes, generated SQL, and provider re-read for the supported MySQL/MariaDB/SQLite subset. SQLite uses `pragma_index_xinfo` so expression and descending index parts are identified instead of accidentally treated as normal columns. Partial, expression, descending, prefix-length, invisible, and other provider-specific index options remain unsupported unless later metadata adds first-class fields for them.

## Workstream E: Checks, Comments, and Descriptions

Deliverables:

- raw provider-specific check expression attributes with `DatabaseType`
- future-design note for first-class check constraint metadata
- MySQL/MariaDB table and column comment import
- metadata descriptions populated from imported comments
- comment attributes on generated model classes/properties
- source-generated XML docs from imported comments and metadata where reliable
- provider SQL generation for comments if metadata exists
- explicit SQLite stance for comments

Tasks:

1. Add MySQL/MariaDB comment import tests.
2. Store imported comments in metadata and emit attributes so comments roundtrip through generated models.
3. Generate XML docs from `[Comment]` attributes and metadata where it improves generated code, without making XML doc text the schema roundtrip source.
4. Add check-constraint fixtures and import/export raw expression attributes, including provider-specific variants on the same model.
5. Document unsupported SQLite/native-comment behavior.
6. Keep SQLite `CREATE TABLE` parsing narrow and warning-based when syntax exceeds the supported subset.

Phase 4 boundary note: check constraints are represented as raw provider-specific `[Check(DatabaseType, name, expression)]` attributes. That is the right shape for fidelity work because it preserves what the provider reports without pretending DataLinq can understand every SQL expression. First-class structured check metadata should wait until schema validation or migrations need expression analysis.

## Workstream F: Identifier Robustness

Deliverables:

- tests for columns with spaces in names
- tests for reserved words and punctuation
- stable generated C# property names with `[Column]` preserving database names
- provider-aware identifier comparison rules for the next validation phase

Tasks:

1. Add provider schemas with quoted identifiers.
2. Verify generated model attributes preserve original names.
3. Verify generated SQL re-quotes identifiers correctly.
4. Record provider-specific casing rules that validation must respect.

Phase 4 boundary note: provider-imported table and column names are sanitized into valid C# model, property, relation, and parameter identifiers, while the original provider names remain canonical in `[Table]`, `[Column]`, and generated SQL. The roundtrip comparer still compares provider-reported names exactly. The validation phase must add provider-aware identifier matching instead of reusing that exact comparer as-is.

Provider casing rules for validation:

- SQLite preserves declared identifier text in metadata but resolves identifiers case-insensitively for ordinary ASCII names. Validation should compare SQLite identifiers case-insensitively while still reporting the preserved declaration text.
- MySQL and MariaDB column, index, alias, and routine names are effectively case-insensitive for validation purposes. Table and database name casing depends on server settings and filesystem behavior, especially `lower_case_table_names`, so validation must inspect or configure that rule before comparing table names.
- DataLinq-generated C# identifiers are not schema identifiers. Validation should compare database names from metadata attributes, not generated C# model or property names.

## Proposed Execution Order

1. Build the support matrix skeleton.
2. Add the roundtrip harness skeleton.
3. Add focused failing tests for identifiers, composite indexes from issue #6, overlapping FK indexes, duplicate relation names, and multiple FKs to the same table.
4. Fix simple metadata reader/generator losses found by those tests.
5. Implement raw check expression attributes and defer structured check metadata.
6. Import MySQL/MariaDB comments into metadata, generated attributes, and XML docs where appropriate.
7. Revisit composite foreign-key representation before validation starts.
8. Update the validation plan with the final support boundary.

Execution status: these steps have been completed for the validation support boundary. The remaining provider-fidelity ideas are deliberately future work, not blockers for Phase 5.

## Verification Plan

At minimum, each implementation slice should run the focused tests it changes.

Before closing the phase, run:

- `DataLinq.Tests.Unit`
- SQLite metadata-reader and roundtrip tests
- MySQL/MariaDB metadata-reader and roundtrip tests where local provider infrastructure is available
- generator tests affected by XML docs, identifiers, indexes, and relations

If local MySQL/MariaDB infrastructure is unavailable, record that honestly instead of claiming provider coverage.

## Exit Criteria

Phase 4 is complete when:

- the provider support matrix is explicit and linked from the roadmap
- supported metadata features have roundtrip tests
- unsupported provider features are documented rather than implied
- relation naming is deterministic for duplicate cases
- multiple same-target foreign keys work in metadata and runtime loading
- primary-key-plus-foreign-key behavior is tested across active providers
- columns with spaces and reserved names are tested across active providers
- check constraints and comments are either implemented or explicitly deferred per provider
- the Phase 5 validation plan has been updated to consume the final support boundary

Status against exit criteria:

- complete for the current validation boundary
- not complete as a full DDL fidelity project, which was never the right bar for this phase
- remaining unsupported provider features are documented as future metadata/modeling work rather than validation inputs

## Non-Goals

- full migration support
- schema drift comparer implementation
- a general-purpose SQL DDL parser
- broad provider expansion
- LINQ parser work
- Native AOT or async runtime work

## First Implementation Slice

The first slice should produce the matrix and the harness, then immediately exercise the holes that already came up:

1. columns with spaces
2. foreign-key columns that also have ordinary indexes
3. primary-key columns that are also foreign keys
4. multiple foreign keys to the same table
5. duplicate generated relation property names and ordering stability

That gives the phase teeth. A matrix without tests is just a nicely formatted shrug.
