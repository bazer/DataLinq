> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Phase 5 Implementation Plan: Product Trust Features

**Status:** Planning.

## Purpose

This document turns the Phase 5 goals from [Roadmap.md](../../Roadmap.md) into an execution plan.

The point of this phase is not to ship a magical migration system first.

The point is to make DataLinq trustworthy enough that a developer can answer a basic adoption question:

Is my C# model actually compatible with the database I am about to run against?

If DataLinq cannot answer that clearly, generated migration scripts and runtime auto-migration would be premature.

## Starting Baseline

Several important things are already true:

- model metadata is represented by `DatabaseDefinition`, `TableDefinition`, `ColumnDefinition`, indices, relations, defaults, and source locations
- C# model metadata can be built from interfaces and source generator inputs
- SQLite can read live database metadata through `MetadataFromSQLiteFactory`
- MySQL and MariaDB can read live database metadata through `MetadataFromSqlFactory` and information-schema models
- provider-specific create-table SQL generation already exists
- metadata-from-server tests already cover SQLite, MySQL, and MariaDB behavior
- Phase 2 improved generator diagnostics and default-value validation
- Phase 4 should provide the provider metadata roundtrip support boundary this phase relies on

But the product-trust gap is still real:

- there is no provider-neutral schema difference model
- there is no comparer that explains model-vs-database drift
- there is no CLI command for validation reports
- there is no safe diff-script generator
- there is no migration snapshot or applied-migration table

That means the first target should be schema validation and drift reporting, not migration execution.

## Phase Objective

By the end of this phase, DataLinq should be able to answer four questions honestly:

1. Can we compare code metadata to live database metadata for SQLite, MySQL, and MariaDB?
2. Are drift reports specific enough to fix the model or database without reading provider internals?
3. Can DataLinq generate conservative SQL diff scripts without silently destroying data?
4. Have we deliberately deferred versioned migrations until validation and stateless diffing are credible?

## Design Stance

The right stance is:

- build validation before generation
- keep provider-neutral comparison separate from provider-specific SQL syntax
- preserve enough provider detail to explain type/default/index differences honestly
- treat destructive changes as dangerous by default
- make CLI output useful before adding runtime startup hooks
- use the existing metadata readers and SQL generators instead of inventing parallel schema models

The wrong stance would be:

- auto-applying schema changes at runtime before drift detection is proven
- treating SQLite and MySQL DDL as interchangeable
- hiding destructive actions behind friendly wording
- designing snapshot migrations before stateless validation works

## Planned Deliverables

### 1. Schema Difference Model

Deliverables:

- `SchemaDifference` or equivalent provider-neutral result shape
- stable difference kinds for missing/extra tables, columns, indices, relations, defaults, nullability, auto-increment, and type mismatches
- severity or safety classification so reports can distinguish incompatible drift from informational differences
- references back to source and target metadata where practical

### 2. Metadata Normalization Rules

Deliverables:

- explicit normalization for provider type names, lengths, signedness, nullability, default values, and identifier casing
- provider-specific comparison helpers only where provider semantics genuinely differ
- tests that prove equivalent metadata does not produce false-positive drift

### 3. Schema Comparer

Deliverables:

- compare model metadata against live database metadata
- produce deterministic, ordered differences
- support SQLite first, then MySQL/MariaDB
- keep comparison pure enough to unit test without live servers

### 4. Validation CLI

Deliverables:

- a CLI command that loads model metadata and live database metadata
- concise human-readable output
- machine-readable JSON output if it is cheap to add
- non-zero exit code on blocking drift

The command name can be decided during implementation, but `validate` should be the default unless the existing CLI command surface argues otherwise.

### 5. Conservative Diff Script Generation

Deliverables:

- provider-specific SQL generation from validated differences
- additive changes first: missing tables, missing columns, missing non-destructive indices
- destructive or ambiguous changes commented out by default with explicit warnings
- SQLite table-rebuild behavior treated as its own provider-specific problem, not hidden behind generic `ALTER` language

### 6. Migration Snapshot Scoping

Deliverables:

- decide the minimum snapshot format after stateless diffing works
- define how snapshots relate to generated source metadata
- define the applied-migrations table shape, but do not require full execution support in the first validation slice

## Workstreams

## Workstream A: Schema Capability Audit

Goals:

- consume the Phase 4 provider metadata support matrix
- verify the comparer only treats supported metadata fields as authoritative
- identify fields that are comparable now versus fields that remain intentionally unsupported

Tasks:

1. Review the provider metadata roundtrip matrix.
2. Map supported metadata fields into comparer inputs.
3. Define how unsupported fields are ignored, warned about, or surfaced as informational differences.
4. Record any Phase 4 gaps that still block trustworthy validation.

## Workstream B: Difference Model and Pure Comparer

Goals:

- build the provider-neutral drift model
- make comparison deterministic and unit-testable

Tasks:

1. Add difference types and severity/safety classification.
2. Implement table and column presence comparison.
3. Add nullability, auto-increment, type, default, index, and relation comparison in staged slices.
4. Add unit tests using in-memory metadata fixtures.

## Workstream C: Provider Metadata Verification

Goals:

- prove live metadata readers provide enough data for validation
- avoid SQLite-only confidence

Tasks:

1. Add or extend SQLite metadata-reader tests for validation-relevant fields.
2. Add or extend MySQL/MariaDB metadata-reader tests for validation-relevant fields.
3. Document provider-specific fields that are intentionally ignored or normalized.

## Workstream D: CLI Validation Surface

Goals:

- make drift detection usable without writing test code
- keep output actionable and automatable

Tasks:

1. Decide whether the command belongs in `DataLinq.CLI` or a tooling layer.
2. Add connection/model loading options using the existing configuration patterns.
3. Render concise text output.
4. Add JSON output if it does not distort the model.
5. Return process exit codes suitable for CI.

## Workstream E: Diff Script Generation

Goals:

- turn validated differences into conservative SQL suggestions
- avoid destructive automation by default

Tasks:

1. Generate additive SQL for missing tables, columns, and indices.
2. Mark destructive or ambiguous operations as commented warnings.
3. Add provider-specific tests for generated SQL.
4. Keep runtime auto-apply out of the first implementation slice.

## Workstream F: Snapshot Migration Design

Goals:

- define the next phase of migration support without blocking validation

Tasks:

1. Draft snapshot JSON shape.
2. Draft migration identity and applied-migration table shape.
3. Decide how rename detection would be represented.
4. Stop before implementing full migration execution unless validation and diff generation are already solid.

## Proposed Execution Order

1. Consume the Phase 4 metadata support matrix and roundtrip test results.
2. Add the schema difference model.
3. Implement table/column presence comparison with unit tests.
4. Extend comparison to types, nullability, defaults, indices, and relations.
5. Verify against SQLite and MySQL/MariaDB live metadata tests.
6. Add CLI validation output.
7. Add conservative diff-script generation.
8. Scope snapshot migrations as follow-up work.

## Verification Plan

At minimum, each implementation slice should run the focused tests it touches.

Before closing the phase, run:

- `DataLinq.Tests.Unit`
- `DataLinq.Tests.Compliance`
- SQLite metadata-reader tests
- MySQL/MariaDB metadata-reader tests where local provider infrastructure is available
- CLI tests for validation report formatting and exit codes

If local MySQL/MariaDB infrastructure is unavailable, record that honestly.

## Exit Criteria

Phase 5 is complete when:

- code metadata can be compared with live SQLite metadata
- code metadata can be compared with live MySQL/MariaDB metadata
- schema drift reports are deterministic, actionable, and tested
- provider-specific normalization rules are documented in code or tests
- a CLI validation command exists
- conservative diff-script generation exists for additive changes
- destructive or ambiguous changes are clearly marked and not auto-applied by default
- snapshot/versioned migrations are either implemented in a narrow first slice or explicitly deferred with a concrete follow-up plan

## Non-Goals

- runtime auto-migration as the first slice
- full EF-style migration history before drift detection works
- broad provider expansion
- query/runtime optimization
- async query API work
- dependency-tracked result-set caching

## Risks

- false-positive drift reports from provider type aliases or default-value formatting
- false-negative drift reports from over-normalization
- SQLite DDL limitations making generic diff generation misleading
- destructive changes appearing safer than they are
- CLI configuration work expanding beyond validation needs

## First Implementation Slice

The first slice should be Workstream A plus the smallest useful part of Workstream B.

Concrete first step:

1. audit model and live metadata fields
2. define the schema difference model
3. implement missing/extra table and column comparison
4. test the comparer with pure metadata fixtures before touching CLI behavior

That keeps the next phase grounded. If the comparer cannot explain simple drift cleanly, every later migration feature is built on sand.
