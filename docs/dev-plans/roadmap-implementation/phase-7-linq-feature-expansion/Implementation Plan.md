> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Phase 7 Implementation Plan: LINQ Feature Expansion

## Purpose

Phase 6 made the existing LINQ translator support boundary explicit, fixed correctness gaps, and added `QueryTranslationException` diagnostics. Phase 7 should now add the next practical query features users will expect from a small ORM without turning this into a broad provider rewrite.

## Goals

- support simple scalar aggregates over translated query predicates
- support computed projections where the execution semantics are clear
- make nullable predicate support explicit, documented, and regression-tested
- add a narrow LINQ `Join(...)` baseline
- add relation-aware predicate translation over generated relation properties

## Non-Goals

- arbitrary `GroupBy(...)`
- arbitrary client method translation
- provider-independent query IR rewrite
- broad LINQ outer join and `GroupJoin(...)` support in the first slice
- hidden client-side predicate fallback after SQL filtering
- eager-loading API design beyond what relation-aware predicates require

## Workstream A: Scalar Aggregates

Goals:

- add high-value aggregate support without grouping semantics
- keep SQL generation boring and provider-backed

Tasks:

1. Add compliance tests for `Sum`, `Min`, `Max`, and `Average` over simple numeric member selectors.
2. Cover filtered aggregates, e.g. `.Where(...).Sum(x => x.Value)`.
3. Define behavior for nullable numeric selectors and empty sequences before implementation.
4. Extend scalar result-operator handling in `QueryExecutor`.
5. Update docs to list aggregate support narrowly.

Initial boundary:

- supported: aggregate selector is a direct member or nullable `.Value` member already understood by the translator
- not supported yet: aggregate over computed selector, grouped aggregate, relation aggregate

Status: Complete. Workstream A adds SQL-backed `Sum`, `Min`, `Max`, and `Average` for direct numeric members, nullable numeric members, nullable `.Value` selectors, and filtered sequences. Empty filtered sequences follow documented LINQ aggregate behavior for the tested supported forms, and computed aggregate selectors remain rejected with `QueryTranslationException`.

## Workstream B: Projection Expansion

Goals:

- make ordinary projection shapes useful
- avoid accidentally promising arbitrary client execution inside SQL

Tasks:

1. Add tests for computed anonymous projections such as string concatenation and supported member chains.
2. Decide which projections are SQL-backed and which are safe post-materialization projections.
3. Preserve SQL-side filtering, ordering, and paging semantics before applying any client projection.
4. Add targeted `QueryTranslationException` cases for rejected selector shapes.
5. Update `Supported LINQ Queries.md` with explicit examples.

Design note:

- SQL-backed projections are better for data volume, but post-materialization projection may be acceptable for shapes that already selected full rows. The docs must be blunt about which path each shape uses.

Status: Complete. Workstream B supports row-local computed scalar and anonymous projections as post-materialization projections after SQL filtering, ordering, and paging. Relation-property projections and nested subquery projections remain rejected with targeted `QueryTranslationException` diagnostics so projection support does not hide N+1 relation traversal or database subqueries.

## Workstream C: Nullable Predicate Polish

Goals:

- turn partially proven nullable behavior into a documented support boundary
- avoid surprises around `.HasValue`, `.Value`, `null`, and lifted operators

Tasks:

1. Add focused tests for `.HasValue`, `!HasValue`, `.Value` comparisons, and mixed nullable/non-nullable equality.
2. Cover nullable member access for date/time predicates already supported through `.Value`.
3. Confirm SQL null semantics match C# semantics for tested predicates.
4. Update the support matrix and user docs.

Status: Complete. Workstream C adds focused nullable predicate coverage for `.HasValue`, `!HasValue`, guarded `.Value` comparisons, guarded nullable date/time member access, and mixed nullable/non-nullable equality and inequality. `nullable != nonNullable` now emits SQL that includes null rows, matching C# lifted nullable semantics for the documented shape.

## Workstream D: Explicit LINQ `Join(...)` Baseline

Goals:

- support the explicit join shape users expect
- reuse the lower-level SQL join builder where practical

Tasks:

1. Add tests for a narrow inner join over simple equality keys.
2. Support simple result projections from both sides.
3. Define aliasing rules and collision handling.
4. Reject composite keys, outer joins, `GroupJoin(...)`, and complex result selectors with `QueryTranslationException` until designed.
5. Verify SQLite and MariaDB SQL behavior.

Initial boundary:

- supported: `outer.Join(inner, outerKey, innerKey, (outer, inner) => new { ... })` with direct member keys or nullable `.Value` keys
- not supported yet: left join, group join, composite anonymous-object keys, relation-property joins, additional filtering/ordering/paging over joined results

Status: Complete. Workstream D adds a narrow SQL-backed explicit inner `Join(...)` baseline for one outer DataLinq query source and one inner DataLinq query source. The SQL join selects primary keys from both sides with internal aliases, materializes both rows through DataLinq caches, and applies the result selector post-materialization. Composite keys, `GroupJoin(...)`, filtering over joined results, and relation-property result projections now have focused `QueryTranslationException` coverage.

## Workstream E: Relation-Aware Predicate Translation

Goals:

- make generated relation properties useful inside predicates
- avoid N+1 relation access when a predicate can become SQL

Tasks:

1. Write a design note before implementation.
2. Add tests for `parent.Relation.Any(child => child.Column == value)` and `parent.Relation.Count() > 0`-style scenarios if supported.
3. Decide between `EXISTS` and join-backed SQL for one-to-many relation predicates.
4. Define behavior for one-to-one and many-to-one relations separately.
5. Integrate relation metadata, table aliases, and provider SQL generation carefully.
6. Keep relation materialization/cache behavior separate from predicate translation.

Preferred first implementation:

- translate one-to-many `Any(predicate)` into `EXISTS` using relation metadata
- reject relation projections and relation aggregates until separately designed

## Verification Plan

At minimum, each workstream should run:

- `run --suite unit --alias quick --output failures --build`
- `run --suite compliance --alias quick --output failures --build`
- `run --suite compliance --targets mariadb-11.8 --output failures --build`
- `docfx docfx.json` when docs change

Before closing the phase, run the broadest available server-backed compliance matrix, including MySQL 8.4 and the current MariaDB targets when available.

## Exit Criteria

Phase 7 is complete when:

- scalar aggregates work for the documented simple selector boundary
- computed projections have an explicit SQL-backed or post-materialization execution model
- nullable predicate support is documented and covered by active tests
- a narrow explicit LINQ `Join(...)` shape is supported or consciously deferred with design notes
- relation-aware predicate translation has a tested first slice and a defensible design boundary
- unsupported query shapes continue to fail with useful `QueryTranslationException` messages
