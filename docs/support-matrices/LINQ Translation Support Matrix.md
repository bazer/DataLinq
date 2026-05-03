> [!NOTE]
> This matrix is maintenance evidence for LINQ translator coverage. For the shorter user-facing contract, start with [Supported LINQ Queries](../Supported%20LINQ%20Queries.md).

# LINQ Translation Support Matrix

**Status:** Current Phase 6 closeout baseline; update whenever LINQ translator support changes.

This matrix records what the active compliance tests prove today, where the public support docs are accurate, and which gaps remain outside the documented support boundary.

The evidence column intentionally points at test files instead of implementation files. If a shape is not represented in active tests, treat it as unsupported or at least undocumented until a focused regression test proves otherwise.

## Predicate Translation

| Area | Currently tested support | Evidence | Audit notes |
| --- | --- | --- | --- |
| Scalar equality and inequality | `==`, `!=`, reversed constant/member equality, missing-row filters, chained `Where(a).Where(b)` | `src/DataLinq.Tests.Compliance/Query/EmployeesQueryBehaviorTests.cs` | Chained `Where(a).Where(b)` is now separately covered across collection, scalar, single-row, and paging result shapes. |
| Range comparison | `>`, `>=`, `<`, `<=` against constants and selected member expressions | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesDateTimeMemberTests.cs` | Public docs match this. |
| Enum comparison | enum equality, inequality, negated enum equality | `EmployeesQueryBehaviorTests.cs` | Public docs match this. |
| Property-to-property comparison | equality, inequality, greater-than, less-than-or-equal comparisons between translated operands | `EmployeesQueryBehaviorTests.cs` | Public docs mention this, but the coverage is narrow and should not be generalized to arbitrary expressions. |
| Boolean grouping | `&&`, `||`, `!`, nested grouped predicates, De Morgan-style groups | `Translation/EmployeesBooleanLogicTests.cs`, `EmployeesQueryBehaviorTests.cs` | Public docs match this at a high level. Fixed true/false conditions now have dedicated regression coverage. |
| Nullable boolean predicates | nullable bool compared to `true`, `false`, and `null`; negated equality forms | `Translation/EmployeesNullableBooleanTests.cs` | Public docs match this. |
| Nullable value guards | `HasValue && Value.Member == constant` for selected date/time members | `Translation/EmployeesDateTimeMemberTests.cs` | Public docs understated this by keeping `HasValue` in the not-yet-documented bucket. The tested support is still guarded-member access, not general nullable algebra. |
| Character predicates | LINQ char predicate matches raw SQL parameter behavior | `Translation/CharPredicateTranslationTests.cs`, `Query/SQLiteInMemoryBehaviorTests.cs` | Public docs did not call this out; it is a narrow parameter/type-fidelity case. |

## Local Collections and Fixed Conditions

| Area | Currently tested support | Evidence | Audit notes |
| --- | --- | --- | --- |
| `Contains` over local arrays | `ids.Contains(row.Column)` and negated form translate as membership predicates | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesContainsTranslationTests.cs` | Public docs match this. |
| `Contains` over `List<T>` and `HashSet<T>` | list/set membership over local values | `EmployeesQueryBehaviorTests.cs`, `Translation/EmployeesEmptyListQueryTests.cs` | Public docs match this. |
| `Contains` over `ReadOnlySpan<T>` | span-backed membership over local arrays | `Translation/EmployeesContainsTranslationTests.cs` | Public docs understated this. Keep the claim narrow: this proves the tested span shape, not arbitrary span pipelines. |
| Multiple collection predicates | combined local collection membership with `&&`, additional string predicates, and range predicates | `EmployeesQueryBehaviorTests.cs` | Useful coverage for local-sequence regression safety. |
| Empty local `Contains` | false fixed condition, negated true condition, direct composition, nested `AND`/`OR`, negated groups, and unsupported item expressions skipped when the empty list already determines the result | `Translation/EmployeesContainsTranslationTests.cs`, `Translation/EmployeesEmptyListQueryTests.cs`, `Translation/EmployeesBooleanLogicTests.cs` | Phase 6 documented the fixed-condition truth table and locked down grouping behavior. |
| Constant-item `Contains` | constant true and constant false predicates collapse to fixed conditions | `Translation/EmployeesContainsTranslationTests.cs` | Public docs did not call this out. This is implementation behavior worth preserving. |
| Empty local `Any(predicate)` | false fixed condition, negated true condition, direct composition, nested `AND`/`OR`, negated groups; complex or otherwise unsupported predicate bodies are ignored when the sequence is empty | `Translation/EmployeesEmptyListQueryTests.cs`, `Translation/EmployeesBooleanLogicTests.cs` | Public docs now distinguish empty fixed-condition behavior from non-empty equality membership. Phase 6 documented the fixed-condition truth table. |
| Projected local `Contains` | `localObjects.Select(x => x.Value).Contains(row.NullableColumn.Value)` and empty projected local sequences | `Translation/EmployeesContainsTranslationTests.cs` | Phase 6 added guarded local sequence extraction and regression coverage. This is now tested for safe local projections that do not reference the database query source. |
| Local object-list `Any(predicate)` | scalar item equality, object-member equality, reversed equality, nullable wrapper normalization, and negated `NOT IN` membership | `Translation/EmployeesEmptyListQueryTests.cs`, `Translation/EmployeesLocalAnyPredicateTests.cs` | Equality-membership shapes are covered. Compound non-empty local predicates remain unsupported. |

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
| Aggregates | `Sum`, `Min`, `Max`, `Average` are not documented as supported | none in query compliance coverage | Planned for Phase 7 as narrow scalar aggregate support. |

## Ordering, Paging, and Projection

| Area | Currently tested support | Evidence | Audit notes |
| --- | --- | --- | --- |
| Ordering | `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, mixed ascending/descending ordering | `EmployeesQueryBehaviorTests.cs` | Public docs match this. |
| Paging | `Skip`, `Take`, `Skip(...).Take(...)` with ordered queries and composed chained predicates | `EmployeesQueryBehaviorTests.cs` | Phase 6 includes a chained-filter paging regression. |
| Ordering plus filtering | `Where(...).OrderBy(...)`, `OrderBy(...).Where(...)`, and `Where(...).OrderBy(...).Where(...)` | `EmployeesQueryBehaviorTests.cs` | Phase 6 has a focused regression proving the outer predicate is preserved after an inner ordering clause. |
| Full-model projection | selecting the model entity | `EmployeesQueryBehaviorTests.cs` | Public docs match this. |
| Scalar projection | `Select(x => x.Property)` | `EmployeesQueryBehaviorTests.cs`, translation tests | Public docs match this. |
| Anonymous projection | `Select(x => new { ... })` for simple property members | `EmployeesQueryBehaviorTests.cs` | Public docs match this. Do not generalize to nested object creation or computed selectors. |
| Views and primary-key lookup | querying generated views and direct `Get<T>(key)` lookup | `SeededEmployeesQueryTests.cs`, `EmployeesQueryBehaviorTests.cs` | Direct lookup is not a LINQ predicate but belongs in the surrounding query support docs. |

## Unsupported or Not Yet Proven

These shapes are intentionally not part of the documented support boundary today:

- `GroupBy(...)`
- LINQ `Join(...)`
- relation-property query expansion
- aggregate result operators such as `Sum(...)`, `Min(...)`, `Max(...)`, and `Average(...)`
- arbitrary local `Enumerable` method chains inside predicates
- arbitrary client methods inside SQL predicates
- nested database subqueries
- complex selector expressions beyond entity, scalar member, and simple anonymous-object projection

Unsupported predicate methods, non-empty compound local `Any(predicate)` shapes, unsupported selectors, and unsupported scalar result operators now have focused `QueryTranslationException` coverage in `Translation/EmployeesUnsupportedQueryDiagnosticsTests.cs`.

## Regression Coverage Notes

Phase 6 added regression tests that make dropped predicates hard to miss:

1. `Where(a).Where(b)` where each predicate excludes different seeded rows and the combined result is strictly smaller than either single predicate.
2. `Where(a).OrderBy(...).Where(b)` if Remotion emits a nested query shape the current parser accepts.
3. `Where(a).Where(b).Count()`, `.Any()`, `.First()`, and `.SingleOrDefault()` to prove result operators apply after both predicates.
4. `Where(a).Where(b).Skip(...).Take(...)` over a deterministic ordering to prove paging is applied after the composed filter.

The highest-value remaining test aid is generated-SQL inspection for these composition cases. Row assertions are mandatory, but SQL shape assertions would expose a dropped outer predicate immediately instead of relying on seed-data luck.
