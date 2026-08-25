# DataLinq.Memory

`DataLinq.Memory` is an experimental, read-only backend for generated DataLinq models. It stores explicitly seeded rows in process and executes a small, documented query subset without a SQL provider.

`DataLinq.Memory` 0.9.0 is [published on NuGet](https://www.nuget.org/packages/DataLinq.Memory/0.9.0). See the [0.9.0 release notes](../releases/0.9.md) for its release boundary and upgrade guidance.

Use it for fast application tests, examples, and transient state when your assertions do not depend on SQL translation, collation, constraints, transactions, or provider-specific behavior. Keep provider-backed tests for those concerns.

## Install

Keep the Memory and core package versions aligned:

```bash
dotnet add package DataLinq
dotnet add package DataLinq.Memory
```

The package targets .NET 8, .NET 9, and .NET 10.

## Create, seed, and query

Construct the memory database before creating generated mutable rows. Construction binds the generated metadata used by the mutable accessors.

```csharp
var memory = new MemoryDatabase<MyDatabase>();

memory.Seed<Employee>(
[
    new MutableEmployee
    {
        Id = new EmployeeId(1),
        Name = "Ada"
    },
    new MutableEmployee
    {
        Id = new EmployeeId(2),
        Name = "Grace"
    }
]);

var db = memory.Query();

var names = db.Employees
    .Where(employee => employee.Id != new EmployeeId(2))
    .Select(employee => employee.Name)
    .ToArray();

var ada = memory.Find<Employee>(new EmployeeId(1));
```

Each table can be seeded once. `Seed` snapshots and validates the supplied generated mutable rows before publishing the table, so later changes to those mutable objects do not change the memory store. Duplicate keys and invalid values fail the seed instead of publishing partial state.

`Find<TModel>(object)` supports one non-null primary-key column. It accepts the public model-side key type, including a scalar-converter-backed typed ID, and returns the same cached immutable instance on repeated hits.

Memory stores canonical provider CLR values but has no SQL physical/wire codec. A Guid-backed typed ID still uses its scalar converter; `[GuidStorage]` matters when the same model runs against SQLite/MySQL/MariaDB, not while Memory holds the canonical `Guid`. See [Scalar Converters and Typed IDs](../Scalar%20Converters%20and%20Typed%20IDs.md).

## Supported query boundary

DataLinq.Memory 0.9.0 supports a deliberately small capability-gated subset:

- one generated root table
- the documented `int` comparisons and local `Contains` membership
- direct `Guid` and Guid-backed typed-ID equality and inequality
- `&&`, `||`, and `!` over supported predicates
- one sufficient primary-key ordering with the documented `Skip` and `Take` forms
- direct scalar projection to the selected column's exact model type, including strings, nullable values, `Guid`, and converter-backed typed IDs
- `Any`, `Count`, `Single`, `SingleOrDefault`, and ordered `First`/`FirstOrDefault` within the supported shape

Unsupported shapes throw `QueryBackendCapabilityException` before Memory row work. This includes general LINQ, `ThenBy`, arbitrary ordering, widened/boxed/computed/anonymous projections, `Last`, joins, grouping, relation traversal, and terminals after paging.

## Deliberate non-features

The experimental Memory backend has no Memory-owned:

- insert, update, delete, or save operation after seeding
- transaction, rollback, constraint, or generated-key behavior
- connection, command, provider, or raw-SQL API
- persistence, reset, snapshot, or durability contract

`DataLinq.Memory` is not SQLite `:memory:`. SQLite in-memory mode is still the real SQLite engine and remains appropriate when a test needs SQL translation, SQLite types, constraints, or transactions.

## AOT and browser use

The Memory runtime has no SQL-provider or native-database dependency. The supported generated-model smoke path is exercised under Native AOT, full trimming, and Blazor WebAssembly. That is evidence for the documented experimental Memory path, not a claim that arbitrary application code or arbitrary LINQ is AOT-compatible.
