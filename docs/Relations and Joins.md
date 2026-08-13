# Relations and Joins

DataLinq has three different relationship stories. Keep them separate:

- relation properties on generated models
- relation-aware predicates over generated relation properties
- explicit LINQ joins between query roots

Mixing those up is the fastest way to write a query that looks reasonable but should be rejected.

## Generated Relation Properties

Generated relation properties are for object traversal after you have a model instance.

```csharp
var department = Department.Get("d005", db);
var managers = department.Managers.ToList();
```

Collection relations return `IImmutableRelation<T>`. They are lazy and cache-aware. The first traversal resolves related primary keys and hydrates missing rows; later traversal can reuse relation and row-cache state.

Singular relations return the related model instance:

```csharp
var assignment = db.Query().DepartmentEmployees
    .OrderBy(row => row.emp_no)
    .First();

var departmentName = assignment.departments.Name;
```

That is ordinary relation traversal. It is not the same thing as a provider-side join unless the relation member appears inside one of the supported query shapes below.

## Relation Predicates

One-to-many relation existence predicates can translate to SQL `EXISTS`.

```csharp
var departmentsWithManager = db.Query().Departments
    .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
    .OrderBy(department => department.DeptNo)
    .ToList();

var departmentsWithoutManager = db.Query().Departments
    .Where(department => !department.Managers.Any(manager => manager.emp_no == managerNumber))
    .ToList();
```

Supported forms include `Any()`, `Any(predicate)`, negated `Any(predicate)`, and existence-equivalent `Count()` comparisons. The related-row predicate body still has to be simple: direct related-row member comparisons against local values plus supported boolean grouping.

Unsupported relation predicate shapes should throw `QueryTranslationException`. Relation traversal from inside the related-row predicate is intentionally rejected.

## Implicit Singular Relation Joins

Singular relation member access can be SQL-backed in documented provider queries:

```csharp
var rows = db.Query().DepartmentEmployees
    .Where(row => row.departments.Name.Contains("e"))
    .OrderBy(row => row.departments.Name)
    .ThenBy(row => row.emp_no)
    .Take(20)
    .Select(row => new
    {
        row.emp_no,
        row.dept_no,
        DepartmentName = row.departments.Name
    })
    .ToList();
```

That query uses an implicit inner join. DataLinq binds `row.departments.Name` to a relation source slot and renders the related table in SQL. Repeated references to the same singular relation reuse the same implicit join source.

Before rendering an implicit or explicit join, DataLinq checks converter-aware key compatibility. Converter-backed columns need matching resolved model, canonical provider, and nominal converter types; canonical `Guid` columns also need the same resolved storage format for the active provider. Known mismatches fail before SQL execution. Converter authors still own the behavioral invariant that equal model keys encode to equal canonical values for both columns—the framework cannot infer that from a converter class alone.

This is deliberately narrow:

- supported: singular relation member access in `Where`, `OrderBy`, `ThenBy`, and direct `Select(...)` projection
- unsupported: relation object projection, collection relation projection, multi-hop traversal, left-join/null-preserving traversal, and hidden lazy-loading inside provider projection

## Explicit Inner Joins

Use explicit joins when both sides are direct DataLinq query roots and the join key is a direct member equality.

```csharp
var assignments =
    from departmentEmployee in db.Query().DepartmentEmployees
    join department in db.Query().Departments
        on departmentEmployee.dept_no equals department.DeptNo
    where department.Name.Contains("e")
    orderby department.Name, departmentEmployee.emp_no
    select new
    {
        departmentEmployee.emp_no,
        departmentEmployee.dept_no,
        DepartmentName = department.Name
    };

var firstPage = assignments.Take(20).ToList();
```

The equivalent fluent `Join(...)` form is also supported:

```csharp
var assignments = db.Query().DepartmentEmployees
    .Join(
        db.Query().Departments,
        departmentEmployee => departmentEmployee.dept_no,
        department => department.DeptNo,
        (departmentEmployee, department) => new
        {
            departmentEmployee.emp_no,
            departmentEmployee.dept_no,
            DepartmentName = department.Name
        })
    .Where(row => row.dept_no == "d005")
    .OrderBy(row => row.emp_no)
    .Take(20)
    .ToList();
```

Direct source-slot projection rows are SQL-backed: DataLinq selects aliases for the projected members and reads them from the provider reader. Row-local joined projections can materialize source rows through table caches, but post-paging composition is supported only for SQL-backed joined projection rows.

## Unsupported Join Shapes

These are outside the current support boundary:

- `GroupJoin(...)`
- outer joins and left joins
- multiple explicit joins
- composite anonymous-object join keys
- relation-property joins
- relation object or collection relation projection
- opaque query-syntax transparent identifiers
- projecting whole source entities from query-syntax joins
- row-local computed joined projection composition after `Skip(...)` or `Take(...)`

Materialize first when you intentionally want LINQ-to-objects behavior:

```csharp
var rows = db.Query().DepartmentEmployees
    .Where(row => row.dept_no.StartsWith("d00"))
    .ToList()
    .GroupJoin(
        db.Query().Departments.ToList(),
        departmentEmployee => departmentEmployee.dept_no,
        department => department.DeptNo,
        (departmentEmployee, departments) => new { departmentEmployee, departments });
```

That code is no longer a provider query after `ToList()`. It may be exactly what you want for a small data set, but it is not SQL translation.

## Where To Check The Boundary

For the exact tested support surface, see [Supported LINQ Queries](Supported%20LINQ%20Queries.md) and [LINQ Translation Support Matrix](support-matrices/LINQ%20Translation%20Support%20Matrix.md).
