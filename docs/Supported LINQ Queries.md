# Supported LINQ Queries

This page is intentionally conservative.

It describes query shapes that are clearly exercised by the test suite today. If a LINQ shape is not listed here, do not assume it is supported just because it looks reasonable.

## Core Query Operations

The following operations are covered by tests:

- `ToList()`
- `Count()`
- `Count(predicate)`
- `Any()`
- `Any(predicate)`
- `Where(...).Any()`
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

Empty local `Any(predicate)` has similar fixed-condition coverage. That does not mean arbitrary non-empty object-list `Any(predicate)` is broadly supported; phase 6 tracks that separately.

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

It also covers guarded nullable value member access in selected date/time predicates, such as `x.last_login.HasValue && x.last_login.Value.Hour == hour`. Treat that as tested guard-plus-member translation, not as a promise that every nullable expression shape can become SQL.

## Supported Projection Shapes

The test suite covers:

- selecting the full model
- selecting a scalar property such as `Select(x => x.DeptNo)`
- selecting an anonymous type such as `Select(x => new { no = x.DeptNo, name = x.Name })`

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
- Unsupported translation usually surfaces as `NotImplementedException`, not as a helpful warning.
- If you are writing a new query shape and you are not sure whether it is supported, add a test first and then document it.

## Not Yet Documented as Supported

The current docs do not claim support for these because this pass has not verified them rigorously enough:

- nullable `.HasValue` checks
- `GroupBy(...)`
- general-purpose `Join(...)`
- aggregate operators such as `Sum(...)`, `Min(...)`, `Max(...)`, or `Average(...)`
- broader client-side method translation beyond the string members listed above
- non-empty local object-list predicates such as `items.Any(item => item.Id == row.Id)`

That does not automatically mean they are impossible. It means the docs should not lie about them.
