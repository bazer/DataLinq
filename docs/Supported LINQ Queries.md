# Supported LINQ Queries

This page is intentionally conservative.

It describes query shapes that are clearly exercised by the test suite today. If a LINQ shape is not listed here, do not assume it is supported just because it looks reasonable.

For the detailed maintainer evidence behind these claims, see the [LINQ Translation Support Matrix](support-matrices/LINQ%20Translation%20Support%20Matrix.md).

## Parser Boundary

In 0.8 and later, `Database.Query()` runs through DataLinq's expression parser and query-plan SQL renderer. In the current 0.9 implementation, the production path is `ExpressionQueryPlanProvider` -> `ExpressionQueryPlanParser` -> `QueryPlanTemplate` + `QueryPlanInvocation` -> `QueryExecutionRequest` -> backend selection and capability validation -> `SqlQueryPlanBackend` -> `QueryPlanSqlBuilder`. Entity sequences, the six entity terminals (`Single`, `SingleOrDefault`, `First`, `FirstOrDefault`, `Last`, and `LastOrDefault`), the six scalar reductions (`Count`, `Any`, `Sum`, `Min`, `Max`, and `Average`), and the currently supported direct SQL projection family execute through the SQL backend. That direct family comprises `ScalarMember` and `SqlRow` sequences plus their parser-supported row terminals, and `GroupedAggregate` sequences; grouped row terminals and explicit-join row terminals remain parser-rejected. Retained local projection recipes are still selected and validated through the same production gate but temporarily use their SQL compatibility executor while the local-recipe backend adapter remains roadmap work.

That implementation detail does not expand the public LINQ contract. The supported surface is still the test-backed subset below. Structurally valid plans that exceed the selected backend's capabilities fail before command execution with a redacted DataLinq-owned capability diagnostic; parser-unsupported provider-query shapes still fail with DataLinq-owned `QueryTranslationException` diagnostics. Neither case uses silent client-side predicate fallback.

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
- `Join(...)`
- `GroupBy(...).Select(...)` for the narrow grouped aggregate projection shape documented below
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

Nullable local membership preserves C# null semantics rather than exposing SQL's raw three-valued `IN` behavior. For a nullable column, a non-null-only membership check also guards `IS NOT NULL`, so it remains false for null even when nested under an outer negation. A mixed sequence such as `[value, null]` matches either the non-null value or `NULL`; negating it excludes both. Negating a non-null-only sequence still includes a `NULL` column value, a null-only sequence becomes `IS NULL`/`IS NOT NULL`, and an empty sequence remains fixed false/true. Only non-null sequence members become SQL parameters.

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

Captured nullable scalars are specialized by nullness. Equality or inequality against a captured null value renders `IS NULL` or `IS NOT NULL` directly instead of binding a null comparison parameter.

## Supported Relation Predicates

Generated one-to-many relation properties can be used in a narrow set of SQL-backed predicates. The translator emits a correlated `EXISTS` subquery instead of lazy-loading the relation for every candidate row.

The test suite covers:

- `parent.Children.Any()`
- `!parent.Children.Any(...)`
- `parent.Children.Any(child => child.Column == value)`
- simple related-row comparisons with `==`, `!=`, `>`, `>=`, `<`, and `<=`
- simple `&&` and `||` groups inside the relation predicate
- existence-equivalent counts such as `parent.Children.Count() > 0`, `Count() >= 1`, `Count() != 0`, `Count() == 0`, `Count() <= 0`, and `Count() < 1`

Example:

```csharp
var departments = db.Query().Departments
    .Where(department => department.Managers.Any(manager => manager.emp_no == employeeNumber))
    .ToList();
```

This first slice is intentionally not a collection relation traversal engine. These shapes are not supported yet:

- collection relation projections inside provider `Select(...)`
- relation aggregates other than the documented existence-equivalent `Count()` comparisons
- thresholds such as `Children.Count() > 1`
- predicates that traverse another relation from the related row, such as `child.Parent.Name == value`
- collection relation traversal outside the documented one-to-many `Any(...)`/existence pattern

Generated singular relation properties have a separate SQL-backed implicit inner-join slice. The test suite covers singular relation traversal in root-row predicates, ordering, direct projection, and supported row-local projection recipes:

- `row.SingularRelation.Member` inside `Where(...)`
- `row.SingularRelation.Member` inside `OrderBy(...)` and `ThenBy(...)`
- `Select(row => row.SingularRelation.Member)`
- `Select(row => new { row.Id, RelatedName = row.SingularRelation.Name })` when every projected member binds to a source-slot column
- supported computed projection such as `Select(row => row.SingularRelation.Name.Trim())`
- repeated access to the same relation in one query reuses one implicit join source

Example:

```csharp
var rows = db.Query().DepartmentEmployees
    .Where(row => row.departments.Name.StartsWith("S"))
    .OrderBy(row => row.departments.Name)
    .ThenBy(row => row.emp_no)
    .Select(row => new
    {
        row.emp_no,
        DepartmentName = row.departments.Name
    })
    .ToList();
```

This is an inner join. Rows whose singular relation does not resolve are not preserved. Left-join/null-preserving traversal is not supported yet. A computed related-member projection is not translated as a SQL expression: SQL selects the joined source keys, DataLinq materializes the rows, and the retained projection recipe performs the supported local computation.

Multi-hop relation traversal, relation object projection, and collection relation projection are not supported in provider `Select(...)`.

## Supported Projection Shapes

`Select(...)` has two deliberately separate paths:

- SQL-backed projection rows for direct source-slot values.
- Row-local projection after materialization from a self-contained recipe for supported computed .NET expressions.

The SQL-backed path reads projected aliases directly from `IDataLinqDataReader`. The test suite covers:

- selecting the full model
- selecting a scalar property such as `Select(x => x.DeptNo)`
- selecting an anonymous type such as `Select(x => new { no = x.DeptNo, name = x.Name })`
- selecting direct members from supported explicit joins
- selecting supported singular relation members such as `Select(x => x.SingularRelation.Name)`
- selecting anonymous rows that combine root columns and supported singular relation columns

Computed projections remain row-local after SQL filtering, ordering, paging, and materialization. The test suite covers:

- computed scalar projections such as `Select(x => x.first_name + ":" + x.emp_no.Value)`
- computed anonymous projections using materialized member chains such as `Trim()`, `ToUpper()`, and `Length`
- computed singular-relation member projections such as `Select(x => x.SingularRelation.Name.Trim())`

The retained plan stores the normalized recipe and captured binding references; execution does not recover or compile the original `Select(...)` expression. Single-source computed recipes form the AOT-safe local path. Recipes that require joined-row materialization remain SQL-only compatibility paths in 0.9.

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

The supported SQL-backed projection path is not a broad SQL expression translator. Client methods, arbitrary computed SQL expressions, relation objects, collection relations, nested database subqueries, multi-hop relation traversal, and nullable left-join relation projection remain rejected. If projection code needs ordinary .NET computation, materialize rows first or use the documented row-local computed projection shapes.

## Supported Explicit Joins

The test suite covers one narrow explicit inner join shape, both as fluent `Join(...)` and as ordinary C# query syntax that lowers to `Queryable.Join(...)`:

- one outer DataLinq query source
- one inner DataLinq query source
- direct member equality keys such as `outer.DepartmentId` and `inner.Id`
- nullable `.Value` key selectors such as `employee.emp_no.Value`
- a result selector that projects direct source-slot values from both sides
- a result selector with supported row-local computation over joined members, such as `department.Name.Trim()` or a scalar string concatenation
- composed `Where(...)`, `OrderBy(...)`, `ThenBy(...)`, `Skip(...)`, `Take(...)`, `Any()`, and `Count()` over projected joined members that map back to source columns
- post-paging `Where(...)`, ordering, `Any()`, and `Count()` over SQL-backed joined projection rows, rendered through a derived-source boundary so C# operator order is preserved
- query-syntax transparent identifiers for a single inner join, when every referenced member can bind back to a source slot

Joined row-local recipes are self-contained, but they remain SQL-only in 0.9 because SQL owns joined-row selection and primary-key buffering. Post-paging composition over a row-local joined result is still unsupported; materialize before applying further LINQ-to-Objects operators.

Fluent example:

```csharp
var rows = db.Query().DepartmentEmployees
    .Join(
        db.Query().Departments,
        departmentEmployee => departmentEmployee.dept_no,
        department => department.DeptNo,
        (departmentEmployee, department) => new
        {
            departmentEmployee.emp_no,
            department.Name
        })
    .Where(row => row.Name.StartsWith("S"))
    .OrderBy(row => row.dept_no)
    .ThenBy(row => row.emp_no)
    .Take(20)
    .ToList();
```

Equivalent query-syntax example:

```csharp
var rows =
    (from departmentEmployee in db.Query().DepartmentEmployees
     join department in db.Query().Departments
        on departmentEmployee.dept_no equals department.DeptNo
     where department.Name.StartsWith("S")
     orderby department.Name, departmentEmployee.emp_no
     select new
     {
         departmentEmployee.emp_no,
         departmentEmployee.dept_no,
         DepartmentName = department.Name
     })
    .Take(20)
    .ToList();
```

When the result selector contains only direct source-slot values, the implementation reads SQL projection aliases directly from the joined result row. Row-local computed joined projections remain a separate fallback and cannot be used for provider-side composition after paging.

Composed predicates and orderings over joined rows are SQL-backed only when the joined projection member is a direct source-slot value, such as `row.dept_no` or `row.Name` in the example above. If a joined projection member is computed row-local code, materialize first and filter/order in memory. When a supported joined query applies `Where(...)`, ordering, `Any()`, or `Count()` after `Skip(...)` or `Take(...)`, DataLinq renders the paged join as a derived source and binds the later operators to the derived projection aliases.

These join shapes are not supported yet:

- `GroupJoin(...)`
- left/outer join patterns such as `DefaultIfEmpty()`
- composite anonymous-object join keys
- multiple chained joins
- query-syntax transparent identifiers that project whole source entities or cannot bind back to source-slot values
- scalar aggregates over joined rows other than `Any()` and `Count()`
- relation-property joins, relation object projection, or collection relation projection inside the result selector
- fluent relation-aware join APIs such as `JoinBy(...)`, `JoinMany(...)`, `LeftJoinBy(...)`, and `LeftJoinMany(...)`
- standard `Queryable.LeftJoin(...)`

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

`Sum(...)` returns zero for an empty filtered sequence. Nullable `Min(...)`, `Max(...)`, and `Average(...)` return `null` for an empty filtered sequence. Aggregates over computed selectors and relation-property aggregates are not supported yet. Grouped aggregate projection has a separate, narrower contract below.

## Supported Grouped Aggregate Projection

The test suite covers SQL-shaped `GroupBy(...)` aggregate projection:

- a single DataLinq query source
- optional `Where(...)` before `GroupBy(...)`
- direct mapped member keys
- composite anonymous-object keys whose members are SQL-renderable
- SQL-renderable computed key members such as supported date parts and string functions
- grouping over supported explicit joined row projections
- grouping over SQL-backed implicit singular relation traversal
- immediate `Select(...)`
- projection members limited to `group.Key` for scalar keys, `group.Key.Member` for composite keys, `group.Count()`, and direct numeric grouped `Sum(...)`, `Min(...)`, `Max(...)`, and `Average(...)` selectors
- nullable numeric selectors for the tested nullable numeric column shape
- narrow `Where(group => ...)` predicates after `GroupBy(...)` when they compare `group.Key` or supported grouped aggregates
- `Where(row => ...)`, ordering, `Skip(...)`, and `Take(...)` after grouped aggregate projection when later operators bind to projected key or aggregate members
- `Count()` and `Any()` over grouped aggregate projection rows
- constructor-backed DTO/record grouped projections when constructor parameter names are stable

Example:

```csharp
var countsByDepartment = db.Query().DepartmentEmployees
    .Where(row => row.dept_no.StartsWith("d00"))
    .GroupBy(row => row.dept_no)
    .Select(group => new
    {
        DeptNo = group.Key,
        Count = group.Count(),
        MinEmployeeNumber = group.Min(row => row.emp_no),
        MaxEmployeeNumber = group.Max(row => row.emp_no),
        AverageEmployeeNumber = group.Average(row => row.emp_no)
    })
    .ToList();
```

Composite and computed keys project named key members:

```csharp
var hiringByDepartmentYear = db.Query().DepartmentEmployees
    .GroupBy(row => new
    {
        row.dept_no,
        FromYear = row.from_date.Year
    })
    .Select(group => new
    {
        DeptNo = group.Key.dept_no,
        group.Key.FromYear,
        Count = group.Count()
    })
    .ToList();
```

Grouped predicates render as SQL `HAVING`, not row-level `WHERE`:

```csharp
var busyDepartments = db.Query().DepartmentEmployees
    .GroupBy(row => row.dept_no)
    .Where(group => group.Count() > 5)
    .Select(group => new
    {
        DeptNo = group.Key,
        Count = group.Count()
    })
    .ToList();
```

Grouped projection rows can also be filtered, ordered, and paged when the later operators bind to key or aggregate members:

```csharp
var topDepartments = db.Query().DepartmentEmployees
    .GroupBy(row => row.dept_no)
    .Select(group => new
    {
        DeptNo = group.Key,
        Count = group.Count(),
        SumEmployeeNumbers = group.Sum(row => row.emp_no)
    })
    .Where(row => row.Count > 5 && row.SumEmployeeNumbers > 0)
    .OrderByDescending(row => row.Count)
    .ThenBy(row => row.DeptNo)
    .Take(10)
    .ToList();
```

DataLinq renders this as a SQL grouped aggregate query and materializes the aggregate rows directly from the data reader. These rows are not entity rows and do not go through table-cache materialization.

This is not general LINQ `GroupBy(...)` support. These grouped shapes remain unsupported:

- bare `GroupBy(...).ToList()`
- materialized `IGrouping<TKey,TElement>` sequences
- enumerating grouped elements inside the projection
- whole composite `group.Key` object projection; project `group.Key.Member` values instead
- client-computed group keys that cannot render as SQL
- computed grouped aggregate selectors such as `group.Sum(row => row.Value + 1)`
- grouping over row-local computed joined projection members
- collection relation grouping
- filters/orderings that require computed grouped-row members, grouped element enumeration, or client fallback
- further `Where(...)` or ordering after `Skip(...)`/`Take(...)` over grouped projection rows

## Supported Ordering and Paging

The test suite covers:

- single-column ordering
- multi-column ordering
- mixed ascending and descending ordering
- `Skip(...)`
- `Take(...)`
- `Skip(...).Take(...)`
- post-paging `Where(...)`
- post-paging `OrderBy(...)` and `OrderByDescending(...)`
- `Count()` and `Any()` over paged sources

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

When an operator after `Skip(...)` or `Take(...)` must apply to the already-paged source, DataLinq renders a SQL subquery boundary instead of flattening the chain into the wrong clause order.

Supported examples include:

```csharp
var topMalesByNewestHireDate = db.Query().Employees
    .Where(x => x.emp_no < 990000)
    .OrderBy(x => x.birth_date)
    .Take(20)
    .Where(x => x.gender == Employee.Employeegender.M)
    .OrderByDescending(x => x.emp_no)
    .Take(5)
    .ToList();
```

The subquery boundary is deliberately narrow. It is for single-source query composition over mapped rows. It is not a promise of arbitrary nested database subqueries in projections, broad SQL expression projection lists, joined-row pushdown, or grouped-query pushdown.

## Direct Primary-Key Lookup

There is also tested support for direct lookup without a LINQ predicate:

```csharp
var department = Department.Get("d005", db);
```

That is useful when you already know the key and do not need a query pipeline.

## Known Unsupported Operators

The test suite explicitly expects `QueryTranslationException` for:

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

- broad `GroupBy(...)` beyond the SQL-backed grouped aggregate row shapes documented above
- `GroupJoin(...)`, outer joins, composite-key joins, and multi-join query syntax
- aggregate operators over computed selectors or relation properties
- relation object or collection relation projections inside provider `Select(...)`, and unsupported relation traversal inside relation predicates
- broader client-side method translation inside SQL predicates beyond the string members listed above

That does not automatically mean they are impossible. It means the docs should not lie about them.
