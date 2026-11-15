# Testing applications with SQLite

On the 0.10 development branch, add `DataLinq.Testing` to the test project that references your generated model. Each `SqliteTestDatabase<TDatabase>` owns a fresh, named SQLite in-memory database. The fixture creates the schema from the model's generated metadata and exposes the same unit-of-work interface that an application uses in production. It works with any test framework.

## Arrange related rows in one transaction

```csharp
using DataLinq.Testing;

await using var fixture = await SqliteTestDatabase<EmployeesDb>.CreateAsync(token);
var (employee, salary) = await fixture.SeedAsync(async (session, ct) =>
{
    var employee = await session.InsertAsync(new MutableEmployee
    {
        first_name = "Ada", last_name = "Lovelace",
        birth_date = new DateOnly(1980, 1, 1), hire_date = new DateOnly(2020, 1, 1),
        gender = Employee.Employeegender.F
    }, ct);
    var salary = await session.InsertAsync(new MutableSalaries
    {
        emp_no = employee.emp_no!.Value,
        FromDate = new DateOnly(2026, 1, 1),
        ToDate = new DateOnly(2026, 12, 31),
        salary = 75000
    }, ct);
    return (employee, salary);
}, token);

var storedSalary = fixture.Query().Employees.Single().salaries.Single();
```

Each seed call owns one transaction. The callback can read its own writes and use the key returned by a principal insert for the dependent. The call returns its result after commit and cleanup. An exception in the callback rolls back earlier writes. Synchronous tests can use `Create()` and `Seed(session => ...)`. Pass one callback to `SeedAsync`. To run several steps in one transaction, await them in order inside that callback. Both async overloads reject multicast delegates before running any step.

## Run the application

Pass `fixture.UnitOfWorkFactory` to the handler's constructor. To test a hosted application, call the replacement on the same service collection that received `AddDataLinq`, before building the host. For hosts built from a common template, register DataLinq separately on each host that needs its own fixture. A collection of copied descriptors shares the template’s registration and cannot be used for replacement.

```csharp
using DataLinq.Testing;

await using var fixture = await SqliteTestDatabase<ShopDb>.CreateAsync(token);
var builder = Host.CreateApplicationBuilder();
RegisterShopServices(builder.Services);
builder.Services.ReplaceDataLinqWithSqliteInMemory(fixture, serviceKey: "orders");
using var host = builder.Build();
await host.StartAsync(token);
using var scope = host.Services.CreateScope();
var handler = scope.ServiceProvider.GetRequiredService<SubmitOrder>();
await handler.ExecuteAsync(orderId, token);
await host.StopAsync(token);
```

Omit the key to replace the model's default registration. A keyed registration becomes the default only through `AsDefault()`. An unknown explicit key or missing default fails during replacement. The fixture backs the selected registration's generated services, database, query root and factory, including its default aliases. Enabled startup validation inspects that database. The host borrows the fixture. Dispose scopes, readers, transactions and the host before disposing the fixture.

For rollback coverage, arrange a constraint violation on the second write and assert the handler's outcome. Read through `fixture.Query()` to check what the application sees. To verify persistence independently, read the underlying SQLite store through `fixture.Database`, or call `fixture.Database.Provider.State.ClearCache()` before requerying.

## SQLite coverage

The fixture uses shared-cache connections with foreign keys enabled and pooling disabled. Its keeper holds the store until fixture disposal. A separately opened connection can keep the store alive until that connection closes too.

Supply a model and view SQL that SQLite can execute. During creation the fixture prepares a zero-row query against each view to resolve its sources and declared columns. SQLite provisioning currently rejects applicable model check constraints, physical indexes declared on views, and other index shapes it cannot reproduce. The package carries the SQLite native runtime through `DataLinq.SQLite`. The test host must support that native library. Exercise MySQL/MariaDB dialects, collations, server defaults, locking and provider-specific failures against those servers. See [dependency injection and unit of work](Dependency%20Injection%20and%20Unit%20of%20Work.md) and [runtime schema validation](Runtime%20Schema%20Validation.md).
