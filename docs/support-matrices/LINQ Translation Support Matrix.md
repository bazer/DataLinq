> [!NOTE]
> This matrix is maintenance evidence for LINQ translator coverage. For the shorter user-facing contract, start with [Supported LINQ Queries](../Supported%20LINQ%20Queries.md).

# LINQ Translation Support Matrix

**Status:** Current test-backed LINQ translator baseline; update whenever LINQ translator support changes.

This matrix records what the active compliance tests prove today, where the public support docs are accurate, and which gaps remain outside the documented support boundary.

The evidence column intentionally points at test files instead of implementation files. If a shape is not represented in active tests, treat it as unsupported or at least undocumented until a focused regression test proves otherwise.

## 0.8 Parser Migration Status

The current 0.8 branch routes production queries through DataLinq's expression parser and query-plan SQL renderer. The historical parser migration baseline is tracked in the source-only audit file at `docs/dev-plans/roadmap-implementation/v0.8/phase-1-query-contract-and-plan-baseline/Query Contract Audit.md`.

The audit does not expand the public contract. It records the historical Remotion-backed behavior that the DataLinq expression parser had to preserve or reject deliberately. Phase 7 removed the migration-only Remotion parser dependencies from the active runtime and test baseline. Remaining support claims should be backed by active DataLinq parser tests, not by historical Remotion parity notes.

## Predicate Translation

| Area | Currently tested support | Evidence | Audit notes |
| --- | --- | --- | --- |
| Scalar equality and inequality | `==`, `!=`, reversed constant/member equality, missing-row filters, chained `Where(a).Where(b)` | `src/DataLinq.Tests.Compliance/Query/EmployeesQueryBehaviorTests.cs` | Chained `Where(a).Where(b)` is now separately covered across collection, scalar, single-row, and paging result shapes. |
| Range comparison | `>`, `>=`, `<`, `<=` against constants and selected member expressions | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesDateTimeMemberTests.cs` | Public docs match this. |
| Enum comparison | enum equality, inequality, negated enum equality | `EmployeesQueryBehaviorTests.cs` | Public docs match this. |
| Property-to-property comparison | equality, inequality, greater-than, less-than-or-equal comparisons between translated operands | `EmployeesQueryBehaviorTests.cs` | Public docs mention this, but the coverage is narrow and should not be generalized to arbitrary expressions. |
| Boolean grouping | `&&`, `||`, `!`, nested grouped predicates, De Morgan-style groups | `Translation/EmployeesBooleanLogicTests.cs`, `EmployeesQueryBehaviorTests.cs` | Public docs match this at a high level. Fixed true/false conditions now have dedicated regression coverage. |
| Nullable boolean predicates | nullable bool compared to `true`, `false`, and `null`; negated equality forms | `Translation/EmployeesNullableBooleanTests.cs` | Public docs match this. |
| Nullable value predicates | `.HasValue`, `!HasValue`, guarded `.Value` comparisons, selected guarded date/time member access, and mixed nullable/non-nullable equality and inequality | `Translation/EmployeesNullablePredicateTests.cs`, `Translation/EmployeesDateTimeMemberTests.cs` | The support boundary is intentionally documented. `nullable != nonNullable` includes null rows to match C# lifted nullable semantics. |
| Character predicates | LINQ char predicate matches raw SQL parameter behavior | `Translation/CharPredicateTranslationTests.cs`, `Query/SQLiteInMemoryBehaviorTests.cs` | Public docs did not call this out; it is a narrow parameter/type-fidelity case. |

## Relation Predicate Translation

| Area | Currently tested support | Evidence | Audit notes |
| --- | --- | --- | --- |
| One-to-many relation existence | generated collection relation `Any()`, `Any(predicate)`, negated `Any(predicate)`, and existence-equivalent `Count()` comparisons | `Translation/EmployeesRelationPredicateTranslationTests.cs` | These translate to correlated `EXISTS` subqueries. `Count()` support is deliberately limited to forms reducible to existence or non-existence. |
| Related-row predicate body | direct related-row member comparisons against local values, plus simple `&&` and `||` groups | `Translation/EmployeesRelationPredicateTranslationTests.cs` | This is not arbitrary predicate translation for a second query source. Relation traversal from the related row remains rejected. |
| Singular implicit relation traversal | root-row singular relation member access in `Where`, `OrderBy`, `ThenBy`, and direct `Select(...)` projection, rendered as an implicit inner join and reused for repeated relation references | `Translation/EmployeesImplicitRelationJoinTests.cs`, `Translation/EmployeesProjectionTranslationTests.cs`, `Translation/QueryPlanSnapshotTests.cs` | This is SQL-backed inner-join traversal. Relation object projection, collection relation projection, multi-hop traversal, left-join null semantics, and fluent relation-aware join APIs remain outside the shipped boundary. |
| Unsupported relation predicate/projection shapes | relation traversal inside the related-row predicate, unsupported `Count()` thresholds, relation object projection, and collection relation projection fail with `QueryTranslationException` | `Translation/EmployeesRelationPredicateTranslationTests.cs`, `Translation/QueryPlanUnsupportedShapeTests.cs` | Collection traversal beyond the documented existence patterns and relation aggregates beyond existence-equivalent `Count()` forms remain outside the documented boundary. |

## Local Collections and Fixed Conditions

| Area | Currently tested support | Evidence | Audit notes |
| --- | --- | --- | --- |
| `Contains` over local arrays | `ids.Contains(row.Column)` and negated form translate as membership predicates | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesContainsTranslationTests.cs` | Public docs match this. |
| `Contains` over `List<T>` and `HashSet<T>` | list/set membership over local values | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesEmptyListQueryTests.cs` | Public docs match this. |
| `Contains` over `ReadOnlySpan<T>` | span-backed membership over local arrays | `Translation/EmployeesContainsTranslationTests.cs` | Public docs understated this. Keep the claim narrow: this proves the tested span shape, not arbitrary span pipelines. |
| Multiple collection predicates | combined local collection membership with `&&`, additional string predicates, and range predicates | `EmployeesQueryBehaviorTests.cs` | Useful coverage for local-sequence regression safety. |
| Empty local `Contains` | false fixed condition, negated true condition, direct composition, nested `AND`/`OR`, negated groups, and unsupported item expressions skipped when the empty list already determines the result | `Translation/EmployeesContainsTranslationTests.cs`, `Translation/EmployeesEmptyListQueryTests.cs`, `Translation/EmployeesBooleanLogicTests.cs` | The fixed-condition truth table and grouping behavior are covered by focused tests. |
| Constant-item `Contains` | constant true and constant false predicates collapse to fixed conditions | `Translation/EmployeesContainsTranslationTests.cs` | Public docs did not call this out. This is implementation behavior worth preserving. |
| Empty local `Any(predicate)` | false fixed condition, negated true condition, direct composition, nested `AND`/`OR`, negated groups; complex or otherwise unsupported predicate bodies are ignored when the sequence is empty | `Translation/EmployeesEmptyListQueryTests.cs`, `Translation/EmployeesBooleanLogicTests.cs` | Public docs distinguish empty fixed-condition behavior from non-empty equality membership. |
| Projected local `Contains` | `localObjects.Select(x => x.Value).Contains(row.NullableColumn.Value)` and empty projected local sequences | `Translation/EmployeesContainsTranslationTests.cs` | Guarded local sequence extraction is tested for safe local projections that do not reference the database query source. |
| Local object-list `Any(predicate)` | scalar item equality, object-member equality, reversed equality, nullable wrapper normalization, and negated `NOT IN` membership | `Translation/EmployeesEmptyListQueryTests.cs`, `Translation/EmployeesLocalAnyPredicateTests.cs` | Equality-membership shapes are covered. Compound non-empty local predicates remain unsupported. |

### Fixed-Condition Truth Table

These shapes intentionally collapse to fixed SQL predicates instead of generating invalid SQL or visiting predicate bodies that do not matter.

| Query shape | Fixed condition | SQL predicate | Evidence |
| --- | --- | --- | --- |
| `empty.Contains(value)` | false | `1=0` | `Translation/EmployeesContainsTranslationTests.cs`, `Translation/EmployeesEmptyListQueryTests.cs` |
| `!empty.Contains(value)` | true | `1=1` | `Translation/EmployeesContainsTranslationTests.cs`, `Translation/EmployeesEmptyListQueryTests.cs` |
| `empty.Any()` | false | `1=0` | `Translation/EmployeesEmptyListQueryTests.cs` |
| `!empty.Any()` | true | `1=1` | `Translation/EmployeesEmptyListQueryTests.cs` |
| `empty.Any(predicate)` | false without visiting `predicate` | `1=0` | `Translation/EmployeesEmptyListQueryTests.cs`, `Translation/EmployeesBooleanLogicTests.cs` |
| `!empty.Any(predicate)` | true without visiting `predicate` | `1=1` | `Translation/EmployeesEmptyListQueryTests.cs`, `Translation/EmployeesBooleanLogicTests.cs` |
| constant `local.Contains(item)` when the item is present | true | `1=1` | `Translation/EmployeesContainsTranslationTests.cs` |
| constant `local.Contains(item)` when the item is absent | false | `1=0` | `Translation/EmployeesContainsTranslationTests.cs` |

## Member and Method Translation

| Area | Currently tested support | Evidence | Audit notes |
| --- | --- | --- | --- |
| String prefix/suffix search | `StartsWith`, `EndsWith`, negated prefix/suffix predicates | `EmployeesQueryBehaviorTests.cs` | Public docs match this. |
| String content search | string `Contains` | `Translation/EmployeesStringMemberTests.cs` | Public docs match this. |
| String casing | `ToUpper`, `ToLower` | `Translation/EmployeesStringMemberTests.cs` | Public docs match this. |
| String trimming and length | `Trim`, `Length`, `Trim().Length` | `Translation/EmployeesStringMemberTests.cs` | Public docs match this. |
| String substring | `Substring(...)` in tested argument shapes | `Translation/EmployeesStringMemberTests.cs` | Public docs should stay conservative on overload breadth. |
| String null/whitespace checks | `string.IsNullOrEmpty`, `string.IsNullOrWhiteSpace` in true and false forms | `Translation/EmployeesStringMemberTests.cs` | Public docs match this. |
| DateOnly members | `Year`, `Month`, `Day`, `DayOfYear`, `DayOfWeek` | `Translation/EmployeesDateTimeMemberTests.cs` | Public docs match this. |
| TimeOnly members | `Hour` behind nullable guard | `Translation/EmployeesDateTimeMemberTests.cs` | Public docs match this at member level. |
| DateTime members | `Minute`, `Second`, `Millisecond` behind nullable guard | `Translation/EmployeesDateTimeMemberTests.cs` | Public docs match this at member level. |
| Provider-specific function semantics | SQLite, MySQL, and MariaDB behavior is exercised through active provider data sources | all active-provider compliance tests | The matrix should not promise identical SQL text across providers. It only promises equivalent behavior for the tested rows. |

## Result Operators

| Area | Currently tested support | Evidence | Audit notes |
| --- | --- | --- | --- |
| Materialization and counts | `ToList()`, `Count()`, `Count(predicate)` | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesDateTimeMemberTests.cs` | Public docs list `Count()` but should distinguish predicate count coverage. |
| Existence | `Any()`, `Any(predicate)`, and `Where(...).Any()` | `EmployeesQueryBehaviorTests.cs` | Public docs list `Any()` but should distinguish predicate coverage from local `Enumerable.Any(predicate)` translation. |
| Single-row operators | `Single(predicate)`, `SingleOrDefault(predicate)`, `First(predicate)`, `FirstOrDefault(predicate)` | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesStringMemberTests.cs` | Public docs match this. |
| Last-row operators | `Last()`, `LastOrDefault(predicate)` in ordered scenarios | `EmployeesQueryBehaviorTests.cs` | Public docs match this but should keep the existing advice to order explicitly. |
| Unsupported tail/while operators | `TakeLast`, `SkipLast`, `TakeWhile`, `SkipWhile` throw `NotSupportedException` | `EmployeesQueryBehaviorTests.cs` | Public docs match this. |
| Scalar aggregates | `Sum`, `Min`, `Max`, `Average` over direct numeric members, nullable numeric members, nullable `.Value`, and filtered sequences | `Translation/EmployeesAggregateTranslationTests.cs` | The boundary is narrow: no computed selector aggregates or relation-property aggregates. Grouped aggregate projection is tracked separately below. |

## Ordering, Paging, and Projection

| Area | Currently tested support | Evidence | Audit notes |
| --- | --- | --- | --- |
| Ordering | `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, mixed ascending/descending ordering | `EmployeesQueryBehaviorTests.cs` | Public docs match this. |
| Paging | `Skip`, `Take`, `Skip(...).Take(...)` with ordered queries, composed chained predicates, post-paging filters/orderings, and `Count()`/`Any()` over paged sources | `EmployeesQueryBehaviorTests.cs`, `Translation/QueryPlanSqlParityTests.cs`, `Translation/QueryPlanSnapshotTests.cs` | Chained-filter paging has focused regression coverage. Phase 13 adds single-source subquery pushdown when later filters, orderings, or scalar reductions must apply over an already-paged source. |
| Ordering plus filtering | `Where(...).OrderBy(...)`, `OrderBy(...).Where(...)`, and `Where(...).OrderBy(...).Where(...)` | `EmployeesQueryBehaviorTests.cs` | Focused regression coverage proves the outer predicate is preserved after an inner ordering clause. |
| Full-model projection | selecting the model entity | `EmployeesQueryBehaviorTests.cs` | Public docs match this. |
| Scalar projection | `Select(x => x.Property)` and supported singular relation member scalar projection | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesProjectionTranslationTests.cs`, `Translation/EmployeesImplicitRelationJoinTests.cs` | Direct source-slot scalar projections are SQL-backed and read from aliased result values. |
| Anonymous projection | `Select(x => new { ... })` for direct source-slot members, supported singular relation members, and row-local computed expressions | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesProjectionTranslationTests.cs`, `Translation/QueryPlanSnapshotTests.cs` | Direct source-slot projection rows are SQL-backed. Computed anonymous projections remain row-local after materialization. Relation object and collection relation projections remain rejected to avoid hidden N+1 behavior. |
| Computed scalar projection | row-local string concatenation and materialized member chains after SQL filtering, ordering, and paging | `Translation/EmployeesProjectionTranslationTests.cs` | Client projection is deliberate here. Do not generalize this to SQL predicate method translation. |
| Grouped aggregate projection and composition | SQL-backed grouped aggregate rows with direct, composite, and SQL-renderable computed keys; `group.Key` for scalar keys; `group.Key.Member` for composite keys; narrow grouped `HAVING`; ordering/paging/filtering over projected grouped key or aggregate members; grouped-row `Any()`/`Count()`; supported explicit joined projections and implicit singular relation traversal as grouping inputs; constructor-backed DTO/record projections; `db.Query()` and `transaction.Query()` roots across active providers | `Translation/EmployeesGroupedAggregateTranslationTests.cs`, `Translation/QueryPlanSnapshotTests.cs`, `Translation/ExpressionQueryPlanParserTests.cs` | This is SQL-backed grouped aggregate row materialization and composition, not materialized `IGrouping<TKey,TElement>` support. Whole composite `group.Key` projection, client-computed keys, computed aggregate selectors, collection relation grouping, grouped element enumeration, and non-bindable grouped-row composition remain rejected. |
| Views and primary-key lookup | querying generated views and direct `Get<T>(key)` lookup | `SeededEmployeesQueryTests.cs`, `EmployeesQueryBehaviorTests.cs` | Direct lookup is not a LINQ predicate but belongs in the surrounding query support docs. |

## Explicit Joins

| Area | Currently tested support | Evidence | Audit notes |
| --- | --- | --- | --- |
| Inner `Join(...)` and single query-syntax inner join | one explicit inner join between two direct DataLinq query sources, direct member equality keys, nullable `.Value` key normalization, SQL-backed direct source-slot result projection from both sides, single query-syntax transparent-identifier binding, composed `Where`, ordering, paging, `Any`, and `Count` over projected members, and post-paging `Where`/ordering/`Any`/`Count` over SQL-backed joined projection rows | `Translation/EmployeesJoinTranslationTests.cs`, `Translation/QueryPlanSnapshotTests.cs`, `Translation/QueryPlanUnsupportedShapeTests.cs`, `Translation/QueryPlanSqlParityTests.cs` | SQL-backed joined projection rows read aliases directly when every result member maps to a source-slot value. Joined pushdown uses a derived-source boundary so later predicates/orderings bind to derived projection aliases instead of flattening past paging. |
| Unsupported join shapes | composite anonymous-object keys, `GroupJoin(...)`, relation-property joins, relation object/collection relation result projection, opaque transparent identifiers, row-local joined post-paging composition, and non-bindable joined projection composition fail with `QueryTranslationException` | `Translation/EmployeesJoinTranslationTests.cs`, `Translation/QueryPlanUnsupportedShapeTests.cs` | Outer joins, multi-join pipelines, scalar aggregates beyond joined `Any`/`Count`, and query syntax that projects whole source entities are outside the documented boundary. |

## Unsupported or Not Yet Proven

These shapes are intentionally not part of the documented support boundary today:

- broad `GroupBy(...)` beyond the SQL-backed grouped aggregate row shapes documented above
- `GroupJoin(...)`, outer joins, composite-key joins, multi-join pipelines, opaque query-syntax transparent identifiers, and row-local post-paging composition over explicit joined results
- relation-property query expansion beyond documented one-to-many existence predicates and the documented singular implicit predicate/ordering/projection traversal
- aggregate result operators over computed selectors, relation properties, or grouped aggregate shapes outside the documented direct numeric selector boundary
- additional body clauses over pushed-down projections, joins, or grouped sources
- arbitrary local `Enumerable` method chains inside predicates
- arbitrary client methods inside SQL predicates
- nested database subqueries
- relation object or collection relation projections inside provider `Select(...)`
- nested database subqueries inside provider `Select(...)`

Unsupported predicate methods, non-empty compound local `Any(predicate)` shapes, unsupported selectors, and unsupported scalar result operators now have focused `QueryTranslationException` coverage in `Translation/EmployeesUnsupportedQueryDiagnosticsTests.cs`.

## Regression Coverage Notes

The regression suite includes tests that make dropped predicates hard to miss:

1. `Where(a).Where(b)` where each predicate excludes different seeded rows and the combined result is strictly smaller than either single predicate.
2. `Where(a).OrderBy(...).Where(b)` to prove ordering does not hide or drop later predicates.
3. `Where(a).Where(b).Count()`, `.Any()`, `.First()`, and `.SingleOrDefault()` to prove result operators apply after both predicates.
4. `Where(a).Where(b).Skip(...).Take(...)` over a deterministic ordering to prove paging is applied after the composed filter.

The central `CurrentQueryTranslationInspection` helper is now a DataLinq-only SQL inspection surface for these cases. It builds plans through `ExpressionQueryPlanParser` and renders SQL through `QueryPlanSqlBuilder`.

Phase 13 adds pushdown-specific regression coverage:

1. Plan snapshots record an explicit `pushdown` operation instead of hiding the nested boundary in SQL text.
2. SQL-shape tests prove that post-paging composition renders `FROM (SELECT ...)` and keeps inner and outer parameters separate.
3. Provider behavior tests compare post-paging filter/order results with in-memory LINQ composition across SQLite, MySQL, and MariaDB.
4. Transaction-root tests prove the same pushed-down shape executes from `transaction.Query()`.

Phase 13B adds grouped aggregate regression coverage:

1. Plan snapshots record `group-by`, `group-key`, and `grouped-aggregate(count)` nodes.
2. SQL-shape tests prove the renderer emits explicit `GROUP BY` plus `COUNT(*)`.
3. Provider behavior tests compare grouped count rows with in-memory LINQ across SQLite, MySQL, and MariaDB.
4. Transaction-root tests prove grouped aggregate projection executes from `transaction.Query()`.
5. Unsupported-shape tests keep bare `GroupBy`, computed keys, composite keys, grouped element enumeration, and unsupported grouped aggregates outside the documented boundary.

Phase 14 adds explicit-join composition coverage:

1. Joined result predicates and orderings bind projected members back to source-slot columns.
2. SQL-shape coverage proves composed joined queries render `JOIN`, `WHERE`, and joined source aliases.
3. Provider behavior tests compare joined `Where`, ordering, paging, `Any`, and `Count` with in-memory LINQ across SQLite, MySQL, and MariaDB.
4. Transaction-root tests prove composed joined projections hydrate after buffering joined primary keys, avoiding nested reader use on transaction connections.
5. Unsupported-shape tests kept post-paging joined composition outside the documented boundary until Phase 21 added the deliberate derived-source design for SQL-backed joined rows.

Phase 15 adds implicit singular relation join coverage:

1. Relation member predicates and orderings bind to a generated implicit inner join.
2. Plan snapshots record an `implicit-join` source and prove repeated relation access reuses that source.
3. Provider behavior tests compare implicit relation filtering/ordering with in-memory relation traversal across SQLite, MySQL, and MariaDB.
4. Transaction-root tests prove the same implicit relation traversal executes from `transaction.Query()`.
5. Unsupported projection tests keep relation traversal out of provider `Select(...)` until a projection design exists.

Phase 16 adds grouped numeric aggregate coverage:

1. Grouped aggregate plan values record selector-bearing `sum`, `min`, `max`, and `average` members while keeping `count` selectorless.
2. SQL-shape tests prove grouped projections emit explicit `GROUP BY`, `COUNT(*)`, `SUM`, `MIN`, `MAX`, and `AVG`.
3. Provider behavior tests compare direct numeric and nullable numeric grouped aggregate rows with in-memory LINQ across SQLite, MySQL, and MariaDB.
4. Transaction-root tests prove grouped numeric aggregate projection executes from `transaction.Query()`.
5. Unsupported-shape tests keep computed grouped aggregate selectors outside the documented boundary.

Phase 17 adds grouped row composition and `HAVING` coverage:

1. Grouped predicates over `group.Key`, `group.Count()`, and supported grouped numeric aggregates render as `HAVING`.
2. Post-projection filters and orderings bind grouped row members back to first-class group-key or aggregate values.
3. SQL-shape tests prove grouped ordering/paging and derived grouped scalar reductions.
4. Provider behavior tests compare grouped filtering, ordering, paging, `Any()`, and `Count()` with in-memory LINQ across SQLite, MySQL, and MariaDB.
5. Transaction-root tests prove supported grouped composition executes from `transaction.Query()`.
6. Unsupported-shape tests keep grouped element enumeration, unsupported grouped predicates, and terminal operators other than grouped-row `Any()`/`Count()` outside the documented boundary.

Phase 18 adds advanced grouped key and joined grouping coverage:

1. Composite anonymous-object keys record named key members and bind `group.Key.Member` explicitly.
2. SQL-renderable computed keys reuse existing function plan values, with provider SQL-shape coverage proving each key appears in `GROUP BY`.
3. Enum, nullable numeric, and string-function keys have provider behavior tests.
4. Explicit joined projections can be grouped when key members and aggregate selectors map back to source-slot values.
5. Implicit singular relation traversal can be used as a grouped key through the existing SQL-backed inner-join path.
6. Constructor-backed grouped projections are covered through a record projection test.
7. Unsupported diagnostics keep whole composite `group.Key` projection, client-computed keys, grouped element enumeration, and post-paging grouped composition outside the documented boundary.

Phase 19 adds SQL-backed projection row coverage:

1. Direct scalar and anonymous source-slot projections read aliased SQL values from the data reader.
2. Supported singular relation member projection reuses the implicit inner-join source-slot path and does not lazy-load relation properties.
3. Explicit joined direct projection rows are SQL-backed when every projected member maps to a source-slot value.
4. Transaction-root tests prove SQL-backed projection rows work from `transaction.Query()`.
5. Snapshot tests record `sql-row` projection shape and implicit relation join sources.
6. Unsupported diagnostics keep relation object projection, collection relation projection, nested provider queries, multi-hop traversal, and client methods outside the documented boundary.

Phase 20 adds single query-syntax inner join coverage:

1. Compiler-generated transparent identifiers bind back to DataLinq source slots for one inner join.
2. Query-syntax `where`, `orderby`, paging, `Any()`, and `Count()` over joined rows reuse the explicit join composition model.
3. Query-syntax `select new { ... }` rows materialize through SQL-backed projection aliases when every member maps to a source-slot value.
4. Provider behavior tests compare query-syntax joins with in-memory LINQ across SQLite, MySQL, and MariaDB.
5. Transaction-root tests prove supported query-syntax joins execute from `transaction.Query()`.
6. Snapshot tests record query-syntax joins as ordinary source-slot joins with SQL-row projection.

Phase 21 adds joined post-paging pushdown coverage:

1. SQL-backed joined projection rows preserve C# operator order for `Where(...)` and ordering after `Skip(...)` or `Take(...)`.
2. Derived-source SQL selects joined projection aliases plus joined primary-key aliases, then binds later predicates/orderings to the derived aliases.
3. Provider behavior tests compare fluent and query-syntax joined post-paging rows with in-memory LINQ across SQLite, MySQL, and MariaDB.
4. `Any()` and `Count()` over paged joined rows reduce over the derived joined source instead of flattening past paging.
5. Transaction-root tests prove joined pushdown executes from `transaction.Query()`.
6. Unsupported diagnostics keep row-local computed joined projections, relation projection, grouped joins, and opaque transparent identifiers outside the supported pushdown boundary.
