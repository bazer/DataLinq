> [!WARNING]
> This document is roadmap/specification material. It describes planned behavior, not shipped DataLinq behavior.

# Specification: Dependency Injection and Hosting Integration

**Status:** Accepted.
**Release horizon:** DataLinq 0.10 for unnamed and keyed registration, explicit unit of work, and startup-host integration; broader host variants remain later work.
**Last reviewed:** 2026-10-04 (H10-4 registration/configuration and keyed database instances, following H10-1 package placement, H10-2 singleton reads, and H10-3 composable units of work; ownership mechanics and bounded API details remain open).
**Dependency:** The shipped 0.9 backend/source boundary and the 0.10 async contracts must be stable before host integration freezes public service abstractions.
**Goal:** Make DataLinq straightforward to configure, validate, and consume from ASP.NET Core, generic host, background workers, Blazor, MAUI, Avalonia, and other .NET application surfaces without hiding database I/O or transaction boundaries.

**0.10 failure contract:** [AAPI-21 through AAPI-26](../roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md#aapi-21-validate-first-then-honor-pre-cancellation-even-on-cache-hits) own cancellation validation, initialization/read/mutation recovery, confirmed/unknown completion, cleanup precedence, and structured failure information. Host unit-of-work helpers must consume these policies, including an independent configurable 30-second starting recovery rollback budget subject to provider verification rather than the canceled request token. Do not promise a hard total-disposal deadline or infer rollback from cancellation.

**0.10 mutation/callback contract:** [AAPI-27 through AAPI-33](../roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md#aapi-27-capture-mutation-inputs-before-the-first-suspension) settle mutation input capture/exclusive lifetime, synchronous local edits, finite multi-model capture, task-returning callback families, borrowed completion restrictions, explicit token delivery, and results after successful commit/finalization/cleanup. Apply those policies to host helpers without hidden transaction retention or callback replay.

**0.10 composable service decision:** [H10-3](#h10-3-explicit-composable-units-of-work) adds participant rollback requests, native typed business results, an outcome-aware owning helper, and generated owner/participant service entry points. Existing `CommitAsync` callbacks retain their success-means-commit contract. This is accepted W4 design, not implemented behavior or a W4 closeout.

**0.10 concurrency contract:** [AAPI-34 through AAPI-41](../roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md#aapi-34-reject-overlapping-transaction-execution) settle transaction overlap/resource ownership, private internal/mutable/helper rights, busy caller-disposal rejection, unfinished-callback admission/recovery, and cache coordination/isolation. Host helpers must not queue transaction work implicitly, commit unfinished callbacks, propagate ambient cancellation, or promise a hard drain deadline. Exact public surfaces remain under OAPI-7, provider feasibility under OAPI-9, and host lifetimes still require H10 design.

**0.10 lower-level ownership/failure access:** [AAPI-56 through AAPI-63](../roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md#aapi-56-mirror-lower-level-execution-with-verified-async-capability) settle verified async execution and reader/resource ownership, raw/managed adapter gating without raw mutation tracking, consuming synchronous attachment, owning-root async disposal, typed immutable failure snapshots, and provider-scoped recovery configuration. Host registrations must capture validated immutable `DataLinqExecutionOptions.RecoveryRollbackTimeout` settings (30-second default, positive finite supported durations), preserve direct constructor compatibility, and let transactions/helpers inherit them. The budget starts at automatic rollback, not unfinished-work draining; no global/live settings, restarting retries, or total-disposal deadline are introduced. Expose failure context after helper disposal with recovery actions valid at reporting time. End dependent transaction/reader lifetimes before provider disposal; container-created versus externally supplied ownership must be explicit, without double-owning database/provider resources. Exact registration/constructor compatibility and provider feasibility still require H10/A10 evidence.

**0.10 metadata/mutation inventory:** [AAPI-64 through AAPI-72](../roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md#aapi-64-async-existence-checks-preserve-their-distinct-probe-semantics) settle async runtime validation, effective provider identity/ownership, and main mutation/callback families. Startup validation captures configuration and propagates host cancellation, separating per-command timeout and operational failure from completed comparison policy. It does not recreate providers, create/repair schemas, or claim an atomic DDL snapshot. Provider callback helpers use untyped Transaction; typed callbacks stay on Database<TDatabase>. Generated source-less mutation helpers own independent transactions, so participating services must pass their intended unit's transaction explicitly. Apply finite insertion, narrow nullable Save behavior, and unchanged-update read rules without batch or persistence expansion.

**Related work:**

- `docs/dev-plans/architecture/Applications patterns.md`
- `docs/dev-plans/providers-and-features/Schema Validation Hooks.md`
- `docs/dev-plans/providers-and-features/UUID Storage Format Support.md`

## Problem Statement

**0.10 validation integration:** [AAPI-106 through AAPI-109](../roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md#aapi-106-runtime-validation-types-and-immutable-result-construction), as amended by [H10-1](#h10-1-integration-in-existing-packages), place validation types and hosting integration in core with separate namespaces and responsibilities, retain complete immutable result collections, separate display filtering from failure policy, scope Include to comparison and preserve empty-versus-unreadable schema distinctions. Use the same bounded per-command timeout contract; startup cancellation is separate and operational failures cannot become successful comparisons. Actual host/provider verification remains pending.

**0.10 inventory decisions:** [AAPI-100 through AAPI-102](../roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md#aapi-100-async-raw-model-readers-follow-the-existing-class-hierarchy) settle raw model readers, lower-level provider-transaction async completion and exact diagnostic values/options placement. Hosted units of work complete through the managed Transaction wrapper; provider-level completion does not perform its mutable/cache finalization. DataLinqExecutionOptions lives in DataLinq; the diagnostic classification enums use Unknown = 0. [AAPI-105](../roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md#aapi-105-provider-interface-disposal-implements-the-inherited-slot) settles IDatabaseProvider's inherited IAsyncDisposable slot/default and public virtual base/concrete dispatch. Unsupported async disposal never chooses synchronous cleanup or marks resources disposed; host/old-binary verification remains pending.

**0.10 diagnostic/configuration detail:** [AAPI-91 through AAPI-99](../roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md#aapi-91-failure-context-is-an-immutable-diagnostic-snapshot) settle immutable typed context/direct access, independent outcome/recovery classifications and secondary exceptions, preserved options constructors/provider interface defaults, 1 ms through 4,294,967,294 ms recovery duration with pre-setup immutable capture, and DLG004. Host registration uses the same validated settings without live reconfiguration; externally supplied providers retain their settings. Recovery actions describe reporting-time state, not permanent retry permissions. MariaDB's existing version probe, like SQLite setup, remains synchronous during provider construction and is not covered retroactively by startup-validation cancellation.

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

> The provider-backed `Database<TDatabase>`, its existing `ReadOnlyAccess<TDatabase>`, and that access object's generated `TDatabase` read root are application-level singletons. Transactions and write units of work are explicitly created per operation.

That position follows from the current runtime:

- `Database<T>` owns a provider and cache.
- `DatabaseProvider<T>` owns finalized metadata, state, logging configuration, and read-only access.
- `ReadOnlyAccess<TDatabase>` builds the generated database root over a read-only access object.
- `Transaction<TDatabase>` is explicit, disposable, and commit/rollback based.
- Convenience mutation methods on `Database<T>` already create short-lived transactions.

The DI design should preserve those semantics instead of flattening everything into a scoped context.

## Design Principles

- **Use the host's primitives:** integrate with `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Hosting`, and `Microsoft.Extensions.Logging`.
- **Use existing packages:** common DI/hosting integration belongs in `DataLinq`, and provider registration belongs in the corresponding provider package. Keep integration organized under extension namespaces. Future helpers that require ASP.NET Core dependencies must use a separate package.
- **Singleton database root:** register provider-backed `Database<TDatabase>` as singleton unless a provider has a documented reason not to.
- **Shared read convenience:** inject the existing provider-owned read access and generated read root as singletons, without constructing new facades for each request or scope.
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

### H10-1: Integration In Existing Packages

**Accepted:** 2026-09-30. Keep W4 DI/unit-of-work integration and W5 hosted startup validation in the existing packages. This supersedes the earlier DI/provider extension-package proposal and AAPI-106's separate-hosting-package requirement. It records planned placement, not implemented APIs or dependencies.

| Existing package | Integration responsibility |
| --- | --- |
| `DataLinq` | Common registration, unit-of-work contracts and integration, configuration support, and W5 hosted startup validation. |
| `DataLinq.MySql` | MySQL and MariaDB registration and provider-specific configuration. |
| `DataLinq.SQLite` | SQLite registration and provider-specific configuration. |

Organize these APIs under extension namespaces, such as `DataLinq.Extensions.DependencyInjection` and `DataLinq.Extensions.Hosting`, with corresponding provider namespaces. Exact public namespace/type spelling remains part of the API review; these names do not identify new assemblies or NuGet packages. Runtime validation retains its accepted `DataLinq.Validation` and `DataLinq.Exceptions` placement.

Do not introduce DI-only, provider-DI, or hosting-only DataLinq packages for this scope. The existing provider-to-core dependency direction remains. Future helpers that actually require ASP.NET Core types/dependencies must live in a separate package; W4/W5 require neither an ASP.NET Core dependency nor an ASP.NET-specific package. Testing-package layout remains a separate T10 decision.

**Rationale:** basic DI abstractions already arrive through DataLinq's logging dependency. Keeping registration with the runtime/provider packages avoids additional package selection, version alignment, and release maintenance for common application setup. Consumers using direct construction, including Memory consumers, will also inherit any added dependency graph; namespaces do not make dependencies optional.

**Dependency boundary:** use the Microsoft.Extensions packages needed by the accepted APIs. The planning audit used the repository's 10.0 package line and net8.0/net9.0/net10.0 dependency groups. All names in this table have the `Microsoft.Extensions.` prefix; the last column lists additions beyond preceding rows.

| Feature | Package used directly | Additional dependencies |
| --- | --- | --- |
| Service registration, lifetimes, factories, and service resolution | `DependencyInjection.Abstractions` | Already transitive through `Logging.Abstractions`; make the reference explicit when used directly. |
| Host logging | `Logging.Abstractions` | Already referenced by core. |
| `IConfiguration` and named connection strings | `Configuration.Abstractions` | `Primitives`. |
| Standard options registration/validation and `IOptions<T>` | `Options` | Nothing further beyond preceding rows. |
| Bind configuration sections into options objects | `Configuration.Binder` | `Configuration`. |
| Configuration binding through the standard options pipeline | `Options.ConfigurationExtensions` | Nothing further beyond preceding rows. |
| W5 startup execution through `IHostedService` | `Hosting.Abstractions` | `Diagnostics.Abstractions` and `FileProviders.Abstractions`. |

Programmatic registration needs no new package IDs beyond the current graph. Plain DataLinq options objects and ordinary argument validation do not themselves require Microsoft.Extensions.Options. Exact options/binding APIs and dependency versions remain implementation decisions; do not add every package merely because it appears in this inventory. Standard options/configuration binding is supported by the [options binding dependency metadata](https://www.nuget.org/packages/Microsoft.Extensions.Options.ConfigurationExtensions/10.0.11#dependencies-body-tab) and [binder metadata](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Binder/10.0.11#dependencies-body-tab); the [hosting metadata](https://www.nuget.org/packages/Microsoft.Extensions.Hosting.Abstractions/10.0.11#dependencies-body-tab) records its abstraction dependencies.

Registration/startup adapters consume the application's services without requiring the full `Microsoft.Extensions.Hosting` implementation, Microsoft's concrete DI container, configuration source providers, or logging providers as DataLinq dependencies. Preserve explicit opt-in startup execution and the W4/W5 sequencing. Core must still avoid dependencies on Tools, CLI configuration, source parsing, and ASP.NET Core. Verify the actual restored/packed dependency graph and supported consumer/platform evidence when implementation lands.

## Public API Shape

### H10-4: Registration, Configuration, And Keyed Instances

**Accepted:** 2026-10-04. Provider selection stays explicit in application code. Registration records a construction recipe; the first resolution captures and validates settings and constructs the singleton database. Both unnamed and keyed registrations belong in W4. This supersedes the earlier unnamed-only scope, configuration-driven provider selection, and container-build-time construction wording. The examples below express the accepted API direction; exact overloads and option types still require API review and implementation evidence.

Each registration is identified by the generated database model type and an optional service key. The provider and connection settings belong to that registration; they are not its lookup identity. Support different providers for one model and multiple instances of the same provider with different connection strings. Apply the same key to the database, existing read services, and unit-of-work factory, as detailed under [Multiple Databases and Names](#multiple-databases-and-names).

#### Configuration And Construction Rules

- Select the provider through `UseMySql`, `UseMariaDb`, or `UseSQLite` in code. Configuration supplies connection strings and typed provider/execution/validation settings; it does not select a provider through a `"Provider"` string or load provider plugins.
- Support direct connection strings and names resolved from the application's `IConfiguration`, alongside typed provider options/binding. A literal connection string does not require an `IConfiguration` service. Report missing required configuration clearly without exposing connection secrets.
- Registration records the recipe without constructing a provider or accessing a database. Reject structural registration conflicts before provider construction. At first resolution of any service in a registration, resolve its required configuration, validate and capture settings, and construct one shared database/provider/read-service graph. Building the container alone is not a promise of eager construction or configuration validation.
- Opt-in W5 startup validation resolves its selected registrations during startup, bringing configuration/construction failures forward. Ordinary first resolution can still perform existing synchronous constructor work: [MariaDB version detection](../../../src/DataLinq.MySql/MariaDB/MariaDBProvider.cs) and [SQLite setup/keep-alive acquisition](../../../src/DataLinq.SQLite/SQLiteProvider.cs) are not converted to async or made I/O-free by DI.
- Capture immutable effective settings for each constructed registration, including the accepted execution options. Do not mutate or recreate live databases, caches, or factories when configuration reloads. `IOptionsMonitor` does not imply live DataLinq reconfiguration; adopting new settings requires a new explicitly owned database/container lifetime. Externally supplied instances retain their existing settings.
- Use the host's `ILoggerFactory` when available and retain the existing no-op logging behavior when it is absent. Neither a full host nor logging/configuration services are mandatory for programmatic registration.

### Basic ASP.NET Core / Generic Host

Recommended shape:

```csharp
builder.Services.AddDataLinq<EmployeesDb>(db =>
{
    db.UseMySqlConnectionString("employees");
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

The fluent form resolves the named connection string from `IConfiguration` when this registration is first resolved, under H10-4. Direct configuration uses `UseMySql(connectionString)` instead. Neither form reads secrets at source-generation time or introduces a hidden transaction.

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

The `UseMySql()` call selects the provider. Binding configures that selected provider and the associated validation policy; it cannot change provider type or registration key. Capture effective options at construction under H10-4, without a live reload subscription. Exact binding overloads, option types, and conflicting-setting diagnostics remain API-review details.

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

### H10-3: Explicit Composable Units Of Work

**Accepted:** 2026-10-01. The ownership, rollback-request, service-composition, native-result, and future union-compatibility decisions below resolve this design question. They supersede this document's earlier participant `Rollback()`, mandatory `Begin()`, scoped current-session proposal, and void-returning mutation sketches. Public spelling and generator wiring still need the bounded API work listed below; this is not a claim that W4 has shipped or that every H10 question is closed.

#### Ownership And Transaction Creation

The participant session exposes transaction-bound reads, the supported mutation surface, and a way to request rollback. It exposes no physical commit, rollback, or disposal operations. The owner controls completion and cleanup. Query and mutation signatures must retain the real runtime's return values and sync/async semantics; for example, immutable mutation results must not be discarded by an interface declaring every mutation `void`.

Use the existing managed `Transaction<TDatabase>` lifecycle, including its mutable/cache finalization, failure classification, and operation gate. Prefer direct interface implementation where it fits; a small borrowing adapter is acceptable when needed to express ownership. Do not create a second transaction engine or require a wrapper allocation merely to rename a transaction. A narrow interface communicates participation rights; managed owning helpers must also enforce borrowed-completion restrictions at runtime. It is not a universal protection against arbitrary casts or unsupported direct provider access.

No separate call to `Begin()` is necessary. `Database<TDatabase>.Transaction()` already creates an explicit logical transaction with lazy provider I/O. A factory can expose this operation for DI/testability, and an owning callback helper creates it internally. Choosing an owning service entry point is an explicit operation boundary; registering or resolving a service must not start a transaction.

Pass the participant session explicitly to nested services. W4 introduces no ambient/`AsyncLocal` transaction, automatically resolved current session, nested independent transaction masquerading as participation, or savepoint behavior. Work on a shared transaction remains sequential, with all child work awaited. Reads through the singleton read root remain independent of this transaction.

#### Participant Rollback Requests

Use `session.RequestRollback(failure)` as the illustrative API. It immediately records an irreversible veto on committing the shared managed transaction and returns a typed rollback value. It performs no provider I/O and does not end the owner's lifetime. Expose the same veto semantics through the managed transaction/session surface so manual owners and callback owners cannot disagree about whether commit is allowed.

- A later commit attempt is rejected before provider commit dispatch. For a manual owner, cleanup remains that owner's responsibility; an owning helper performs recovery and cleanup before reporting failure.
- Once rollback is requested, reject further database execution through that session. Local result construction, failure mapping, and unwinding remain possible, as do owner recovery and cleanup.
- Repeated requests cannot clear the veto. Preserve the original request for diagnostics; a parent can translate the returned business failure without replacing the original reason the transaction became noncommittable.
- A requested business rollback is distinct from poisoning caused by an execution failure. Exceptions, cancellation, provider failure, and unknown completion do not become ordinary business results.
- An ordinary returned domain value is not inspected for application-specific success/failure flags. Only the explicit result protocol and transaction state control completion.

This is a rollback of the whole shared unit. A child that wants to undo only its own writes and let the parent commit would need a separate savepoint design, outside W4.

#### Native Result And Conversion Contract

Provide small DataLinq-owned result types, illustrated as `TransactionResult<T>` for a string failure and `TransactionResult<T, TFailure>` for a typed failure. Success and rollback are distinct cases, represented as readonly structs `Success<T>` and `Rollback<TFailure>`. These names are provisional. Keep the implementation small, with typed storage and an explicit discriminator rather than an object-backed general-purpose union library.

The public construction rules are:

| Expression | Meaning |
| --- | --- |
| `return value;` where the value is a `T` | Implicitly construct the success case. |
| `return session.RequestRollback(failure);` | Record the session veto and implicitly construct the rollback case from its typed marker. |
| A bare `TFailure` | No implicit failure conversion; use the rollback-request form. |
| `default(TransactionResult<...>)` | Uninitialized and invalid for completing an operation; never permission to commit. |

The rollback marker lets the session infer only `TFailure`; the method's return type supplies `T`. Both branches may have the same payload type without confusing their meaning:

```csharp
// Illustrative method body returning TransactionResult<OrderResult, OrderResult>.
if (!available)
    return session.RequestRollback(OrderResult.Rejected("Unavailable"));

return OrderResult.Accepted();
```

Likewise, `TransactionResult<string>` can accept a plain successful string or a string wrapped by `RequestRollback`. Conversion from `T` constructs `Success<T>` internally; callers need not construct that wrapper. Separate case types also avoid treating a broad success payload such as `object` as the union's failure case solely because of the payload's runtime type.

Expose explicit inspection such as `IsSuccess`, `RequiresRollback`, `TryUnwrap`, and `Match`. Do not add an implicit conversion from the result back to `T` in the initial surface. Keep wrong-case access guarded, and keep an uninitialized result distinguishable from either valid case. The proposed type does not replace ThrowAway across the repository; that broader dependency migration is later work.

#### Generated Owner And Participant Entry Points

An opted-in service supplies one business implementation. The existing bundled source generator emits a standalone owning entry point and a participating overload taking the explicit session. A marker interface identifies the database and an attribute selects operation methods; `IDataLinqService<TDatabase>` and `[DataLinqOperation]` are illustrative spellings. Constructor/factory wiring and diagnostics are part of signature review, not implicit service activation or runtime reflection.

```csharp
public partial class OrderService : IDataLinqService<ShopDb>
{
    [DataLinqOperation]
    private async Task<TransactionResult<Order, OrderError>> PlaceCoreAsync(
        IDataLinqSession<ShopDb> session,
        CreateOrder command,
        CancellationToken cancellationToken)
    {
        var customer = await session.Query().Customers.SingleAsync(
            x => x.Id == command.CustomerId, cancellationToken);

        if (!customer.CanPlaceOrders)
            return session.RequestRollback(
                new OrderError("Customer cannot place orders."));

        return await session.InsertAsync(
            new MutableOrder { CustomerId = customer.Id }, cancellationToken);
    }
}
```

The generated signatures have this shape:

```csharp
// Own a new transaction and finish completion/cleanup before returning.
Task<TransactionResult<Order, OrderError>> PlaceAsync(
    CreateOrder command, CancellationToken cancellationToken = default);

// Borrow the supplied session; do not physically complete or dispose it.
Task<TransactionResult<Order, OrderError>> PlaceAsync(
    IDataLinqSession<ShopDb> session,
    CreateOrder command, CancellationToken cancellationToken = default);
```

The standalone method returns the union too: different success/failure types cannot both be returned as a plain `T`. A participating success means that service's work succeeded so far, not that the outer transaction committed. Services can therefore be used independently or stacked under one owner without duplicating their business code. Calling an owning overload from inside another operation still starts an independent unit; callers must use the session-taking overload to participate.

A parent can translate a child's failure while preserving the veto:

```csharp
// Inside an operation returning TransactionResult<Checkout, CheckoutError>.
var result = await orders.PlaceAsync(session, command.Order, cancellationToken);

if (!result.TryUnwrap(out var order, out var failure))
    return session.RequestRollback(CheckoutError.FromOrderError(failure));

return new Checkout(order.Id);
```

Keep all completion/recovery behavior in shared runtime helpers; generated methods forward to those helpers rather than copying rollback/finalization logic. The same protocol must be usable without generation. Validate method shapes, generated-name collisions, and token forwarding with generator diagnostics/tests. Runtime enforcement remains necessary: generation cannot prove arbitrary application control flow handles every child result correctly.

#### Owning Callback Completion

Add an explicit outcome-aware helper, illustrated as `ExecuteAsync`, that receives a session and cancellation token and awaits a `TransactionResult` callback. Generated owning methods use it; manually composed services can use it directly. It invokes the callback once without replay and retains the existing unfinished-work, active-reader, cancellation, recovery, and cleanup policies.

| Final callback result | Shared rollback veto | Owner behavior |
| --- | --- | --- |
| Success | Absent | Commit, finalize, clean up, then return success. |
| Rollback | Present or absent | Ensure the veto is recorded, roll back, clean up, then return the typed business failure. |
| Success | Present | Roll back and clean up, then throw an inconsistent-result exception; never return success for discarded writes. |
| Uninitialized result | Either | Do not commit; recover/clean up and report invalid result usage. |
| Exception or cancellation | Either | Preserve the existing failure/recovery contract and throw; do not turn it into a business rejection. |

Each managed participating boundary must honor a returned rollback case in the shared transaction state, including when a result was forwarded or mapped rather than created locally by `RequestRollback`. The immediate veto from `RequestRollback` must survive an ignored result. Returning a result is never evidence that physical completion has already occurred.

Return the normal rollback result only after rollback is confirmed, or no provider transaction/work was started, and required cleanup succeeds. A rollback, finalization, or cleanup failure still throws with honest outcome/secondary-failure information. A rolled-back inserted model or generated key is not evidence of persisted data. Transaction-bound deferred work must remain inside its owning lifetime.

Existing `CommitAsync` callback contracts remain intact: normal callback completion requests commit, and a veto prevents that commit and causes an exception. Do not reinterpret arbitrary existing callback return types or silently return a normal result after rollback. This new helper is the explicit opt-in for returning expected business failures after successful rollback. It reuses managed lifecycle machinery rather than directly completing the lower-level provider transaction.

#### C# Union Compatibility

Prepare the case model and member names for C# 15 custom unions while keeping W4 usable on the repository's existing .NET 8/9/10 and C# 14 baseline. Do not make the new service/result API depend on .NET 11 or emit union syntax unconditionally into consumer code.

The [C# union specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-15.0/unions) permits custom types and distinct case wrappers. Its compiler-facing `Value` represents the active case, and `HasValue` describes non-null contents rather than business success. Reserve those meanings or isolate them through a union-member provider; do not accidentally commit a conflicting public contract. The runtime runner still rejects the uninitialized/default result even if its union view represents empty contents as null.

Prefer typed storage and the [non-boxing custom union access pattern](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/unions#build-a-custom-union-that-avoids-boxing) over the object storage generated by the ordinary `union` shorthand. This avoids requiring a separate allocation for struct cases on the typed path; an object-valued view can still box. Native matching supplements the ordinary inspection API and does not replace transaction ownership or the rollback veto.

The [2026-09-08 .NET 11 RC1 announcement](https://devblogs.microsoft.com/dotnet/dotnet-11-rc-1/) records stabilization of C# 15 unions. Actual compiler integration, target-specific metadata/polyfill choices, and source/binary compatibility must be verified separately. Adding union recognition can affect existing pattern matching, so it is a deliberate compatibility change, not assumed to be a harmless attribute addition. Broad ThrowAway replacement and native union activation are later work, not requirements to finish W4.

#### Bounded API And Verification Follow-Up

This decision closes the ownership/composition question; it does not freeze a complete signature manifest. Before implementation API freeze:

- finalize public names/namespaces, the session/owner/factory mutation and completion inventories, and exception/diagnostic access using existing runtime conventions;
- specify generator method naming, constructor/factory acquisition (including H10-4's selected registration key), supported sync/async shapes, and diagnostics without changing the one-body/two-entry-point contract;
- complete the result surface for nullable payloads, operations with no value, explicit construction/inspection, and unusual generic conversions, preserving distinct cases and invalid-default rejection;
- prove generated and handwritten composition with consumer-shaped tests, including same-type/string payloads, failure mapping, ignored vetoes, post-veto execution rejection, borrowed completion, cancellation, cleanup errors, and no callback replay;
- retain separate follow-up for native union compilation/compatibility and for a possible repository-wide ThrowAway replacement.

Exact mechanics may be settled during API review; they must not silently change these accepted semantics. H10-4 settles registration/configuration behavior; exact overloads and container/external-instance disposal mechanics remain for review. W6B fakes consume the final W4 signatures and these behaviors rather than inventing a competing lifecycle.

## Service Lifetimes

### H10-2: Shared Singleton Read Services

**Accepted:** 2026-10-01. Register `Database<TDatabase>`, the provider's existing `ReadOnlyAccess<TDatabase>`, and that access object's existing generated `TDatabase` read root as singletons. Share these same instances across resolutions, requests, and DI scopes within one service provider/database registration. This supersedes the earlier scoped/transient read-facade proposal. The provider-specific database and its base `Database<TDatabase>` service identify the same database instance.

**Rationale:** reusable read infrastructure is part of DataLinq's design and a benefit to preserve in DI. The current provider already constructs and retains read access, which constructs and retains the generated root; `Database<TDatabase>.Query()` returns that root. Registration should expose those objects without recreating read facades or table/query roots per request. Singleton here means per application container/registration, not a process-global static shared by independent service providers.

Read roots retain no current request, user, request token, or active unit of work. Application-added partial members must respect their shared lifetime. Individual query executions, active enumerators, mutable query builders, connections, and transactions retain their existing operation/resource lifetimes; sharing the root does not authorize concurrent use of one active enumerator or transaction. An injected read root provides no operation-wide snapshot or transaction participation. Reads that must join a unit of work use that unit's transaction-bound root.

Expose `ReadOnlyAccess<TDatabase>` directly as well as the generated `TDatabase`; this decision does not introduce a new read-access interface. Ending a child DI scope must not dispose the shared database/provider or read services. Preserve one disposal owner for each owned database/provider pair; exact registration mechanics and external-instance ownership remain part of the ownership review. Transactions and unit-of-work instances remain explicitly created per operation, never shared singletons.

**Owner/gate:** H10/W4. Verify reference identity across repeated resolutions and independent scopes, identity with the provider's read access and `Database.Query()`, no repeated read-root construction per scope, direct consumption by singleton workers, concurrent independent reads and cancellation/failure isolation, and correct scope/host disposal. This is an accepted integration design, not evidence that DI registration is implemented.

Services exposed by each registration (unnamed or keyed under H10-4):

| Service | Lifetime | Reason |
| --- | --- | --- |
| `Database<TDatabase>` | Singleton | Owns provider/cache/state and is expensive enough to treat as app-level infrastructure. |
| Provider-specific database, e.g. `MySqlDatabase<TDatabase>` | Singleton | Same object as the base database registration. |
| `ReadOnlyAccess<TDatabase>` | Singleton | Exposes the provider's existing shared read access. |
| Generated `TDatabase` read root | Singleton | Exposes that read access object's existing root, also returned by `Database.Query()`. |
| `IDataLinqUnitOfWorkFactory<TDatabase>` | Singleton | Creates explicit transactions from the singleton database root. |
| `IDataLinqUnitOfWork<TDatabase>` | Explicit/disposable per operation | Owner-controlled completion under H10-3; participants borrow the explicit session. Exact public names/signatures remain subject to API review. |
| Schema validation hosted service | Singleton hosted service | Runs once during host startup over registered targets. |

Scoped application services can depend on these singleton read services. Singleton hosted services can inject the shared read root or unit-of-work factory directly; they need scopes only when consuming other scoped application services.

### Why Not Scoped `Database<TDatabase>`?

Scoped `Database<TDatabase>` would look familiar to EF users, but it is wrong for DataLinq's current architecture.

It would imply that every HTTP request, background job scope, or UI operation gets a fresh provider/cache root. That fights the DataLinq cache design and makes disposal semantics noisy. If a future provider has a reason to maintain per-scope state, that should be exposed as a provider-specific service, not by changing the default database root lifetime.

## Multiple Databases and Names

Under H10-4, W4 supports:

- multiple model types, such as `EmployeesDb` and `SalesDb`
- multiple providers for the same model type, such as MySQL and SQLite in one test container
- multiple instances of the same model/provider with different connection strings, such as primary and reporting servers

Different model types are straightforward:

```csharp
builder.Services.AddDataLinq<EmployeesDb>(db => db.UseMySql("..."));
builder.Services.AddDataLinq<SalesDb>(db => db.UseSQLite("..."));
```

Use ordinary .NET service keys, including strings/constants and enum values. A key describes the application role or test target, independently of provider type and the configuration connection-string name. Do not use connection secrets as service keys. For example:

```csharp
public enum ShopConnection
{
    Primary,
    Reporting,
    SQLiteTest
}

services.AddDataLinq<ShopDb>(ShopConnection.Primary)
    .AsDefault()
    .UseMySqlConnectionString("ShopPrimary");

services.AddDataLinq<ShopDb>(ShopConnection.Reporting)
    .UseMySqlConnectionString("ShopReporting");

services.AddDataLinq<ShopDb>(ShopConnection.SQLiteTest)
    .UseSQLite(sqliteConnectionString);
```

Each registration has its own singleton database/provider, read access, generated read root, cache, and captured settings. Repeated resolutions of one registration share those instances; different registrations do not implicitly share DataLinq caches, even when they address the same physical database. This does not change provider-driver connection-pooling behavior.

Use standard [keyed service injection](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/overview#keyed-services). [`FromKeyedServicesAttribute`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.fromkeyedservicesattribute) belongs to `Microsoft.Extensions.DependencyInjection.Abstractions`; this requires no ASP.NET dependency or separate DataLinq integration package.

```csharp
public sealed class ShopService(
    [FromKeyedServices(ShopConnection.Primary)]
    ShopDb primary,

    [FromKeyedServices(ShopConnection.Reporting)]
    ShopDb reporting,

    [FromKeyedServices(ShopConnection.Primary)]
    IDataLinqUnitOfWorkFactory<ShopDb> transactions)
{
    // Independent reads use the selected read root.
    // The factory creates transactions for the primary registration.
}
```

The same key resolves a coherent set of services:

| Service | Selected instance |
| --- | --- |
| Generated `TDatabase` | The registration's existing read root. |
| `ReadOnlyAccess<TDatabase>` | Its existing provider-owned read access. |
| `Database<TDatabase>` | Its owning database instance. |
| Concrete provider database type | The same database instance, where that type is exposed. |
| `IDataLinqUnitOfWorkFactory<TDatabase>` | A factory bound to that database. |

**Default and conflict rules:**

- `AddDataLinq<TDatabase>()` creates the unnamed/default registration.
- A keyed registration becomes available through ordinary unkeyed injection only by explicit `.AsDefault()` selection. It aliases that registration's services; it does not construct or own a second database.
- At most one default exists per model, whether registered unnamed or selected through `.AsDefault()`. Do not select the first, last, or only keyed registration implicitly.
- Registering the same model/key twice is an error, even if provider types differ. The same key can be used independently for different model types. Follow ordinary keyed-service equality semantics.
- A missing requested key fails clearly without falling back to the default. An unkeyed request without an explicit default also fails.
- Preserve H10-2's single disposal owner across concrete/base and keyed/default aliases. Exact container bookkeeping remains part of the disposal review.

**Dynamic selection and tests:** test fixtures can choose a registered target using standard keyed resolution:

```csharp
var database = serviceProvider.GetRequiredKeyedService<ShopDb>(connection);
var transactions = serviceProvider.GetRequiredKeyedService<
    IDataLinqUnitOfWorkFactory<ShopDb>>(connection);
```

Alternatively, build separate test containers with the chosen provider as the default so application constructors remain unchanged. Start with constructor injection and standard keyed lookup; the earlier separate named `IDataLinqDatabaseFactory<TDatabase>` resolver is not required for W4. Add a DataLinq-specific resolver only if concrete usage justifies it.

**Generated service composition:** the selected registration key must flow into generated owner-factory wiring without requiring connection selection in the authored business method. A standalone call uses its selected factory. A participating call uses the explicitly supplied session, including its database/provider identity; it must not open another transaction from its own factory or use an independently injected read root for transactional reads. The same operation can therefore participate against different providers in tests. Transactions from separate registrations do not become one atomic transaction; distributed transactions are outside this decision.

**Remaining API work:** finalize overload/key argument spelling, `.AsDefault()` registration mechanics and diagnostics, typed options/binding conflict rules, and generated service registration/factory wiring. Basic keyed support is accepted W4 scope, not deferred pending a new abstraction. No implementation or consumer verification is claimed by these examples.

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
- targets identify the full model/key registration; a default alias refers to the same target, not another database to construct or validate
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

Worker services can inject the singleton read root or unit-of-work factory directly. A fresh unit of work is still created for each bounded write operation:

```csharp
public sealed class ImportWorker : BackgroundService
{
    private readonly IDataLinqUnitOfWorkFactory<EmployeesDb> units;

    public ImportWorker(IDataLinqUnitOfWorkFactory<EmployeesDb> units)
    {
        this.units = units;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await units.ExecuteAsync(
                (session, token) => ImportOneAsync(session, token),
                cancellationToken: stoppingToken);
            HandleImportResult(result);
        }
    }
}
```

This is a shape sketch: `ImportOneAsync` represents an application operation returning a native transaction result, and `HandleImportResult` consumes its completed outcome. The helper owns completion/cleanup under H10-3; an opted-in generated service can provide the same boundary.

When a job also consumes scoped application services, create an application-service scope per job or message. DataLinq's shared read services do not themselves require that scope. Keep unit-of-work instances within the operation that owns them; do not retain them on the singleton worker.

## Blazor

### Blazor Server

Blazor Server scoped services live for the circuit, not one HTTP request. That makes request-style transaction assumptions dangerous.

Recommended guidance:

- share singleton `Database<TDatabase>`, read access, and generated read roots across circuits
- inject the shared read root or application query services for UI reads
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

These are option-shape sketches, not finalized types. The service key belongs to registration in application code, not a mutable/bound `Name` property. H10-4 requires construction-time capture and no live reconfiguration; exact literal-versus-named connection conflicts and binding precedence must be specified before API freeze.

## Logging

DataLinq already has `DataLinqLoggingConfiguration` and provider constructors that accept `ILoggerFactory`.

DI integration should wire the host `ILoggerFactory` automatically when available, using the existing no-op configuration otherwise:

```csharp
var loggerFactory = sp.GetService<ILoggerFactory>();
return new MySqlDatabase<EmployeesDb>(connectionString, loggerFactory);
```

Users should not need to call `UseLoggerFactory(...)` manually when using DI, or register logging merely to construct a database.

Provider SQL logs, transaction logs, cache logs, startup validation logs, and schema validation logs should flow through the host logging pipeline with stable categories.

## Disposal

The DI container should own disposal for singleton database instances it creates.

Rules:

- databases created by the registration delegate are container-owned unless explicitly marked external
- externally supplied instances should not be disposed by DataLinq unless the user opts in
- manually created unit-of-work instances are caller-owned and disposed by `using`/`await using`; generated owning methods and owning callbacks manage their own completion/cleanup under H10-3, while participants never dispose the borrowed session
- startup validation should not dispose registered databases

This matters because hosted apps often run for a long time, and incorrect disposal ownership creates miserable shutdown bugs.

## Implementation Slices

### Slice 1: Core DI Registration

- Add DI registration under extension namespaces in `DataLinq`, following H10-1.
- Add `AddDataLinq<TDatabase>(...)`.
- Register `Database<TDatabase>` as singleton.
- Register provider-specific database concrete type as singleton where possible.
- Register `ReadOnlyAccess<TDatabase>` and generated `TDatabase` read root for injection.
- Reuse the provider-owned singleton read instances under H10-2; do not construct per-scope/per-request facades.
- Implement H10-4's keyed registration, coherent service selection, explicit default aliases, and duplicate/missing-target diagnostics.
- Wire optional `ILoggerFactory` automatically.
- Add unit tests for service resolution, lifetime behavior, and disposal ownership.

### Slice 2: Provider Registration Extensions

- Implement these extensions in the existing provider packages, following H10-1.
- Add `UseMySql(...)`.
- Add `UseMariaDb(...)`.
- Add `UseSQLite(...)`.
- Support direct connection strings and named connection strings.
- Support provider-specific options binding.
- Record construction recipes without provider creation; resolve, validate, and capture immutable settings at first resolution under H10-4.
- Add tests that validate configuration binding and connection-string resolution without connecting to live databases.

### Slice 3: Explicit Unit of Work API

- Implement H10-3's participant/owner/factory contracts after the bounded signature review.
- Reuse managed `Transaction<TDatabase>` behavior and return values, without a mandatory extra `Begin()` step or duplicate transaction engine.
- Keep physical commit, rollback, and disposal with the owner; add the irreversible participant rollback request and post-veto execution rejection.
- Add native typed results and an outcome-aware owning helper, preserving existing `CommitAsync` semantics.
- Extend the bundled generator with opted-in owner/participant service methods that share runtime lifecycle helpers.
- Bind generated owning factories to the selected registration; participating calls retain the supplied session's database identity under H10-4.
- Add consumer, generator, and runtime tests for composition, result propagation, completion, cancellation, and cleanup failures.

### Slice 4: Startup Validation Integration

- Implement this W5 slice in `DataLinq` under a hosting extension namespace, following H10-1.
- Reuse the runtime validation API from `Schema Validation Hooks.md`.
- Add database validation target registration, retaining H10-4's model/key identity without duplicating targets through default aliases.
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
- `ReadOnlyAccess<TDatabase>` and generated `TDatabase` resolve to the same existing instances across repeated resolutions and independent scopes, matching provider read access and `Database.Query()`.
- creating/resolving additional scopes does not reconstruct read access or generated table roots.
- disposing a child scope does not dispose shared database/provider/read services.
- host `ILoggerFactory` is passed to provider database creation.
- programmatic registration works without `ILoggerFactory` or `IConfiguration`, using no-op logging when absent.
- registration and container construction do not themselves construct providers or access databases; the first resolution validates/captures configuration and constructs one service graph, reused by all aliases and subsequent resolutions.
- named connection strings resolve from `IConfiguration` at first resolution; missing required configuration fails clearly without secrets.
- configuration changes after construction do not recreate or reconfigure that registration; changing configuration before first resolution is reflected in the captured settings.
- missing connection string fails with a clear error.
- multiple model types can be registered independently.
- same-model registrations support both different providers and the same provider with different connection strings, with separate read roots/caches/settings and correctly bound factories.
- enum/string constructor injection and dynamic keyed lookup select the entire matching service set; concrete/base/default aliases preserve identity.
- duplicate model/key registrations and competing defaults fail; missing keys and absent defaults never use implicit fallback or registration-order selection.
- disposal happens once for container-owned singleton databases.
- external database instances are not disposed unless configured.

Unit-of-work tests:

- factory creates a transaction-backed unit of work.
- participant interface exposes no physical commit, rollback, or disposal; helper-owned lifecycle also rejects borrowed completion at runtime.
- coordinator commits once.
- participant rollback requests prevent commit and further database execution without prematurely disposing the shared transaction.
- disposal rolls back uncommitted work according to existing transaction semantics.
- generated services work both independently and as explicit participants, with one business implementation and no ambient session.
- generated owning factory resolution honors its selected registration key; participating calls retain the supplied session even when the service's standalone factory targets another registration.
- string, typed, and same-type success/failure payloads preserve their cases; default results cannot commit.
- mapped/forwarded child failures preserve the veto; ignored child failures cannot produce outer success.
- expected rollback results are returned only after confirmed rollback/no started work and successful cleanup; exceptions/cancellation/unknown outcomes still throw.
- existing `CommitAsync` callbacks retain their contract; new helpers invoke once, await all work, and preserve recovery/finalization rules.

Startup validation tests:

- registered validation target runs at startup.
- validation failure prevents host startup when policy requires it.
- warning-only differences log without throwing when configured.
- connection strings are not logged.
- multiple registered databases produce separate validation summaries.
- keyed targets resolve their existing database registration; default aliases do not construct another provider or cause duplicate validation.

Integration tests:

- ASP.NET Core test host can resolve read root in Minimal API handler.
- concurrent handlers share read-root identity while independent executions preserve cancellation/failure isolation.
- write handler can use unit-of-work factory.
- a singleton worker can consume the shared read root/factory directly, with separate scopes only for scoped application services.
- SQLite registration can use a configured local path.

## Risks and Sharp Edges

- **Shared read root:** injected `TDatabase` is shared query infrastructure. Application partial members must not store request-local state, and ordinary reads must not imply transaction participation or snapshot isolation.
- **Ambient sessions:** `AsyncLocal` can be useful, but it makes transaction ownership less obvious. It should not be first-slice behavior.
- **Blazor Server scope semantics:** scoped does not mean request-scoped in Blazor Server. Documentation must be blunt about this.
- **Multiple same-model registrations:** H10-4 requires explicit target selection across reads, factories, generated owners, and startup validation. Shared model types do not imply shared caches or one transaction across registrations.
- **Startup validation availability:** validation depends on provider metadata readers and live database access. Failures need precise logs or users will disable the feature.
- **Package dependency creep:** pulling ASP.NET Core into the base runtime would be a mistake. Keep web conveniences optional.

## Open Questions

- Package placement is resolved by [H10-1](#h10-1-integration-in-existing-packages): existing core/provider packages, extension namespaces, and a separate package only for future ASP.NET-dependent helpers. Exact extension API spelling and packed dependency evidence remain for review.
- Read-service identity/lifetimes and direct read-access injection are resolved by [H10-2](#h10-2-shared-singleton-read-services): expose the existing database/read-access/generated-root instances as singletons. Container/external-instance disposal ownership mechanics remain to be reviewed.
- Unit-of-work ownership/composition is resolved by [H10-3](#h10-3-explicit-composable-units-of-work): owner-only physical completion, explicit participants with rollback requests, native results, generated service entry points, and preparation for future native unions. Its bounded signature/generator follow-up is required before API freeze.
- Registration/configuration and first-slice keyed support are resolved by [H10-4](#h10-4-registration-configuration-and-keyed-instances): explicit provider selection, first-resolution immutable settings, optional logging/configuration dependencies, model/key identity, coherent keyed services, and explicit default aliases. Exact overloads, options conflicts, alias disposal mechanics, and generated factory wiring remain bounded API work.
- W4 participant sharing is explicit only under H10-3. Any ambient or savepoint scope is a separate later proposal.
- Should endpoint filters/middleware be included in the ASP.NET package or documented as application code?

## References

- ASP.NET Core dependency injection: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection>
- .NET dependency injection overview: <https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection>
- .NET generic host: <https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host>
- .NET MAUI dependency injection: <https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/dependency-injection>
- Avalonia dependency injection: <https://docs.avaloniaui.net/docs/app-development/dependency-injection>
