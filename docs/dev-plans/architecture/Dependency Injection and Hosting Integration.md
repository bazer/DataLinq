> [!WARNING]
> This document is roadmap/specification material. It describes planned behavior, not shipped DataLinq behavior.

# Specification: Dependency Injection and Hosting Integration

**Status:** Accepted.
**Release horizon:** First post-0.9 adoption release.
**Last reviewed:** 2026-07-10.
**Dependency:** Native async provider execution and the 0.9 backend/source boundary should be stable before host integration freezes public service abstractions.
**Goal:** Make DataLinq straightforward to configure, validate, and consume from ASP.NET Core, generic host, background workers, Blazor, MAUI, Avalonia, and other .NET application surfaces without hiding database I/O or transaction boundaries.

**Related work:**

- `docs/dev-plans/architecture/Applications patterns.md`
- `docs/dev-plans/providers-and-features/Schema Validation Hooks.md`
- `docs/dev-plans/providers-and-features/UUID Storage Format Support.md`

## Problem Statement

DataLinq currently has a strong runtime shape but a weak application integration story.

Applications can construct provider-backed databases directly:

```csharp
var db = new MySqlDatabase<EmployeesDb>(connectionString);
```

That is simple, but it does not scale cleanly across real application surfaces:

- ASP.NET Core applications expect `IServiceCollection`, `IConfiguration`, `IHostApplicationBuilder`, logging, hosted services, and environment-specific options.
- Minimal APIs and controllers should be able to inject read access without hand-rolled static database holders.
- Background services need a safe way to create operation scopes and short-lived transactions.
- Startup validation should be opt-in and fail-fast when the application chooses that policy.
- Multiple databases or multiple registrations of the same model should be possible without naming hacks.
- Platform apps such as MAUI and Avalonia should use the same core DI primitives rather than separate magic APIs.

The current Blazor sample shows the problem clearly: it initializes a static `MySqlDatabase<EmployeesDb>` holder from configuration at startup. That works as a sample, but it is the wrong long-term pattern for a framework that already has a DI container and logging pipeline.

DataLinq should take responsibility for first-class registration, configuration, and startup validation. It should not copy Entity Framework's `DbContext` lifetime model blindly. DataLinq's cache, provider state, and explicit transaction model are different enough that pretending it is EF would produce the wrong defaults.

## Design Position

The central opinion:

> A provider-backed `Database<TDatabase>` should be an application-level singleton, while operation-specific read facades and write units of work should be scoped or explicitly created.

That position follows from the current runtime:

- `Database<T>` owns a provider and cache.
- `DatabaseProvider<T>` owns finalized metadata, state, logging configuration, and read-only access.
- `ReadOnlyAccess<TDatabase>` builds the generated database root over a read-only access object.
- `Transaction<TDatabase>` is explicit, disposable, and commit/rollback based.
- Convenience mutation methods on `Database<T>` already create short-lived transactions.

The DI design should preserve those semantics instead of flattening everything into a scoped context.

## Design Principles

- **Use the host's primitives:** integrate with `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Hosting`, and `Microsoft.Extensions.Logging`.
- **Keep core small:** avoid ASP.NET Core dependencies in core runtime packages. ASP.NET-specific helpers can be extension packages or thin optional layers.
- **Singleton database root:** register provider-backed `Database<TDatabase>` as singleton unless a provider has a documented reason not to.
- **Scoped read convenience:** allow scoped injection of generated read roots for ergonomic handlers and services.
- **Explicit writes:** do not start hidden request-wide transactions by default.
- **Policy-driven startup validation:** validation can fail startup, warn only, or be disabled per environment. It must be visible in the registration call.
- **Normal configuration sources:** connection strings should come from `IConfiguration`, options binding, user secrets, environment variables, Key Vault, or whatever the host already supports. Runtime apps should not be forced through `datalinq.json`.
- **No generic repository ceremony:** DataLinq already exposes useful query and mutation primitives. The integration should not wrap them in a weaker abstraction just because that pattern is familiar.
- **One cross-platform model first:** MAUI, Avalonia, WPF, WinUI, console apps, workers, and ASP.NET should all start from the same service registration model.

## Non-Goals

- No automatic migrations during application startup.
- No automatic schema repair.
- No hidden database connections during service registration unless explicitly requested.
- No source-generator database access.
- No default request-wide transaction middleware.
- No generic `IRepository<T>` abstraction.
- No special XAML framework package in the first slice unless a platform-specific need is proven.
- No replacement of `datalinq.json` tooling configuration. This plan is about runtime and host integration.

## Package Shape

The preferred package split is:

```text
DataLinq
DataLinq.Extensions.DependencyInjection
DataLinq.MySql.Extensions.DependencyInjection
DataLinq.SQLite.Extensions.DependencyInjection
```

The exact names can change, but the dependency direction should not:

- `DataLinq` remains the runtime core.
- `DataLinq.Extensions.DependencyInjection` owns core service registration abstractions, options, unit-of-work interfaces, and validation registration hooks.
- Provider packages add provider-specific `UseMySql`, `UseMariaDb`, and `UseSQLite` registration methods.
- ASP.NET Core-only helpers, if needed, should live in a separate package or namespace so core DI does not depend on the web stack.

The core DI package can depend on:

- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Options`
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions` if startup validation is implemented there

Avoid taking a hard dependency on `Microsoft.AspNetCore.*` for the first implementation.

## Public API Shape

### Basic ASP.NET Core / Generic Host

Recommended shape:

```csharp
builder.Services.AddDataLinq<EmployeesDb>(db =>
{
    db.UseMySql(builder.Configuration.GetConnectionString("employees")!);
    db.ValidateSchemaOnStartup(validation =>
    {
        validation.FailOnSeverity = SchemaDifferenceSeverity.Error;
    });
});
```

Equivalent fluent form:

```csharp
builder.Services
    .AddDataLinq<EmployeesDb>()
    .UseMySqlConnectionString("employees")
    .ValidateSchemaOnStartup();
```

The fluent form should resolve the named connection string from `IConfiguration` when the service provider is built. It should not read secrets at source-generation time.

### Options Binding

Applications should be able to bind provider and validation settings from normal host configuration:

```csharp
builder.Services
    .AddDataLinq<EmployeesDb>()
    .UseMySql()
    .Bind(builder.Configuration.GetSection("DataLinq:Employees"));
```

Possible configuration shape:

```json
{
  "DataLinq": {
    "Employees": {
      "Provider": "MySql",
      "ConnectionStringName": "employees",
      "ValidateOnStartup": true,
      "Validation": {
        "FailOnSeverity": "Error",
        "TreatValidationIssuesAsFailures": true
      }
    }
  }
}
```

Provider selection from configuration is useful, but it must not become an unbounded plugin loader. The application should still reference the provider package and call the provider registration extension so the supported provider set is explicit.

### Minimal API Read Usage

For simple read endpoints, inject the generated read root:

```csharp
app.MapGet("/employees", (EmployeesDb db) =>
    db.Employees.Take(25).ToList());
```

This is intentionally pleasant. Read-only database access is the common path, and DataLinq should not force handlers to inject a provider wrapper just to run a query.

The generated root injected this way must be read-only. Mutations should require a transaction or unit of work.

### Controller / Service Read Usage

Applications that prefer explicit infrastructure types can inject read access:

```csharp
public sealed class EmployeeQueries
{
    private readonly ReadOnlyAccess<EmployeesDb> access;

    public EmployeeQueries(ReadOnlyAccess<EmployeesDb> access)
    {
        this.access = access;
    }

    public IReadOnlyList<Employee> RecentEmployees()
    {
        return access.Query().Employees
            .OrderByDescending(x => x.Id)
            .Take(25)
            .ToList();
    }
}
```

This keeps query services lightweight and avoids generic repository boilerplate.

### Write Usage

Writes should be explicit:

```csharp
app.MapPost("/employees", (
    IDataLinqUnitOfWorkFactory<EmployeesDb> unitsOfWork,
    CreateEmployee command) =>
{
    using var unit = unitsOfWork.Begin();

    unit.Insert(new MutableEmployee
    {
        FirstName = command.FirstName,
        LastName = command.LastName
    });

    unit.Commit();

    return Results.Created();
});
```

For services that participate in a write operation but should not own commit:

```csharp
public interface IDataLinqSession<TDatabase>
{
    TDatabase Query();
    void Insert<TModel>(Mutable<TModel> model);
    void Update<TModel>(Mutable<TModel> model);
    void Save<TModel>(Mutable<TModel> model);
    void Delete<TModel>(TModel model);
    void Rollback();
}

public interface IDataLinqUnitOfWork<TDatabase> : IDataLinqSession<TDatabase>, IDisposable
{
    void Commit();
}

public interface IDataLinqUnitOfWorkFactory<TDatabase>
{
    IDataLinqUnitOfWork<TDatabase> Begin(
        Action<DataLinqUnitOfWorkOptions>? configure = null);
}
```

The participant/coordinator distinction from the older application-patterns draft is still good. What needs more caution is the ambient `AsyncLocal` model. Ambient transactions are convenient, but they are also a sharp edge in background work, streaming endpoints, Blazor Server circuits, and async fan-out.

Recommended first slice:

- implement explicit `IDataLinqUnitOfWorkFactory<TDatabase>`
- optionally add scoped `IDataLinqSession<TDatabase>` only when a unit of work has been explicitly started for the scope
- defer ambient `AsyncLocal` behavior until concrete scenarios prove it is worth the complexity

## Service Lifetimes

Default registrations:

| Service | Lifetime | Reason |
| --- | --- | --- |
| `Database<TDatabase>` | Singleton | Owns provider/cache/state and is expensive enough to treat as app-level infrastructure. |
| Provider-specific database, e.g. `MySqlDatabase<TDatabase>` | Singleton | Same object as the base database registration where possible. |
| `ReadOnlyAccess<TDatabase>` | Scoped or transient facade | Ergonomic read dependency. It may wrap singleton provider state but should feel operation-local. |
| Generated `TDatabase` read root | Scoped or transient | Convenience read model for handlers/services; must be read-only. |
| `IDataLinqUnitOfWorkFactory<TDatabase>` | Singleton | Creates explicit transactions from the singleton database root. |
| `IDataLinqUnitOfWork<TDatabase>` | Explicit/disposable, optionally scoped | Should not be silently created for every request. |
| Schema validation hosted service | Singleton hosted service | Runs once during host startup over registered targets. |

The important rule is that scoped services can depend on singleton database infrastructure, but singleton hosted services must create scopes when they need scoped services. This matches the generic host DI model and avoids leaking scoped objects into singleton services.

### Why Not Scoped `Database<TDatabase>`?

Scoped `Database<TDatabase>` would look familiar to EF users, but it is wrong for DataLinq's current architecture.

It would imply that every HTTP request, background job scope, or UI operation gets a fresh provider/cache root. That fights the DataLinq cache design and makes disposal semantics noisy. If a future provider has a reason to maintain per-scope state, that should be exposed as a provider-specific service, not by changing the default database root lifetime.

## Multiple Databases and Names

DataLinq needs two separate concepts:

- multiple model types, such as `EmployeesDb` and `SalesDb`
- multiple registrations of the same model type, such as primary and reporting databases

Different model types are straightforward:

```csharp
builder.Services.AddDataLinq<EmployeesDb>(db => db.UseMySql("..."));
builder.Services.AddDataLinq<SalesDb>(db => db.UseSQLite("..."));
```

Multiple registrations of the same model type need naming or keyed services:

```csharp
builder.Services.AddDataLinq<EmployeesDb>("primary", db => db.UseMySql("..."));
builder.Services.AddDataLinq<EmployeesDb>("reporting", db => db.UseMySql("..."));
```

On .NET 8+, keyed services are a natural host-level fit:

```csharp
app.MapGet("/reports", (
    [FromKeyedServices("reporting")] EmployeesDb db) =>
{
    return db.Employees.Take(100).ToList();
});
```

DataLinq should not require keyed services as the only mechanism because libraries and older host surfaces may prefer explicit named factories:

```csharp
public interface IDataLinqDatabaseFactory<TDatabase>
{
    Database<TDatabase> Get(string name);
    TDatabase Query(string name);
}
```

The first implementation can support unnamed registrations only. The design should leave room for names/keyed services because multi-tenant, read-replica, and reporting scenarios will need them.

## Startup Validation

Startup validation should build on `Schema Validation Hooks.md`.

Registration should be attached to the database registration:

```csharp
builder.Services.AddDataLinq<EmployeesDb>(db =>
{
    db.UseMySqlConnectionString("employees");
    db.ValidateSchemaOnStartup();
});
```

or registered centrally:

```csharp
builder.Services.AddDataLinqSchemaValidation(validation =>
{
    validation.ValidateDatabase<EmployeesDb>();
    validation.FailOnSeverity = SchemaDifferenceSeverity.Error;
});
```

Behavior:

- validation targets are explicit
- validation runs from a hosted service during startup
- validation logs structured results
- validation never logs connection strings
- validation throws only when policy says it should
- validation does not create, migrate, or repair schema
- validation can be disabled or softened by environment

Environment-specific policy should live in application code:

```csharp
builder.Services.AddDataLinq<EmployeesDb>(db =>
{
    db.UseMySqlConnectionString("employees");

    if (!builder.Environment.IsDevelopment())
        db.ValidateSchemaOnStartup();
});
```

That example is deliberately simple. Some teams want validation in development only. Some want it in staging and production. Some want a deployment job to validate before the app starts. DataLinq should provide the mechanism and avoid pretending there is one universally correct deployment policy.

## ASP.NET Core Integration

The ASP.NET Core story should be boring in the best way:

- `builder.Services.AddDataLinq<TDatabase>(...)`
- inject generated read root into Minimal API handlers for reads
- inject query services into controllers/pages
- inject `IDataLinqUnitOfWorkFactory<TDatabase>` for writes
- optional endpoint filter or middleware only when an application explicitly wants a transaction boundary around a route group

Possible endpoint filter:

```csharp
app.MapGroup("/admin")
    .WithDataLinqUnitOfWork<EmployeesDb>()
    .MapPost("/employees", ...);
```

This should not be the default. Automatic request transactions are too blunt:

- many requests are read-only
- streaming responses do not map cleanly to transaction lifetime
- external side effects and database commit ordering are application-specific
- nested operations need clear commit ownership

The default should make the correct read path easy and the write path explicit.

## Generic Host and Worker Services

Worker services should use `IServiceScopeFactory` per job or message:

```csharp
public sealed class ImportWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopes;

    public ImportWorker(IServiceScopeFactory scopes)
    {
        this.scopes = scopes;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var units = scope.ServiceProvider
                .GetRequiredService<IDataLinqUnitOfWorkFactory<EmployeesDb>>();

            using var unit = units.Begin();
            // process one bounded unit of work
            unit.Commit();
        }
    }
}
```

Singleton hosted services should not directly capture scoped read roots or scoped sessions. That is standard host hygiene, and DataLinq docs should say it plainly.

## Blazor

### Blazor Server

Blazor Server scoped services live for the circuit, not one HTTP request. That makes request-style transaction assumptions dangerous.

Recommended guidance:

- singleton `Database<TDatabase>` is fine
- inject read/query services for UI reads
- create explicit units of work inside event handlers or application services
- do not keep a transaction open across component lifetime or circuit lifetime
- avoid ambient session patterns unless the boundary is very tightly controlled

### Blazor WebAssembly

Blazor WebAssembly should not directly connect to server databases. The normal pattern is:

- server-side DataLinq in an API
- WASM client calls the API

Local SQLite in WebAssembly is a separate constrained-platform story and should not be smuggled into the general DI plan. If supported, it needs explicit docs around storage, browser persistence, size, AOT/trimming, and SQLitePCLRaw behavior.

## MAUI, Avalonia, WPF, and Other UI Apps

There is probably no need for a special XAML-first DataLinq abstraction in the first implementation.

MAUI already exposes `MauiProgram` and `builder.Services`; Avalonia can use `Microsoft.Extensions.DependencyInjection`; WPF and WinUI apps can build a generic host or service provider at startup. The right first move is to make the normal DI registration work well everywhere.

Recommended guidance:

- register DataLinq in the app startup composition root
- use singleton database roots
- inject query services or read roots into view models where appropriate
- create explicit units of work per command/save operation
- keep remote database calls off the UI thread
- provide helper samples for local SQLite app-data paths if SQLite is the common platform scenario

Possible MAUI setup:

```csharp
builder.Services.AddDataLinq<AppDb>(db =>
{
    var path = Path.Combine(FileSystem.AppDataDirectory, "app.db");
    db.UseSQLite(path);
});
```

DataLinq can add small provider helpers for common platform paths later, but the core integration should remain ordinary DI.

## Configuration API Details

Connection string options should support:

- direct connection string
- named connection string from `IConfiguration.GetConnectionString(...)`
- provider-specific options object
- options binding from configuration sections
- programmatic factory for advanced cases

Possible option model:

```csharp
public sealed class DataLinqRegistrationOptions<TDatabase>
{
    public string? Name { get; set; }
    public string? ConnectionString { get; set; }
    public string? ConnectionStringName { get; set; }
    public bool ValidateOnStartup { get; set; }
    public DataLinqSchemaValidationOptions Validation { get; } = new();
}
```

Provider-specific options should extend rather than pollute the core:

```csharp
public sealed class MySqlDataLinqOptions<TDatabase>
{
    public string? ConnectionString { get; set; }
    public string? ConnectionStringName { get; set; }
    public string? DatabaseName { get; set; }
}
```

Avoid stringly-typed provider options in the common path. The configuration binder can populate options from strings, but the programmatic API should be strongly typed.

## Logging

DataLinq already has `DataLinqLoggingConfiguration` and provider constructors that accept `ILoggerFactory`.

DI integration should wire the host `ILoggerFactory` automatically:

```csharp
var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
return new MySqlDatabase<EmployeesDb>(connectionString, loggerFactory);
```

Users should not need to call `UseLoggerFactory(...)` manually when using DI.

Provider SQL logs, transaction logs, cache logs, startup validation logs, and schema validation logs should flow through the host logging pipeline with stable categories.

## Disposal

The DI container should own disposal for singleton database instances it creates.

Rules:

- databases created by the registration delegate are container-owned unless explicitly marked external
- externally supplied instances should not be disposed by DataLinq unless the user opts in
- unit-of-work instances are caller-owned and disposed by `using`
- startup validation should not dispose registered databases

This matters because hosted apps often run for a long time, and incorrect disposal ownership creates miserable shutdown bugs.

## Implementation Slices

### Slice 1: Core DI Registration

- Add a DI extension package.
- Add `AddDataLinq<TDatabase>(...)`.
- Register `Database<TDatabase>` as singleton.
- Register provider-specific database concrete type as singleton where possible.
- Register `ReadOnlyAccess<TDatabase>` and generated `TDatabase` read root for injection.
- Wire `ILoggerFactory` automatically.
- Add unit tests for service resolution, lifetime behavior, and disposal ownership.

### Slice 2: Provider Registration Extensions

- Add `UseMySql(...)`.
- Add `UseMariaDb(...)`.
- Add `UseSQLite(...)`.
- Support direct connection strings and named connection strings.
- Support provider-specific options binding.
- Add tests that validate configuration binding and connection-string resolution without connecting to live databases.

### Slice 3: Explicit Unit of Work API

- Add `IDataLinqSession<TDatabase>`.
- Add `IDataLinqUnitOfWork<TDatabase>`.
- Add `IDataLinqUnitOfWorkFactory<TDatabase>`.
- Wrap existing `Transaction<TDatabase>` rather than duplicating transaction behavior.
- Keep `Commit()` available only on the coordinator interface.
- Add tests for commit, rollback, disposal, nested service participation, and failed commit behavior.

### Slice 4: Startup Validation Integration

- Reuse the runtime validation API from `Schema Validation Hooks.md`.
- Add database validation target registration.
- Add hosted service startup runner.
- Add environment/policy options.
- Add structured logging.
- Add tests for pass, warning-only, fail-fast, and metadata-read failure cases.

### Slice 5: Samples and Documentation

- Replace static database-holder sample patterns where practical.
- Add ASP.NET Core Minimal API sample.
- Add controller/query-service sample.
- Add worker service sample.
- Add Blazor Server caveats.
- Add MAUI local SQLite setup sample.
- Add Avalonia/WPF generic host setup notes.

## Test Plan

Unit tests:

- `AddDataLinq<TDatabase>` registers all expected services.
- `Database<TDatabase>` resolves as singleton.
- generated `TDatabase` read root resolves from a scope.
- host `ILoggerFactory` is passed to provider database creation.
- named connection strings resolve from `IConfiguration`.
- missing connection string fails with a clear error.
- multiple model types can be registered independently.
- disposal happens once for container-owned singleton databases.
- external database instances are not disposed unless configured.

Unit-of-work tests:

- factory creates a transaction-backed unit of work.
- participant interface cannot commit.
- coordinator commits once.
- rollback prevents commit.
- disposal rolls back uncommitted work according to existing transaction semantics.
- nested services can share an explicit unit when passed the participant interface.

Startup validation tests:

- registered validation target runs at startup.
- validation failure prevents host startup when policy requires it.
- warning-only differences log without throwing when configured.
- connection strings are not logged.
- multiple registered databases produce separate validation summaries.

Integration tests:

- ASP.NET Core test host can resolve read root in Minimal API handler.
- write handler can use unit-of-work factory.
- worker-style scope can resolve and dispose operation services.
- SQLite registration can use a configured local path.

## Risks and Sharp Edges

- **Ambiguous scoped root:** injecting generated `TDatabase` is ergonomic, but it must be clearly read-only or users will assume it behaves like EF `DbContext`.
- **Ambient sessions:** `AsyncLocal` can be useful, but it makes transaction ownership less obvious. It should not be first-slice behavior.
- **Blazor Server scope semantics:** scoped does not mean request-scoped in Blazor Server. Documentation must be blunt about this.
- **Multiple same-model registrations:** unnamed-only registration is simple but incomplete. Design the types so keyed/named support can be added without breaking the API.
- **Startup validation availability:** validation depends on provider metadata readers and live database access. Failures need precise logs or users will disable the feature.
- **Package dependency creep:** pulling ASP.NET Core into the base runtime would be a mistake. Keep web conveniences optional.

## Open Questions

- Should the first package be named `DataLinq.Extensions.DependencyInjection`, `DataLinq.DependencyInjection`, or provider-specific only?
- Should generated `TDatabase` read root be scoped or transient when resolved through DI?
- Should `ReadOnlyAccess<TDatabase>` be directly injectable, or should a new `IDataLinqReadAccess<TDatabase>` abstraction exist?
- Should startup validation live in the same DI package or a hosting-specific package?
- Should named/keyed registrations be first-slice or second-slice?
- Should unit-of-work participant sharing be explicit only, or should an optional ambient scope be added later?
- Should endpoint filters/middleware be included in the ASP.NET package or documented as application code?
- Should provider registration support connection-string reload through `IOptionsMonitor`, or should database instances stay immutable until app restart?

## References

- ASP.NET Core dependency injection: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection>
- .NET dependency injection overview: <https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection>
- .NET generic host: <https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host>
- .NET MAUI dependency injection: <https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/dependency-injection>
- Avalonia dependency injection: <https://docs.avaloniaui.net/docs/app-development/dependency-injection>
