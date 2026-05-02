> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.
# Phase 5: Product Trust Features

**Status:** Implemented for validation, conservative diffing, and snapshot scoping; full versioned migration execution is deferred.

## Scope

This folder tracks the execution plan for the fifth roadmap phase described in [Roadmap.md](../../Roadmap.md).

Phase 5 is about making DataLinq safer to adopt in real projects:

1. detect schema drift between models and live databases
2. report differences in a way a developer can act on
3. generate conservative SQL diff scripts only after validation is trustworthy
4. keep versioned/snapshot migrations as a later slice, not the first move

## Starting Stance

The repo now has enough metadata machinery from Phase 4:

- C# models can become `DatabaseDefinition` graphs
- SQLite, MySQL, and MariaDB can read live database metadata with an explicit support boundary
- SQLite, MySQL, and MariaDB can generate create SQL from supported metadata
- the active TUnit suites already include metadata-from-server and provider SQL coverage
- provider metadata roundtrip tests identify which schema features validation may compare

The missing core is no longer drift detection. The comparer, validator, CLI surface, conservative diff generator, and snapshot DTO now exist, and Phase 5 is closed for this intended product-trust scope.

## Current Status

Implemented:

- provider capability rules from Phase 4 are encoded in `SchemaValidationCapabilities`
- the pure comparer reports table, column, supported column shape, simple/unique index, foreign-key, MySQL/MariaDB check, and MySQL/MariaDB comment drift
- SQLite and MySQL/MariaDB metadata tests now verify the comparer against live/read metadata and deliberate drift
- `datalinq validate` exposes drift detection through text and JSON output with CI-oriented exit codes
- `datalinq diff` emits conservative SQL suggestions for supported additive drift and comments out destructive or ambiguous drift
- `SchemaMigrationSnapshot` defines a deterministic versioned snapshot JSON shape
- `Snapshot Migration Design.md` records migration identity, applied-migration table, and explicit rename handling for later execution work

Deferred:

- `add-migration`
- `update-database`
- runtime migration APIs
- applied-migration table implementation
- automatic rename inference
- destructive migration execution

Closeout note:

- the generators, unit suite, SQLite compliance lane, and MariaDB compliance/provider lanes were verified through the testing CLI
- the local MySQL 8.4 host-port lane remains blocked by an authentication issue where in-container clients authenticate but host-side MySqlConnector calls are denied as `localhost`; that belongs to infrastructure follow-up, not Phase 5 product-trust scope

## Documents

- `Implementation Plan.md`
- `Snapshot Migration Design.md`

## Related Plans

- [`../../providers-and-features/Provider Metadata Roundtrip Fidelity.md`](../../providers-and-features/Provider%20Metadata%20Roundtrip%20Fidelity.md)
- [`../../providers-and-features/Migrations and Validation.md`](../../providers-and-features/Migrations%20and%20Validation.md)
- [`../../Roadmap.md`](../../Roadmap.md)
