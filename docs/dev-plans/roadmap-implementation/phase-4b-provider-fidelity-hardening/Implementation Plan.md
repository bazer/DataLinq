> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Phase 4B Implementation Plan: Provider Fidelity Hardening

**Status:** Implemented.

## Purpose

Phase 4B plugs the most useful holes found during the Phase 4 support-matrix review. The goal is not exhaustive DDL fidelity. The goal is to stop losing or misrepresenting provider metadata that ordinary users reasonably expect to survive a read/generate/read cycle.

## Goals

- preserve `ON UPDATE` and `ON DELETE` actions for foreign keys
- warn and skip unsupported advanced index shapes instead of importing them as ordinary indexes
- warn and skip generated/computed columns until DataLinq has a first-class read-only/generated column model
- preserve raw provider SQL default expressions for MySQL/MariaDB when typed C# default conversion would be dishonest
- compare views in schema validation at the safe presence and column-shape boundary
- update the provider metadata matrix from tests, not optimism

## Non-Goals

- structured SQL-expression parsing for defaults, checks, generated columns, or views
- deferrable foreign-key metadata
- provider-neutral collation or character-set metadata
- generated-column mutation semantics
- full view-definition diffing
- arbitrary expression, partial, descending, prefix-length, invisible, or ignored index support

## Workstream A: Foreign-Key Referential Actions

Goals:

- add shared metadata for referential actions
- preserve provider-reported `ON UPDATE` and `ON DELETE` rules through generated models and generated SQL
- make validation and roundtrip comparison include the action pair

Tasks:

1. Add a shared referential-action enum and extend `[ForeignKey]`.
2. Add `OnUpdate` and `OnDelete` to `RelationDefinition`.
3. Parse actions from MySQL/MariaDB `information_schema.REFERENTIAL_CONSTRAINTS`.
4. Parse actions from SQLite `pragma_foreign_key_list`.
5. Emit action-aware `[ForeignKey]` attributes and action-aware provider SQL.
6. Extend roundtrip comparison and schema validation signatures.
7. Add SQLite and MySQL/MariaDB roundtrip coverage.

Support boundary:

- supported: `NO ACTION`, `RESTRICT`, `CASCADE`, `SET NULL`, and `SET DEFAULT` where the provider accepts them
- not supported: deferrable constraints, match clauses, and provider-specific enforcement timing

Status: Complete. Workstream A adds provider-scoped referential actions to `[ForeignKey]` and `RelationDefinition`, reads them from MySQL/MariaDB and SQLite metadata, emits them into generated model attributes and provider SQL, and includes them in roundtrip and validation comparison.

## Workstream B: Advanced Index Guardrails

Goals:

- prevent lossy index imports
- keep the ordinary index contract clean and testable

Tasks:

1. Keep SQLite partial/expression/descending guardrails.
2. Add MySQL warnings and skips for expression, descending, prefix-length, and invisible indexes.
3. Add MariaDB warnings and skips for descending, prefix-length, and ignored indexes.
4. Add provider tests for at least one unsupported MySQL/MariaDB index shape.
5. Update the matrix wording so unsupported advanced index features are explicit guardrails, not silent omissions.

Status: Complete. Workstream B keeps SQLite expression/partial/descending detection and adds MySQL/MariaDB guardrails for lossy advanced index shapes. Prefix-length indexes are covered in the server suite, and MySQL expression, descending, and invisible indexes plus MariaDB descending and ignored indexes are now skipped rather than imported as ordinary indexes when the metadata exposes those flags.

## Workstream C: Generated Column Guardrails

Goals:

- avoid generating mutable C# columns for provider-generated values
- avoid downstream index/FK failures when generated columns are skipped

Tasks:

1. Detect MySQL/MariaDB generated columns from information schema metadata.
2. Warn and skip generated columns during import.
3. Skip indexes or relations that point at skipped columns with clear warnings.
4. Add provider tests proving generated columns do not enter the model as ordinary value properties.

Design note:

Generated columns probably deserve first-class metadata later, but they should not be imported as normal writable columns. A read-only/generated column contract touches mutation generation and insert/update behavior, so Phase 4B keeps the safe boundary: detect, warn, and skip.

Status: Complete. Workstream C detects MySQL/MariaDB generated columns through actual generated-column markers, warns, and skips them. Dependent indexes and relations that refer to skipped columns are also skipped with warnings instead of failing later with misleading metadata.

## Workstream D: Raw Provider Default Expressions

Goals:

- preserve provider defaults that are SQL expressions rather than typed literals
- avoid converting expression text into misleading string defaults

Tasks:

1. Add a provider-scoped raw default attribute.
2. Parse and emit the attribute in generated model files.
3. Emit raw provider default SQL only for matching provider targets.
4. Keep generated mutable constructors from assigning raw SQL text as a C# value.
5. Add provider tests for a raw MySQL/MariaDB expression default.

Support boundary:

- supported: raw provider SQL expression preservation for MySQL/MariaDB defaults that cannot be safely reduced to typed C# values
- not supported: provider-neutral default-expression analysis

Status: Complete. Workstream D adds `[DefaultSql(DatabaseType, expression)]`, parses it from generated model files, emits matching-provider raw default SQL, keeps raw SQL out of mutable C# constructor defaults, and preserves MySQL/MariaDB expression defaults through provider roundtrips.

## Workstream E: View Validation Boundary

Goals:

- make schema validation report missing and extra views
- compare view columns where provider metadata already supplies them
- avoid pretending view SQL text can be normalized reliably yet

Tasks:

1. Include views in the comparable object set.
2. Report table/view type mismatches.
3. Compare columns for views.
4. Keep table-only metadata comparisons, such as indexes, foreign keys, checks, and comments, scoped to real tables.
5. Add validation tests for missing/extra views and view column drift.

Status: Complete. Workstream E includes views in schema validation for presence, table/view type mismatches, and columns. Table-only metadata comparisons stay scoped to real tables, and full view-definition diffing remains deferred.

## Verification Plan

At minimum:

- `run --suite unit --alias quick --output failures --build`
- `run --suite mysql --alias latest --batch-size 2 --output failures --build`
- `docfx docfx.json` when docs change

Targeted workstream verification can use the active TUnit projects directly, but the phase should close with the Testing CLI quick suites where available.

## Exit Criteria

Phase 4B is complete when:

- referential actions roundtrip through metadata, generated model attributes, generated SQL, and validation comparison
- unsupported advanced index shapes are warned and skipped for all audited providers
- MySQL/MariaDB generated columns are warned and skipped instead of imported as mutable columns
- raw MySQL/MariaDB default expressions roundtrip as provider-scoped metadata
- schema validation includes views at the documented safe boundary
- the provider metadata matrix reflects the implemented and tested support boundary

Status: Complete for the scoped support boundary above.
