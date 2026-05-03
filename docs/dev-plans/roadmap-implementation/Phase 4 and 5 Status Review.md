> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Phase 4 and 5 Status Review

**Date:** 2026-05-02

## Bottom Line

Phase 4 is implemented for the provider metadata boundary Phase 5 needed.

Phase 5 is implemented for product trust: validation, drift reporting, conservative diff scripts, and snapshot scoping are in place.

Full versioned migration execution remains intentionally deferred. That is not a small missing helper; it is a separate product surface with history tracking, checksums, transaction semantics, rename operations, and failure recovery. It should not be smuggled into the validation phase.

## Phase 4: Done

Implemented:

- explicit provider metadata support matrix for SQLite, MySQL, and MariaDB
- create-read-generate-create-read roundtrip tests for the supported subset
- ordinary table, column, primary-key, autoincrement, default, simple index, unique index, composite index, and foreign-key metadata coverage
- composite foreign-key grouping with ordered relation metadata
- duplicate same-target relation naming coverage
- quoted identifier handling for spaces, punctuation, C# keyword-shaped names, and leading digits
- MySQL/MariaDB raw check expressions and table/column comments
- unsupported provider features documented instead of treated as validation facts

Still out of scope:

- generated/computed columns
- collation and charset metadata
- referential actions
- deferrable foreign keys
- expression, partial, descending, prefix-length, invisible, and other provider-specific index options
- arbitrary SQL DDL parsing

Verdict: Phase 4 can be considered complete for roadmap purposes. It is not complete as a fantasy "all DDL fidelity" project, but that was never the right goal.

## Phase 5: Done

Implemented:

- provider validation capabilities are encoded in code through `SchemaValidationCapabilities`
- `SchemaDifference` records kind, severity, safety, path, message, and optional model/database definitions
- `SchemaComparer` reports deterministic drift for supported tables, columns, types, nullability, primary keys, autoincrement, defaults, indexes, foreign keys, checks, and comments
- SQLite and MySQL/MariaDB provider tests verify the comparer against live/read metadata and deliberate drift
- `datalinq validate` loads configured source/live metadata and emits text or JSON validation output
- validation exit codes are CI-oriented: success, drift found, or command/config/load failure
- `SchemaDiffScriptGenerator` emits conservative SQL suggestions for additive drift
- `datalinq diff` exposes diff generation through the public CLI
- destructive, ambiguous, informational, and unsupported differences are comments, not executable SQL
- `SchemaMigrationSnapshot` defines a deterministic snapshot JSON shape for the supported schema subset
- snapshot design documents migration IDs, the future applied-migration table, and explicit rename handling

Still out of scope:

- `add-migration`
- `update-database`
- runtime migration APIs
- applied-migration table creation and reads
- migration artifact checksums
- transaction/failure recovery semantics for migration execution
- automatic rename inference
- destructive migration execution

Verdict: Phase 5 has delivered the product-trust foundation. Full migration execution should be treated as a future dedicated workstream, not as cleanup.

## Closeout Verification

The final closeout pass used the testing CLI rather than direct test project invocations.

Verified:

- `run --suite all --alias all --batch-size 2 --output failures --build` built the generators, unit, compliance, and MySQL/MariaDB suites; generators passed `31/31`; unit passed `273/273`; the SQLite compliance batch passed `306/306`.
- `run --suite compliance --targets 'mariadb-10.11,mariadb-11.4,mariadb-11.8' --batch-size 2 --output failures --build` passed all MariaDB compliance batches.
- `run --suite mysql --targets 'mariadb-10.11,mariadb-11.4,mariadb-11.8' --batch-size 2 --output failures --build` passed all MariaDB provider batches.
- `reset --targets mariadb-11.8` followed by `run --suite mysql --targets mariadb-11.8 --output failures --build` passed against a freshly recreated MariaDB 11.8 target.

## Roadmap Position

The roadmap is now at Phase 6.

Recommended move:

1. Keep full migration execution as a future dedicated feature.
2. Start Phase 6: LINQ translation coverage and query composition.

Only choose full migration execution next if it is more valuable than LINQ coverage right now. The migration foundation is concrete enough to resume later; the query-translation gaps affect ordinary application code sooner.
