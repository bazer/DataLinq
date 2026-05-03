> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# LINQ Translation Support Audit and Roadmap

**Status:** Phase 6 planning source; the execution plan in [`../roadmap-implementation/phase-6-linq-translation-coverage-and-query-composition/Implementation Plan.md`](../roadmap-implementation/phase-6-linq-translation-coverage-and-query-composition/Implementation%20Plan.md) has been implemented for its planned support boundary.

## Purpose

DataLinq's LINQ provider has grown enough useful translation behavior that the missing parts now hurt more than a simple "not implemented" bucket suggests.

The Phase 6 LINQ work started with a real support matrix and targeted fixes, not a broad query-provider rewrite. Future LINQ expansion should keep that same discipline.

The immediate user-facing gaps are:

- `Enumerable.Any(predicate)` over local collections only supports one narrow equality shape
- `Contains` over projected local collections often requires a manual `ToArray()` materialization
- chained `Where(...)` can lose outer predicates because nested `QueryModel` parsing returns early
- fixed true/false conditions work in some cases, but composition with `AND`, `OR`, negation, and empty collections needs clearer rules

Those are not exotic features. They are ordinary application queries. We should treat them as product quality work.

## Current Translation Architecture

The current SQL path is concentrated in:

- `QueryExecutor`
  Converts Remotion `QueryModel` instances into `SqlQuery<T>`, applies result operators, executes rows/scalars, and handles basic projections.
- `WhereVisitor<T>`
  Walks Remotion/C# expression trees for `Where` predicates and writes directly into `WhereGroup<T>`.
- `QueryBuilder<T>`
  Resolves columns, constants, SQL functions, comparisons, negation, and boolean group state.
- `OrderByVisitor<T>`
  Handles simple member-based ordering.
- `SqlQuery<T>`, `WhereGroup<T>`, `Where<T>`, `Operand`, and `Comparison`
  Hold the provider-neutral-ish SQL shape before provider rendering.

That architecture can keep supporting incremental fixes. It is not ready for arbitrary query composition, joins, or provider capability planning without a clearer intermediate model.

## Currently Covered Behavior

The active compliance tests show support for:

- simple equality/inequality and range comparisons
- reversed constant/member comparisons
- enum comparisons
- nullable boolean comparisons in several `==`, `!=`, and negated forms
- grouped `AND`/`OR` predicates and nested negation
- local `Contains` over arrays, lists, sets, and some span forms
- empty local `Contains` and `Any` as fixed true/false conditions
- string `StartsWith`, `EndsWith`, `Contains`, `ToUpper`, `ToLower`, `Trim`, `Substring`, `Length`, `IsNullOrEmpty`, and `IsNullOrWhiteSpace`
- selected date/time members such as `Year`, `Month`, `Day`, `DayOfYear`, `DayOfWeek`, `Hour`, `Minute`, `Second`, and `Millisecond`
- `Any`, `Count`, `Single`, `SingleOrDefault`, `First`, `FirstOrDefault`, `Last`, and `LastOrDefault` result operators in the current supported shapes
- `OrderBy`, `ThenBy`, descending ordering, `Skip`, and `Take`
- simple member projection and a narrow anonymous-object projection path

That is a better baseline than the code comments imply, but it is undocumented and uneven.

## Known Gaps

### Local `Any(predicate)` Is Too Narrow

This fails today:

```csharp
lmData.Query().Lagfart
    .Where(x =>
        AllaUnikaFastigheter.Any(y => y == x.FastighetsReferens) &&
        leveranser.Any(y => y.Guid == x.KunddataLeveransUUID))
    .ToList();
```

The first `Any` shape is the simple "local item equals outer column" case. The second is "local item member equals outer column", with nullable conversion in the expression:

```text
Convert([y].Guid, Nullable`1) == [x].KunddataLeveransUUID
```

The translator should turn this into an `IN` predicate over `leveranser.Select(y => y.Guid)`, applying the same nullability/value normalization used for direct comparisons.

### Projected Local Collections Need Manual Materialization

This should work:

```csharp
vagfasDb.Query().Foreningar
    .Where(f => föreningIds.Select(id => id.Id).Contains(f.ForeningID!.Value));
```

Today the reliable workaround is:

```csharp
var container = föreningIds.Select(id => id.Id).ToArray();
return vagfasDb.Query().Foreningar
    .Where(f => container.Contains(f.ForeningID!.Value));
```

The translator should be able to partially evaluate local projection pipelines that do not depend on the database query source.

### Chained `Where` Composition Is Suspect

`QueryExecutor.ParseQueryModel` currently extracts a nested query model from `MainFromClause.FromExpression` and returns the parsed subquery immediately.

That is a red flag for:

```csharp
query.Where(a).Where(b)
```

The parser should preserve both predicates, applying the inner query model first and then the outer body clauses/result operators. Returning early from the outer model risks only the first predicate becoming SQL.

### Fixed Conditions Need a More Explicit Model

Empty collection translation currently emits fixed conditions such as always false/always true. That is the right idea, but it needs a disciplined model:

- `empty.Contains(x)` -> false
- `!empty.Contains(x)` -> true
- `empty.Any(predicate)` -> false without visiting the predicate
- `!empty.Any(predicate)` -> true without visiting the predicate
- composition with `AND`/`OR` should preserve parentheses and short-circuit semantics where possible

The implementation exists in pieces. It needs tests and clearer invariants so future fixes do not break boolean grouping.

### Unsupported Expression Shapes Are Not Classified

Common translator failures now use `QueryTranslationException` rather than raw `NotImplementedException` messages from visitor internals. Future diagnostics work should continue to distinguish:

- unsupported but valid LINQ shape
- valid shape that should become SQL soon
- client-only expression that should be rejected clearly
- provider-specific unsupported operation
- translator bug

The error surface should help users rewrite the query or report a precise missing translation.

## Support Matrix To Build

The first deliverable should be a support matrix covering:

- predicate composition: `Where`, chained `Where`, `AND`, `OR`, negation, parentheses
- local collections: `Contains`, `Any`, `All` if considered, empty lists, projected lists, nullable conversions
- member and method translation: string, date/time, nullable `.Value`, enum, Guid, char
- comparisons: column/value, value/column, column/column, function/value, value/function
- result operators: `Any`, `Count`, `Single`, `First`, `Last`, `Skip`, `Take`, unsupported tail/while operators
- ordering: simple member ordering, chained ordering, ordering after filtered subqueries
- projections: entity, member, anonymous object, unsupported nested projection
- provider differences: SQLite, MySQL, MariaDB SQL function and type quirks

This matrix should live in this document or a child document once it becomes too large.

## Recommended Near-Term Work

### 1. Chained `Where` Correctness

This should be first because it is a correctness bug, not a feature request.

Deliverables:

- regression tests proving `Where(a).Where(b)` applies both predicates in SQL
- fix `QueryExecutor.ParseQueryModel` so it composes nested query models instead of returning early
- verify ordering/paging still apply in the correct phase

### 2. Local Projection Evaluation for `Contains`

Deliverables:

- support local `Select(...).Contains(column)` when the projection does not depend on the database query source
- normalize nullable `.Value` and nullable conversions on the column side
- tests for arrays, lists, projected lists, and empty projected lists

### 3. `Any(predicate)` Over Local Object Lists

Deliverables:

- support `list.Any(y => y == x.Column)`
- support `list.Any(y => y.Member == x.Column)`
- support reversed equality: `list.Any(y => x.Column == y.Member)`
- support nullable conversion wrappers on either side
- generate `IN`/`NOT IN` over extracted local values
- keep complex local predicates unsupported until there is a real design

### 4. Fixed Condition Invariants

Deliverables:

- define the fixed-condition truth table
- add tests for empty collections inside nested `AND`/`OR`/negation
- ensure SQL rendering does not accidentally drop or reorder fixed conditions

### 5. Translation Diagnostics

Deliverables:

- replace common raw `NotImplementedException` paths with a query translation exception type
- include expression shape, supported alternatives, and provider when available
- keep unsupported behavior explicit; do not silently fall back to client evaluation

## Explicit Non-Goals

- full LINQ provider rewrite
- implicit joins through relation properties
- arbitrary subqueries against database tables
- client-side fallback for unsupported SQL predicates
- translating every LINQ method on `Enumerable`
- query pipeline abstraction for non-SQL providers
- dependency-tracked result-set caching

Those may still be valuable, but they are larger roadmap items. The near-term work should make common SQL-backed predicates reliable first.

## Roadmap Placement

This work should happen after the first product-trust/schema-validation slice, but before Native AOT/WebAssembly work.

Reason:

- these are active real-query compatibility gaps
- fixes are testable against the existing provider matrix
- AOT work will be cleaner if query translation behavior is better classified first
- broad capability expansion should not jump ahead of ordinary predicate support

## First Audit Tasks

1. Inventory supported expression shapes from compliance tests.
2. Add failing regression tests for the three known user cases:
   - local object-list `Any(y => y.Member == x.NullableColumn)`
   - projected local `Select(...).Contains(x.NullableColumn.Value)`
   - chained `Where(a).Where(b)`
3. Decide which failures are bugs versus new features.
4. Fix the chained `Where` composition path first.
5. Implement the smallest local projection/value extraction helper shared by `Contains` and `Any(predicate)`.
