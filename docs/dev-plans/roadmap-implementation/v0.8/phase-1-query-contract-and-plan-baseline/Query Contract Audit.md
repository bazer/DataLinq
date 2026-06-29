> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 1 Query Contract Audit

**Status:** Complete Phase 1 baseline.

**Updated:** 2026-06-27.

## Purpose

This audit records the phase-start Remotion-backed query behavior that the 0.8 parser migration had to preserve, reject explicitly, or replace deliberately.

The useful rule is simple: if a query shape is public documentation, support-matrix evidence, or active compliance coverage, Phase 2 and later must treat it as migration contract unless a breaking contraction is recorded intentionally.

## Contract Inventory

| Support area | Evidence | Phase 1 classification | Action |
| --- | --- | --- | --- |
| Scalar equality, inequality, range, enum, and property comparisons | `src/DataLinq.Tests.Compliance/Query/EmployeesQueryBehaviorTests.cs` | Green | Preserve. Do not widen this to arbitrary expression comparison without new tests. |
| Chained predicates and boolean grouping | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesBooleanLogicTests.cs` | Green | Preserve. SQL-shape inspection now proves both chained predicates render in SQL. |
| Local `Contains(...)` membership | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesContainsTranslationTests.cs`, `Translation/EmployeesEmptyListQueryTests.cs` | Green | Preserve arrays, lists, sets, tested `ReadOnlySpan<T>`, projected local sequences, and empty fixed-condition handling. |
| Local `Any(predicate)` membership | `Translation/EmployeesLocalAnyPredicateTests.cs`, `Translation/EmployeesEmptyListQueryTests.cs` | Green | Preserve equality-shaped non-empty local membership and empty fixed-condition short-circuiting. Compound non-empty local predicates remain unsupported. |
| Nullable value and nullable bool predicates | `Translation/EmployeesNullablePredicateTests.cs`, `Translation/EmployeesNullableBooleanTests.cs`, `Translation/EmployeesDateTimeMemberTests.cs` | Green | Preserve lifted nullable semantics, especially `nullable != nonNullable` including null rows. |
| String, date, and time member predicates | `Translation/EmployeesStringMemberTests.cs`, `Translation/EmployeesDateTimeMemberTests.cs` | Green | Preserve the tested member/method set only. |
| Scalar result operators and aggregates | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesAggregateTranslationTests.cs`, `Translation/EmployeesUnsupportedQueryDiagnosticsTests.cs` | Green | Preserve `Count`, `Any`, single-row operators, and direct numeric aggregate selectors. Unsupported aggregate selectors now use LINQ operator names instead of Remotion type names. |
| Projection | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesProjectionTranslationTests.cs` | Green | Preserve post-materialization projection behavior. Do not accidentally turn this into a SQL `SELECT`-list promise. |
| Relation predicates | `Translation/EmployeesRelationPredicateTranslationTests.cs` | Green | Preserve one-to-many `Any(...)`, negated `Any(...)`, existence-equivalent `Count()` comparisons, direct related-row comparisons, and simple grouped relation predicates. SQL-shape inspection now verifies correlated `EXISTS`/`NOT EXISTS`. |
| Explicit inner join | `Translation/EmployeesJoinTranslationTests.cs` | Green but narrow | Preserve one direct inner `Join(...)` between two DataLinq query sources with direct member keys and row-local result projection. Keep composite keys, `GroupJoin`, relation projections, and post-join operators unsupported until later phases deliberately expand joins. |
| Ordering and paging | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesUnsupportedQueryDiagnosticsTests.cs` | Green for ordering-before-paging; unsupported for post-paging operators | Preserve `Where/OrderBy/ThenBy/Skip/Take` when filters and ordering happen before paging. Operators after `Skip(...)` or `Take(...)` now throw `QueryTranslationException` because correct behavior requires subquery pushdown. |
| Unsupported operators and shapes | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesUnsupportedQueryDiagnosticsTests.cs`, `Translation/EmployeesJoinTranslationTests.cs`, `Translation/EmployeesRelationPredicateTranslationTests.cs` | Green | Preserve explicit failures for unsupported local predicates, relation traversal, relation projections, computed aggregate selectors, `GroupBy`, `GroupJoin`, composite joins, and post-paging filters/orderings. |

## Public Claims That Must Not Quietly Contract

These support claims are part of the 0.8 parser parity contract unless a release note deliberately records a breaking contraction:

- chained `Where(...)` predicates and grouped `&&`, `||`, and `!`
- local collection membership over arrays, `List<T>`, `HashSet<T>`, the tested `ReadOnlySpan<T>` shape, and safe local projections
- fixed true/false handling for empty local `Contains(...)`, `Any()`, and `Any(predicate)`
- nullable value and nullable bool semantics, including C# lifted inequality behavior
- documented string/date/time member predicates
- `Count`, `Count(predicate)`, `Any`, `Any(predicate)`, `Where(...).Any()`, single-row result operators, and direct numeric scalar aggregates
- post-materialization entity, scalar, anonymous, and row-local computed projections
- one-to-many relation `Any(...)` and existence-equivalent relation `Count()` comparisons
- the current narrow explicit inner `Join(...)` baseline
- ordering-before-paging with deterministic `OrderBy(...).Skip(...).Take(...)`

## Remotion Dependency Inventory

| Category | Active references | Phase 7 expectation |
| --- | --- | --- |
| Main product package dependency | `src/DataLinq/DataLinq.csproj`, `src/Directory.Packages.props` | Remove `Remotion.Linq` from the main runtime graph after parser parity is proven. |
| AOT/trim roots | `src/DataLinq.AotSmoke/DataLinq.AotSmoke.csproj`, `src/DataLinq.TrimSmoke/DataLinq.TrimSmoke.csproj` | Remove roots when constrained publish paths no longer need Remotion. |
| Queryable/parser entry | `src/DataLinq/Linq/Queryable.cs` | Replace `QueryableBase<T>` and `QueryParser.CreateDefault()` with a DataLinq-owned queryable/provider boundary. |
| Query execution and SQL construction | `src/DataLinq/Linq/QueryExecutor.cs`, `src/DataLinq/Query/SqlQuery.cs`, `src/DataLinq/Linq/QueryBuilder.cs`, `src/DataLinq/Linq/Visitors/WhereVisitor.cs`, `src/DataLinq/Linq/Visitors/OrderByVisitor.cs` | Move SQL generation and diagnostics to DataLinq plan nodes before deleting Remotion. |
| Local sequence and projection helpers | `src/DataLinq/Linq/LocalSequenceExtractor.cs`, `src/DataLinq/Linq/ProjectionExpressionEvaluator.cs`, `src/DataLinq/Linq/Evaluator.cs` | Rebuild local sequence extraction and projection binding without Remotion query-source identities. |
| Migration-only test scaffolding | `src/DataLinq.Tests.Compliance/Translation/CurrentQueryTranslationInspection.cs` | Delete or replace in Phase 7. This is the only active compliance-test helper that instantiates `QueryParser` and reflects into `ParseQueryModel`. |
| Documentation references | Public internals/platform docs and roadmap/dev-plan sources | Update when behavior ships; do not remove honest current-state caveats before dependency removal is real. |

## Diagnostics Inventory

Cleaned in Phase 1:

- unsupported aggregate-selector diagnostics use `Sum`, `Min`, `Max`, or `Average` style operator names rather than Remotion result-operator type names
- unsupported collection `GroupBy(...)` now throws `QueryTranslationException` instead of falling into undefined collection materialization
- post-paging filters/orderings now throw a specific subquery-pushdown diagnostic

Remaining Remotion-shaped diagnostics to fix before the new parser becomes default:

- several unsupported paths still include `Query model:` or Remotion-shaped subquery text for maintainer context
- relation-subquery and predicate-subquery failures can still include Remotion clause/rendering details
- join diagnostics still mention the phase-start Remotion query-model shape where that is the only available context

## Phase 2 Handoff

Plan snapshots should start with these shapes:

- chained `Where(a).Where(b)` and `Where(a).OrderBy(...).Where(b)`
- empty fixed conditions rendering `1=0` and `1=1`
- local equality-membership `Any(predicate)` rendering `IN (...)`
- nullable inequality against non-null values
- relation `Any(...)` and negated `Any(...)` rendering correlated `EXISTS`
- existence-equivalent relation `Count()` comparisons
- scalar aggregates over direct numeric and nullable numeric members
- post-materialization projection over filtered/ordered/paged rows
- the current narrow explicit inner join with direct member keys
- explicit rejection of post-paging filters/orderings until subquery pushdown exists

Known behavior decisions at Phase 1 closeout:

- operators after `Skip(...)` or `Take(...)` are not part of the support contract and now fail explicitly
- `TakeLast`, `SkipLast`, `TakeWhile`, and `SkipWhile` fail with `QueryTranslationException`
- relation/property projections, nested database subqueries, `GroupBy`, `GroupJoin`, composite joins, and computed aggregate selectors remain unsupported

Later 0.8 phases supersede two of those entries: Phase 13 implements supported single-source post-paging pushdown, and Phase 13B begins the narrow SQL-backed grouped aggregate projection slice. Treat this section as Phase 1 historical baseline, not the current support matrix.

## Focused Verification

These focused filters passed on 2026-06-27 across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`:

- `EmployeesContainsTranslationTests`
- `EmployeesLocalAnyPredicateTests`
- `EmployeesRelationPredicateTranslationTests`
- `EmployeesUnsupportedQueryDiagnosticsTests`
- `EmployeesQueryBehaviorTests`
- `CharPredicateTranslationTests`

Phase closeout verification also passed:

- compliance quick: 466/466 on `sqlite-file` and `sqlite-memory`
- MySQL lane: 152/152 on `mysql-8.4` and `mariadb-11.8`
- `docfx docfx.json`: succeeded with only the pre-existing duplicate analyzer release-note warnings
