> [!WARNING]
> This document is roadmap/specification material. It describes planned behavior, not shipped DataLinq behavior.

# Specification: Model Testing and Mocking Support

**Status:** Draft specification.
**Goal:** Make DataLinq application code testable without a live database when the test is about business behavior, while preserving provider-backed tests for SQL translation, schema, transaction, and database-specific behavior.

**Related work:**

- `docs/dev-plans/architecture/Dependency Injection and Hosting Integration.md`
- `docs/dev-plans/providers-and-features/In-Memory Provider.md`
- `docs/contributing/DataLinq.Testing.CLI.md`

## Problem Statement

DataLinq has a decent integration-testing story:

- SQLite can be used for fast local database-backed tests.
- The Testing CLI can run provider-backed suites across SQLite, MySQL, and MariaDB targets.
- The repo has seeded Employees fixtures and deterministic Bogus data for compliance-style tests.

That is not the same as a mocking story.

Today, testing application code that consumes immutable DataLinq models is awkward because generated immutable instances are tied to runtime infrastructure:

- immutable models need `IRowData`
- immutable models need `IDataSourceAccess`
- relation properties lazy-load through metadata, provider state, and table cache
- `RowData` is constructed from a data reader, not simple test values
- `ImmutableRelationMock<T>` exists but most members throw
- generated model interfaces currently describe scalar values, but are not sufficient as clean POCO-style test contracts for graphs with relations

That makes tests fall into a bad binary:

- use a real database even when testing pure business logic
- or mock around DataLinq with fragile hand-written substitutes that do not behave like DataLinq models

The concrete pain point is relations. Creating an immutable instance with a realistic relation graph should not require booting SQLite, creating schema, seeding rows, and relying on cache lazy-load behavior. That is integration testing, not a unit test.

## Design Position

The central opinion:

> DataLinq should provide first-class test doubles and model graph builders. Users should not be expected to mock generated immutable classes directly.

Directly mocking generated immutable models is the wrong default because the generated classes are intentionally tied to metadata, row data, and relation loading. Generic mocking frameworks can technically derive from some generated types, but the constructor and lazy-loading dependencies make the result brittle and misleading.

The better approach is a layered test surface:

1. Pure object graph builders for business logic.
2. Metadata-aware immutable builders for tests that need real DataLinq model behavior.
3. Fake read stores and fake units of work for application services.
4. Provider-backed fixtures for query translation and database behavior.

This split matters. Mocked DataLinq tests should prove application behavior. They should not pretend to prove SQL translation, database defaults, isolation, indexes, generated columns, provider UUID encoding, or schema drift.

## Design Principles

- **Separate test intent:** business logic tests, application-service tests, query translation tests, and provider compliance tests need different tools.
- **Do not mock `IQueryable` as proof of SQL behavior:** LINQ-to-Objects and DataLinq SQL translation are different runtimes.
- **Make relation graphs easy:** one-to-one and one-to-many relation fixtures should be first-class.
- **Use generated metadata where correctness matters:** metadata-aware builders should validate property names, column types, required values, and primary keys.
- **Keep pure tests pure:** users should not need a connection string, provider package, or database process to test object graph behavior.
- **Align with DI:** fake read stores and fake units of work should match the planned DI abstractions.
- **Prefer deterministic tests:** test helpers should support deterministic IDs, clocks, UUIDs, default values, and seed data.
- **Keep provider-backed tests available:** SQLite-in-memory and Testing CLI remain the right path for runtime/provider validation.

## Non-Goals

- No claim that fake query execution proves provider SQL translation.
- No attempt to make arbitrary mocking frameworks understand all DataLinq internals.
- No automatic replacement of provider-backed tests.
- No full ACID in-memory provider in this plan. That belongs to the in-memory provider plan.
- No hidden database creation from unit-test helpers.
- No test helper that silently ignores relation/key mismatches.

## Current Runtime Constraints

Relevant current shapes:

- `Immutable<T, M>` stores `IRowData` and `IDataSourceAccess`.
- scalar getters call `GetValue(...)` / `GetNullableValue(...)` against row data.
- relation getters call generated `GetImmutableRelation...` or `GetImmutableForeignKey...` helpers.
- relations use `RelationProperty`, provider table cache, primary keys, and relation keys.
- `InstanceFactory.NewImmutableRow(...)` creates generated immutable instances from row data and source access.
- `DbRead<T>` inherits queryable behavior and is backed by DataLinq's query provider.

This is good production architecture, but poor direct mocking surface.

The first design mistake to avoid is papering over this by saying "just use Moq." A mock that bypasses row data, metadata, and relation behavior might work for one property access but will fail once code calls `PrimaryKeys()`, `GetValues()`, `Mutate()`, relation enumeration, or equality.

## Testing Layers

### Layer 1: Pure Model Shape Tests

Some application code only needs a model-shaped object:

```csharp
public static string DisplayName(IEmployee employee)
    => $"{employee.first_name} {employee.last_name}";
```

For this, the best test object is not a real immutable row. It is a plain object or generated lightweight test model that implements a clean interface.

Desired API:

```csharp
var employee = DataLinqTest.Model<IEmployee>()
    .With(x => x.emp_no, 1001)
    .With(x => x.first_name, "Ada")
    .With(x => x.last_name, "Lovelace")
    .Build();
```

This requires better generated test contracts. The current generated interfaces are useful, but they are still DataLinq-shaped and do not include a full relation graph contract by default.

Possible generator option:

```csharp
[Interface("IEmployee", GenerateInterface = true)]
[TestModelInterface("IEmployeeShape", IncludeRelations = true)]
public abstract partial class Employee(...)
```

or a database-level option:

```csharp
[GenerateTestModelInterfaces(IncludeRelations = true)]
public partial class EmployeesDb(...)
```

This is optional. We should not require every user to generate separate interfaces before the rest of the testing story is useful.

### Layer 2: Immutable Row Builders

When a test needs actual DataLinq immutable behavior, provide a metadata-aware builder:

```csharp
var employee = DataLinqTest.Immutable<Employee, EmployeesDb>()
    .With(x => x.emp_no, 1001)
    .With(x => x.first_name, "Ada")
    .With(x => x.last_name, "Lovelace")
    .Build();
```

This builder should:

- initialize generated metadata if needed
- construct fake row data from scalar values
- create the real generated immutable type through `InstanceFactory`
- validate required scalar values unless disabled
- populate defaults where metadata supports it
- produce correct primary keys
- support `GetValues()`, `PrimaryKeys()`, equality, and `Mutate()`

Implementation likely needs:

- `TestRowData` or a public row-data factory
- `TestDataSourceAccess`
- `TestDatabaseProvider`
- minimal `TableCache` or fake cache behavior for relations

The builder must not need a database connection.

### Layer 3: Relation Graph Builders

Relations are the real feature, not an afterthought.

Desired API:

```csharp
using var graph = DataLinqTest.Graph<EmployeesDb>()
    .Add<Employee>("ada", e => e
        .With(x => x.emp_no, 1001)
        .With(x => x.first_name, "Ada"))
    .Add<Manager>("manager", m => m
        .With(x => x.emp_no, 1001)
        .With(x => x.dept_fk, "d001"))
    .Relate<Employee, Manager>(
        "ada",
        x => x.dept_manager,
        "manager")
    .Build();

var employee = graph.Get<Employee>("ada");
Assert.That(employee.dept_manager.Count).IsEqualTo(1);
```

The graph builder should support:

- one-to-many relations
- many-to-one / foreign-key relations
- composite primary keys
- nullable foreign keys
- empty relations
- missing relation failures with clear diagnostics
- relation lookup by label
- relation lookup by primary key
- deterministic ordering for collection relations

The graph builder should not merely stuff relation properties with hand-written objects. It should use DataLinq relation metadata so key direction mistakes are caught.

### Layer 4: Fake Read Store

Application query services often depend on read access rather than individual model instances.

Desired API:

```csharp
var store = DataLinqTest.ReadStore<EmployeesDb>()
    .Seed<Employee>(employees)
    .Seed<Department>(departments)
    .Build();

var db = store.Query();
var active = db.Employees.Where(x => x.IsDeleted != true).ToList();
```

This is the sharpest design area.

There are two possible strategies:

1. LINQ-to-Objects fake query provider
2. metadata-aware fake provider that supports a DataLinq-like subset

For the first slice, LINQ-to-Objects is probably acceptable if the API and docs are brutally clear:

> Fake read stores test application logic. They do not test DataLinq SQL translation.

The fake read store should intentionally expose itself as fake, for example:

```csharp
DataLinqTest.ReadStore<EmployeesDb>()
    .UseLinqToObjects()
```

That wording is not cosmetic. It prevents a common ORM testing failure mode: passing unit tests that exercise LINQ-to-Objects while production fails because the provider cannot translate the expression.

### Layer 5: Fake Unit of Work

The DI/hosting plan proposes explicit unit-of-work abstractions. Testing should provide fakes for those interfaces.

Desired API:

```csharp
var unit = DataLinqTest.UnitOfWork<EmployeesDb>()
    .Seed<Employee>(existingEmployees)
    .Build();

handler.Handle(command, unit);

Assert.That(unit.Inserted<Employee>()).HasCount().EqualTo(1);
Assert.That(unit.WasCommitted).IsTrue();
```

The fake unit of work should:

- expose a fake read root
- record inserts
- record updates
- record saves
- record deletes
- record commit/rollback/dispose
- optionally apply mutations to the fake read store after commit
- optionally fail on commit for error-path tests

It should not try to simulate every provider transaction rule. Provider-backed tests still own that.

## Relation and Reference Test Doubles

The current `ImmutableRelationMock<T>` should be completed or replaced.

Required collection relation behavior:

```csharp
public sealed class TestImmutableRelation<T> : IImmutableRelation<T>
    where T : IModelInstance
{
    public int Count { get; }
    public ImmutableArray<DataLinqKey> Keys { get; }
    public ImmutableArray<T> Values { get; }
    public T? this[DataLinqKey key] { get; }
    public T? Get(DataLinqKey key);
    public bool ContainsKey(DataLinqKey key);
    public IEnumerable<KeyValuePair<DataLinqKey, T>> AsEnumerable();
    public FrozenDictionary<DataLinqKey, T> ToFrozenDictionary();
    public void Clear();
}
```

Required reference relation behavior:

```csharp
public sealed class TestImmutableForeignKey<T> : IImmutableForeignKey<T>
    where T : IImmutableInstance
{
    public T? Value { get; }
    public void Clear();
}
```

Useful helpers:

```csharp
var relation = DataLinqTest.Relation.Of(employee1, employee2);
var reference = DataLinqTest.Reference.Of(department);
```

These are low-level helpers. They help users who already have model instances. The graph builder should be the preferred high-level API.

## Generated Test Contracts

The current `[Interface]` model support helps, but it is not sufficient as a complete test contract strategy.

Problems:

- generated interfaces focus on scalar value properties
- relation properties are usually absent
- interfaces inherit DataLinq model-instance APIs, which can be too heavy for pure business tests
- mutable and immutable models both implement the interface, but plain test objects are still not ergonomic

Possible additions:

### Option A: Relation-Aware Interfaces

Extend generated interfaces to include relation properties:

```csharp
[Interface("IEmployee", GenerateInterface = true, IncludeRelations = true)]
public abstract partial class Employee(...)
```

Risk: existing users may not want relation properties on interfaces because it increases coupling and can create cycles.

### Option B: Separate Test Shape Interfaces

Generate a second interface that is deliberately POCO-shaped:

```csharp
[TestModelInterface("IEmployeeShape", IncludeRelations = true)]
public abstract partial class Employee(...)
```

This avoids changing the meaning of existing interfaces.

### Option C: No Generator Change First

Start with builders and fake relation objects only. Revisit generated interfaces after real usage.

Recommended stance: implement builders first, then add generated test-shape interfaces if builders alone still leave too much friction.

## DI Test Integration

Testing helpers should align with the DI plan.

Desired API:

```csharp
services.AddDataLinqTesting<EmployeesDb>(test =>
{
    test.UseFakeReadStore()
        .Seed<Employee>(employees);
});
```

Replacement helpers:

```csharp
services.ReplaceDataLinqWithFake<EmployeesDb>(fake =>
{
    fake.Seed<Employee>(employees);
});
```

SQLite-backed helper:

```csharp
services.ReplaceDataLinqWithSqliteInMemory<EmployeesDb>(sqlite =>
{
    sqlite.CreateSchema();
    sqlite.Seed(seed => seed.Insert(employees));
});
```

The fake and SQLite-backed helpers should have different names. A fake read store and an in-memory SQLite database have different guarantees.

## Query Testing Helpers

There are three different query-test needs:

### Application Filtering Tests

Use fake read store / LINQ-to-Objects:

```csharp
var result = service.FindActiveEmployees();
```

This proves service logic only.

### Translation Shape Tests

Use query translation assertions:

```csharp
DataLinqAssert.Query<EmployeesDb>(db => db.Employees.Where(x => x.emp_no == 1001))
    .TranslatesToSqlContaining("WHERE")
    .WithParameter(1001);
```

This should use the real DataLinq query pipeline without requiring a live database when possible. If the current pipeline cannot do this cleanly, that is another reason to pursue the query-plan/translation isolation work later.

### Provider Behavior Tests

Use SQLite/server-backed fixtures:

```csharp
using var database = EmployeesTestDatabase.Create(provider, scenario);
```

This proves actual provider behavior.

The docs must keep these three categories separate. Otherwise users will write fast tests that prove the wrong thing.

## Deterministic Test Data

DataLinq should make deterministic setup easy:

- deterministic auto-increment allocation in fakes
- deterministic `Guid`/UUID generation
- deterministic clock/default-value providers
- deterministic seeded relation graph factories
- explicit default-value population
- easy test data reset

Possible API:

```csharp
DataLinqTest.Graph<EmployeesDb>(options =>
{
    options.Clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    options.GuidFactory = new SequentialGuidFactory();
    options.AutoIncrementStartsAt(1000);
});
```

This overlaps with future default-value and UUID work. The test API should integrate with those features rather than inventing separate semantics.

## Cache and State Testing

Some users need to test cache-sensitive application behavior.

Testing helpers should expose:

- clear all caches
- clear one table cache
- clear relation caches
- inspect cache hit/load counters
- disable cache for a fake or provider-backed test
- force external invalidation events where supported

These helpers should live near runtime diagnostics, not as ad hoc reflection utilities.

## Error-Path Testing

Application tests should be able to simulate DataLinq failures:

- query failure
- insert/update/delete failure
- commit failure
- rollback failure
- schema validation failure
- relation load failure
- stale cache invalidation event

Desired API:

```csharp
var unit = DataLinqTest.UnitOfWork<EmployeesDb>()
    .FailCommit(new TimeoutException("database timeout"))
    .Build();
```

This is useful for application resilience tests. Provider-backed tests should still own actual provider exception behavior.

## Package Shape

Possible package split:

```text
DataLinq.Testing
DataLinq.Extensions.DependencyInjection.Testing
```

`DataLinq.Testing` should contain:

- relation/reference fakes
- row-data builders
- immutable builders
- graph builders
- fake read stores
- fake unit-of-work types once DI abstractions exist
- deterministic data helpers

`DataLinq.Extensions.DependencyInjection.Testing` should contain:

- `IServiceCollection` replacement helpers
- fake DataLinq registration helpers
- SQLite-in-memory DI helpers

Avoid making the runtime package carry testing dependencies.

## Implementation Slices

### Slice 1: Complete Relation Test Doubles

- Replace or complete `ImmutableRelationMock<T>`.
- Add `TestImmutableRelation<T>`.
- Add `TestImmutableForeignKey<T>`.
- Ensure relation doubles support count, values, keys, indexer, enumeration, `Single`, `First`, `ToFrozenDictionary`, and `Clear`.
- Add tests for empty, single, multiple, duplicate-key, and null-reference behavior.

### Slice 2: Row Data and Immutable Builders

- Add `TestRowData`.
- Add scalar `Immutable<TModel, TDatabase>` builder.
- Use generated metadata for value lookup and primary-key calculation.
- Support required values and defaults.
- Add tests for scalar getters, nullable values, primary keys, equality, `GetValues()`, and `Mutate()`.

### Slice 3: Graph Builder

- Add `DataLinqTest.Graph<TDatabase>()`.
- Support labeled nodes.
- Support relation wiring by expression.
- Resolve relation direction through metadata.
- Support composite keys.
- Add diagnostics for missing keys, wrong relation direction, wrong model type, and duplicate labels.
- Add tests for one-to-many, many-to-one, empty relation, composite key, and nullable foreign key graphs.

### Slice 4: Fake Read Store

- Add fake read root construction for generated database models.
- Seed tables from immutable instances, mutable instances, or builder rows.
- Use LINQ-to-Objects query execution with explicit fake naming.
- Document that fake read stores do not prove DataLinq SQL translation.
- Add tests for filtering, ordering, projection, and relation traversal over seeded graphs.

### Slice 5: Fake Unit of Work

- Implement after the DI/unit-of-work abstractions land.
- Record insert/update/save/delete operations.
- Support commit, rollback, disposal, and failure injection.
- Optionally apply committed mutations to fake read store.
- Add tests for command-handler style workflows.

### Slice 6: DI Test Helpers

- Add `AddDataLinqTesting<TDatabase>()`.
- Add `ReplaceDataLinqWithFake<TDatabase>()`.
- Add `ReplaceDataLinqWithSqliteInMemory<TDatabase>()`.
- Add tests with `ServiceCollection` and the planned DI package.

### Slice 7: Query Translation Assertions

- Add SQL/parameter assertion helpers if the query pipeline can expose translation without executing a database command.
- Keep this separate from fake read-store tests.
- Add tests that unsupported queries fail with useful diagnostics.

## Test Plan

Unit tests:

- relation test doubles implement the full relation interface
- reference relation test double supports null and non-null values
- immutable builder creates real generated immutable instances
- immutable builder validates required scalar values
- immutable builder calculates primary keys correctly
- graph builder wires collection relations
- graph builder wires reference relations
- graph builder catches relation/key mismatches
- fake read store executes simple LINQ-to-Objects queries
- fake read store relation traversal works
- fake unit of work records writes and commit/rollback state
- fake unit of work supports failure injection

Integration tests:

- DI replacement helper swaps real DataLinq registration for fake registration
- SQLite-in-memory helper creates schema and seeds data
- provider-backed tests remain the recommended path for SQL translation and provider behavior

Documentation tests or examples:

- pure business logic test with shape interface
- immutable scalar builder
- immutable graph with relations
- application query service using fake read store
- command handler using fake unit of work
- provider-backed test using SQLite-in-memory

## Risks and Sharp Edges

- **False confidence from fake LINQ:** this is the biggest risk. The API and docs must say that fake read stores use LINQ-to-Objects and do not validate SQL translation.
- **Relation graph complexity:** relations are metadata-heavy. A simplistic graph builder will become another broken mock layer.
- **Generated interface churn:** changing generated interfaces can break user code. Prefer opt-in test-shape interfaces if needed.
- **Cache behavior mismatch:** pure graph tests should not pretend to model cache invalidation unless they explicitly use fake cache/state helpers.
- **Overlapping in-memory provider plan:** a full in-memory provider is valuable, but it is not the same as lightweight model mocking.
- **Too many helper APIs:** testing APIs should form a small ladder, not a bag of clever utilities.

## Open Questions

- Should `ImmutableRelationMock<T>` be fixed in place or replaced with a new `TestImmutableRelation<T>` type?
- Should the scalar immutable builder live in `DataLinq.Testing` or in the runtime package behind a low-level factory?
- Should generated test-shape interfaces include relation properties by default, by opt-in, or not at all?
- Should fake read stores expose `DbRead<T>` or a separate generated test database root?
- Should fake read-store LINQ support be deliberately limited to avoid accidental translation claims?
- Should fake unit of work apply changes immediately, only on commit, or support both modes?
- Should deterministic defaults be driven by DataLinq metadata defaults or separate test options?
- Should query translation assertions wait until the query-plan/Remotion isolation work?
