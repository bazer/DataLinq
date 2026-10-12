# Dependency Injection and Unit of Work

These are implemented **0.10 development APIs**, not yet released. Registration lives in the existing core and provider packages. Generic Host and ASP.NET Core supply their usual container; DataLinq requires no separate hosting package. The examples assume an existing generated `Employees` database model.

## Register a database

```csharp
using DataLinq.Extensions.DependencyInjection;
using DataLinq.SQLite.Extensions.DependencyInjection;

builder.Services.AddDataLinq<Employees>()
    .UseSQLite("Data Source=employees.db");
```

Choose the provider explicitly. MySQL uses `DataLinq.MySql.Extensions.DependencyInjection` and `UseMySql`; MariaDB uses `DataLinq.MariaDB.Extensions.DependencyInjection` and `UseMariaDB`. Both are in the `DataLinq.MySql` package.

Construction is lazy. Resolving the database, read model, read access or unit-of-work factory activates the same singleton graph. Inject `Employees` for the read model, `Database<Employees>` for the database API, or `IDataLinqUnitOfWorkFactory<Employees>` for owned operations. The selected concrete database type, `ReadOnlyAccess<Employees>` and concrete factory are also aliases. A scope does not create another database or cache. Transactions are separate per owned operation and cannot execute concurrently within one session.

Registration does not provision or validate a schema. Perform application setup explicitly before querying.

## Configuration and keys

```csharp
using DataLinq;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.SQLite.Extensions.DependencyInjection;

builder.Services.AddDataLinq<Employees>("primary")
    .UseSQLite()
    .Bind(builder.Configuration.GetSection("DataLinq:Primary"))
    .Configure(options =>
    {
        options.ConnectionStringName = "Employees";
        options.Execution = new DataLinqExecutionOptions
        {
            RecoveryRollbackTimeout = TimeSpan.FromSeconds(15)
        };
    })
    .AsDefault();
```

Supported settings are `ConnectionString`, `ConnectionStringName`, `DatabaseName` and `Execution:RecoveryRollbackTimeout`. At first activation, defaults are followed by all bindings in order, then all code callbacks in order, even when fluent calls are interleaved. Settings are validated and captured before provider construction. Configuration reload or retained-option mutation cannot reconfigure an activated database.

Setting either connection property in code replaces the other selection. Binding both without an override is ambiguous and fails. A named selection resolves through the application's `IConfiguration` under `ConnectionStrings`; a literal needs no configuration service. `UseSQLiteConnectionString("Employees")` is the named shorthand, with corresponding MySQL/MariaDB methods. An available host `ILoggerFactory` is supplied automatically.

Identity is the generated model type plus its optional key. Different keys can select different providers/servers for the same model. Use `[FromKeyedServices("primary")]` in constructor injection or `GetRequiredKeyedService<T>("primary")` for explicit lookup. Only `AsDefault()` exposes a keyed identity through unnamed injection, sharing its existing graph. Duplicate identities, conflicting defaults and competing database sources are rejected.

## Owners and participants

| Contract | Rights |
| --- | --- |
| `IDataLinqSession<TDatabase>` | Query, mutate, execute prepared queries, inspect diagnostics and request rollback within an existing operation. |
| `IDataLinqUnitOfWork<TDatabase>` | A session plus commit, rollback and sync/async disposal. |
| `IDataLinqUnitOfWorkFactory<TDatabase>` | Create logical owners or run callbacks whose completion and cleanup it owns. |

`Transaction<TDatabase>` implements the owner directly. Factory `Create()` is synchronous and creates a logical transaction; physical initialization is lazy. Manual owners must finish and dispose their work. Callback helpers own that boundary:

```csharp
using DataLinq.Interfaces;
using DataLinq.Linq;

public sealed class DepartmentReader(IDataLinqUnitOfWorkFactory<Employees> work)
{
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        work.ExecuteAsync((session, token) =>
            session.Query().Departments.CountAsync(cancellationToken: token).AsTask(),
            cancellationToken: cancellationToken);
}
```

`Execute` supports actions and ordinary values. `ExecuteAsync` supports `Task` and `Task<T>` callbacks, with or without a callback token parameter. Await every operation and finish/dispose every enumerator within the callback. Completion drains admitted work and performs cleanup before returning. Synchronous helpers use synchronous provider completion; async helpers use existing managed async execution.

Pass the current session explicitly to nested services. Their reads and writes then use that transaction, including provisional writes. Calling another service's owning overload starts independent work through its own factory. There is no ambient transaction, savepoint or automatic distributed transaction.

The participant contract exposes no provider handle, completion or disposal. Generated mutation helpers accept sessions and preserve returned immutable models. Prepared scalar/row and sequence plans use `session.Execute(prepared, argument)` or `session.ExecuteAsync(prepared, argument, token)`. Preparation can use another registration of the same model; execution uses the supplied session. Capture, cancellation, reader lifetime and provider capability restrictions remain. Memory stays a separate read-only provider: it gains neither SQL transaction ownership nor prepared SQL execution. Fake units of work and DI testing replacements remain outside W4.

## Rollback intent and result data

`session.RequestRollback(reason)` is the explicit business veto. It synchronously records the first non-null reason without provider I/O. An active command or reader may finish its admitted work, but new execution and commit are rejected. Completion admission, helper closure, terminal state or disposal reject a late request. A request cannot be undone or its first reason replaced.

Returned failure objects are data. A custom failure or native rollback result without a request does not veto commit. Ignoring a request's returned marker cannot remove the veto. General owners throw `DataLinqRollbackRequestedException` after veto-driven recovery and cleanup; operational failures retain their ordinary exceptions and diagnostics.

Native results in `DataLinq.Mutation` are optional:

```csharp
using DataLinq.Mutation;

TransactionResult<string?> success = (string?)null;
TransactionResult<Unit> noValue = TransactionResult.Success();

TransactionResult<int> outcome = work.ExecuteResult(session =>
{
    session.RequestRollback("original business reason");
    return TransactionResult<int>.FromRollback(new("public result message"));
});
```

`ExecuteResult` / `ExecuteResultAsync` explicitly validate the native result before completion. After a veto, they return rollback data only when this owning boundary confirms rollback (or no provider work started) and cleanup succeeds. Success plus a veto throws; an invalid default result throws; failed recovery or cleanup remains exceptional. Without a veto, valid failure data can return after commit. A mapped result reason may differ from the original first transaction reason.

`TransactionResult<T>` uses string reasons; `TransactionResult<T,TFailure>` accepts another non-null reason type. Nullable/default success payloads are valid. `TryUnwrap`, `IsSuccess`, `SuccessValue` and `Reason` inspect the result; uninitialized results and wrong-case access throw. Catching an inspection exception does not itself poison a transaction. Use explicit `FromSuccess` / `FromRollback` when a rollback marker is itself the success type and implicit conversions overlap.

## Generated services

Declare a concrete top-level partial class with `IDataLinqService<TDatabase>`, a matching constructor factory and private business methods:

```csharp
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Linq;

public sealed partial class DepartmentService(
    IDataLinqUnitOfWorkFactory<Employees> work) : IDataLinqService<Employees>
{
    [DataLinqOperation]
    private Task<int> CountCoreAsync(
        IDataLinqSession<Employees> session, CancellationToken cancellationToken) =>
        session.Query().Departments.CountAsync(cancellationToken: cancellationToken).AsTask();
}
```

This generates owning `CountAsync(CancellationToken cancellationToken = default)` and participating `CountAsync(IDataLinqSession<Employees> session, CancellationToken cancellationToken = default)`. Participation checks admission and uses the supplied session without completing or disposing it, even when the service's constructor factory selects another database.

Synchronous bodies use `NameCore`; async bodies use `NameCoreAsync` and a final required `CancellationToken`. Supported returns are void, ordinary/custom/native values, `Task` and `Task<T>`. `[DataLinqOperation(ResultAware = true)]` explicitly selects native-result handling for the owner; simply declaring a native result does not. Participants always leave completion to the caller.

A traditional constructor designates one matching factory field or readable property with `[DataLinqFactory]`. Other dependencies use ordinary DI, optional defaults or explicit `[FromKeyedServices]`. Direct construction works. `DLG005` diagnoses invalid authoring, including ambiguous factories/constructors, mismatched sessions and naming collisions. Supported declarations are non-generic top-level classes and non-generic private instance bodies; ref/out/in, pointer/ref-like signatures, async void, ValueTask/custom awaitables and operation-parameter attributes are excluded.

Choose the application service lifetime explicitly:

```csharp
builder.Services.AddDataLinq<Employees>("primary")
    .UseSQLiteConnectionString("Employees")
    .AsDefault()
    .AddScopedService<DepartmentService>();
```

`AddSingletonService`, `AddScopedService` and `AddTransientService` bind the selected registration's factory through generated construction code. Each owning call receives its own session, so a singleton can serve concurrent operations; its authored fields must still be safe for concurrency. Standard scope validation remains effective.

## Ownership and shutdown

Provider-created databases and `UseFactory` results are container-owned. `UseInstance(database)` borrows: container shutdown leaves disposal to the application. `UseInstance(database, DataLinqOwnership.Container)` transfers ownership only after successful first DataLinq activation. Never-activated instances and failures before handoff remain application-owned. A later application-service constructor failure does not undo a successful infrastructure handoff. Instances keep their existing settings.

Owned aliases share one underlying database cleanup attempt. Owned database subclasses replacing disposal dispatch and owned read roots with arbitrary authored disposal are rejected; borrowed instance registration supports those custom roots.

Non-disposable singleton/scoped services can share keyed/default identity. **Disposable singleton/scoped services require one identity:** unnamed, or keyed without `AsDefault`. Both fluent orders reject the unsupported combination. Disposable transients can expose both lookups because each creates a separate activation with one container cleanup invocation. Authored disposal is preserved; async-only disposables require asynchronous container shutdown.

Stop and await application work before disposing its container. Generic Host workers should finish cancellation/recovery in `StopAsync`; ASP.NET Core graceful shutdown should finish admitted requests before root disposal. Hosting may select asynchronous root cleanup even when its outer `Dispose()` is synchronous. Direct DI disposal follows its own sync/async rules.

See [Async Execution](Async%20Execution.md) for overlap/recovery budgets and [Transactions](Transactions.md) for mutable/cache lifecycle behavior.
