# Supported LINQ Queries

This page is intentionally conservative.

It describes query shapes that are clearly exercised by the test suite today. If a LINQ shape is not listed here, do not assume it is supported just because it looks reasonable.

For the detailed maintainer evidence behind these claims, see the [LINQ Translation Support Matrix](support-matrices/LINQ%20Translation%20Support%20Matrix.md).

## Core Query Operations

The following operations are covered by tests:

- `ToList()`
- `Count()`
- `Count(predicate)`
- `Any()`
- `Any(predicate)`
- `Where(...).Any()`
- `Sum(...)`
- `Min(...)`
- `Max(...)`
- `Average(...)`
- `Single(...)`
- `SingleOrDefault(...)`
- `First(...)`
- `FirstOrDefault(...)`
- `Last()`
- `LastOrDefault(...)`
- `OrderBy(...)`
- `OrderByDescending(...)`
- `ThenBy(...)`
- `ThenByDescending(...)`
- `Skip(...)`
- `Take(...)`
- `Select(...)`

## Supported Predicate Shapes

### Equality and comparison

Test coverage exists for:

- `==`
- `!=`
- `>`
- `>=`
- `<`
- `<=`

This includes:

- scalar comparisons
- enum comparisons
- nullable-boolean comparisons
- property-to-property comparisons such as `x.emp_no <= x.from_date.Day`

### Boolean composition

Test coverage exists for:

- chained `Where(...).Where(...)` predicates over the same source
- `&&`
- `||`
- `!`
- nested grouped predicates

### Collection membership

The test suite covers `Contains(...)` against in-memory collections used as an `IN (...)` style predicate:

- arrays
- `List<T>`
- `HashSet<T>`
- the tested `ReadOnlySpan<T>` shape
- projected local sequences such as `localIds.Select(x => x.Value).Contains(row.Id.Value)` when the projection is fully local

Empty local `Contains(...)` predicates are also covered in direct, negated, `AND`, and `OR` compositions. The translator treats those as fixed true/false conditions instead of emitting invalid `IN ()` SQL.

Local `Any(predicate)` over in-memory collections is covered for equality-membership shapes that can safely become `IN (...)` or `NOT IN (...)`:

- `ids.Any(id => id == row.Id)`
- `items.Any(item => item.Id == row.Id)`
- reversed equality such as `items.Any(item => row.Id == item.Id)`

Empty local `Any(predicate)` has similar fixed-condition coverage. For empty local collections, `Contains(...)`, `Any()`, and `Any(predicate)` become `1=0`; negating those expressions becomes `1=1`. The predicate body is not visited when an empty local sequence already decides the result. Compound local predicates are still not supported for non-empty collections; write those as an explicit local projection plus `Contains(...)` when the intent is membership.

### String members

The test suite covers:

- `StartsWith(...)`
- `EndsWith(...)`
- string `Contains(...)`
- `ToUpper()`
- `ToLower()`
- `Trim()`
- `Substring(...)`
- `Length`
- `string.IsNullOrEmpty(...)`
- `string.IsNullOrWhiteSpace(...)`

### Date and time member access

The test suite also covers member access inside predicates for several date and time types, including:

- `DateOnly.Year`
- `DateOnly.Month`
- `DateOnly.Day`
- `DateOnly.DayOfYear`
- `DateOnly.DayOfWeek`
- `TimeOnly.Hour`
- `DateTime.Minute`
- `DateTime.Second`
- `DateTime.Millisecond`

### Nullability

The test suite covers nullable-boolean comparisons such as:

- `x.IsDeleted == true`
- `x.IsDeleted == false`
- `x.IsDeleted == null`
- `x.IsDeleted != true`
- `x.IsDeleted != false`

It also covers nullable value predicates such as:

- `x.last_login.HasValue`
- `!x.last_login.HasValue`
- guarded `.Value` comparisons such as `x.last_login.HasValue && x.last_login.Value == login`
- guarded nullable date/time member access such as `x.created_at.HasValue && x.created_at.Value.Minute == minute`
- mixed nullable/non-nullable equality and inequality such as `x.last_login == login` and `x.last_login != login`

For `nullable != nonNullable`, null rows are included, matching C# lifted nullable semantics.

## Supported Projection Shapes

`Select(...)` projections are evaluated after DataLinq has translated SQL filtering, ordering, and paging and materialized the selected rows. They are not SQL `SELECT`-list expressions today. That is less efficient for wide rows than SQL-backed projection, but it keeps projection semantics honest: projection code runs as normal .NET code over the materialized model instance.

The test suite covers row-local projections such as:

- selecting the full model
- selecting a scalar property such as `Select(x => x.DeptNo)`
- selecting an anonymous type such as `Select(x => new { no = x.DeptNo, name = x.Name })`
- computed scalar projections such as `Select(x => x.first_name + ":" + x.emp_no.Value)`
- computed anonymous projections using materialized member chains such as `Trim()`, `ToUpper()`, and `Length`

Example:

```csharp
var departments = db.Query().Departments
    .OrderBy(x => x.DeptNo)
    .Select(x => new
    {
        no = x.DeptNo,
        name = x.Name
    })
    .ToList();
```

Relation-property projections are not supported in provider `Select(...)` yet. Load rows first with `ToList()` and then traverse relation properties explicitly so the extra relation queries are visible in your code.

## Supported Scalar Aggregates

The test suite covers SQL-backed scalar aggregates over direct numeric member selectors:

- `Sum(x => x.Number)`
- `Min(x => x.Number)`
- `Max(x => x.Number)`
- `Average(x => x.Number)`

Filtered aggregates are also covered:

```csharp
var total = db.Query().Managers
    .Where(x => x.dept_fk.StartsWith("d00"))
    .Sum(x => x.emp_no);
```

Nullable numeric members are supported when the selector is the nullable member itself or its nullable `.Value` member:

```csharp
var min = db.Query().Employees.Min(x => x.emp_no);
var sum = db.Query().Employees.Sum(x => x.emp_no!.Value);
```

`Sum(...)` returns zero for an empty filtered sequence. Nullable `Min(...)`, `Max(...)`, and `Average(...)` return `null` for an empty filtered sequence. Aggregates over computed selectors, grouped aggregates, and relation-property aggregates are not supported yet.

## Supported Ordering and Paging

The test suite covers:

- single-column ordering
- multi-column ordering
- mixed ascending and descending ordering
- `Skip(...)`
- `Take(...)`
- `Skip(...).Take(...)`

Example:

```csharp
var page = db.Query().Employees
    .Where(x => x.emp_no < 990000)
    .OrderBy(x => x.birth_date)
    .ThenBy(x => x.emp_no)
    .Skip(5)
    .Take(10)
    .ToList();
```

## Direct Primary-Key Lookup

There is also tested support for direct lookup without a LINQ predicate:

```csharp
var department = db.Get<Department>(new StringKey("d005"));
```

That is useful when you already know the key and do not need a query pipeline.

## Known Unsupported Operators

The test suite explicitly expects `NotSupportedException` for:

- `TakeLast(...)`
- `SkipLast(...)`
- `TakeWhile(...)`
- `SkipWhile(...)`

## Practical Advice

- If you care which row is "first" or "last", order explicitly first. Anything else is fake determinism.
- `Last()` and `LastOrDefault()` are supported in tested scenarios, but they are not the query engine's nicest path. If what you mean is "top by X", write `OrderByDescending(...).First()` instead.
- `Any(predicate)` is covered for straightforward cases. If a more elaborate `Any(predicate)` feels clever, flatten it into `Where(...).Any()` unless you have a test proving your exact shape works.
- Prefer query shapes already covered by tests. That is the brutally honest rule. Unsupported LINQ in ORMs does not fail gracefully; it usually fails late and irritates you.
- Unsupported translation should surface as `QueryTranslationException` with the unsupported method, operator, selector, or predicate expression in the message.
- If you are writing a new query shape and you are not sure whether it is supported, add a test first and then document it.

## Not Yet Documented as Supported

The current docs do not claim support for these because this pass has not verified them rigorously enough:

- `GroupBy(...)`
- general-purpose `Join(...)`
- aggregate operators over computed selectors, grouped aggregates, or relation properties
- relation-property projections inside provider `Select(...)`
- broader client-side method translation inside SQL predicates beyond the string members listed above

That does not automatically mean they are impossible. It means the docs should not lie about them.
