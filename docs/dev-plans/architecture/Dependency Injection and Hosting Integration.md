> [!WARNING]
> This document is roadmap/specification material. It describes planned behavior, not shipped DataLinq behavior.

# Specification: Dependency Injection and Hosting Integration

**Status:** Accepted.
**Release horizon:** DataLinq 0.10 for unnamed and keyed registration, explicit unit of work, and startup-host integration; broader host variants remain later work.
**Last reviewed:** 2026-10-05 (H10-10 existing-instance ownership handoff, following H10-1 through H10-9; broad W4 design decisions are recorded, with bounded API details and implementation verification remaining).
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

The `UseMySql()` call selects the provider. Binding configures that selected provider and the associated validation policy; it cannot change provider type or registration key. Capture effective options at construction under H10-4, without a live reload subscription. H10-9 settles typed provider configuration, binding/override precedence, and connection-source conflict behavior; exact overload/type and diagnostic spelling remains API-review work. Startup validation execution remains W5.

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

#### H10-8: Session, Owner, And Factory Contracts

**Accepted:** 2026-10-05. Keep three public roles: `IDataLinqSession<TDatabase>` for participation, `IDataLinqUnitOfWork<TDatabase>` for ownership, and `IDataLinqUnitOfWorkFactory<TDatabase>` for creation and owned execution. The owner inherits the participant contract and both disposal interfaces. Reuse the existing managed `Transaction<TDatabase>` as the implementation where practical, without a second transaction engine or mandatory wrapper allocation. These are accepted contract choices; the abbreviated declarations and family inventory below still need complete overload/annotation and consumer verification before implementation API freeze.

The participant exposes transaction-bound reads/mutations, rollback requests, and diagnostics, without provider/raw-access or completion/disposal members:

```csharp
public interface IDataLinqSession<TDatabase>
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    TDatabase Query();
    bool IsRollbackRequested { get; }

    Rollback<TFailure> RequestRollback<TFailure>(TFailure reason)
        where TFailure : notnull;

    DataLinqFailureContext? FailureContext { get; }

    // Typed TryGetRollbackReason<TFailure>(...) and operation families below.
}
```

Define this independently of [`IDataSourceAccess<TDatabase>`](../../../src/DataLinq/Interfaces/IDataSourceAccess.cs), whose `Provider` and `DatabaseAccess` members expose lower-level infrastructure. The interface expresses participant rights; H10-3's runtime restrictions still enforce borrowed completion and do not promise protection against arbitrary casts.

Preserve the existing [transaction](../../../src/DataLinq/Mutation/Transaction.cs) and [async mutation](../../../src/DataLinq/Mutation/Transaction.PublicAsyncMutation.cs) operation families and their return values:

| Session operation family | Return shape |
| --- | --- |
| `Query()` | Transaction-bound generated `TDatabase` root. |
| `Get` / `GetAsync` | Nullable model / `ValueTask` of nullable model. |
| `From(...)` | Transaction-bound fluent query. |
| `Insert`, `Update`, `Save` | Immutable model / `Task` of immutable model. |
| Collection insert | Model list / `Task` of model list. |
| `Delete` | `void` / `Task`. |

Retain existing change-delegate overloads, generic constraints, finite collection handling, cancellation parameters, and nullable mutation behavior. Generated model helpers currently taking `Transaction` gain appropriate session-taking counterparts so participating callers can use model conveniences without casting to an owner. Existing source-less helpers keep their independent owning behavior; session-taking helpers retain the supplied transaction.

The owner extends the session directly:

```csharp
public interface IDataLinqUnitOfWork<TDatabase> :
    IDataLinqSession<TDatabase>, IDisposable, IAsyncDisposable
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    void Commit();
    Task CommitAsync(CancellationToken cancellationToken = default);

    void Rollback();
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
```

An owner can be passed directly where a participant session is required; no `unit.Session` property or separate session object is required. `Transaction<TDatabase>` should implement this contract where practical, retaining managed mutable/cache finalization, the operation gate, and existing synchronous/asynchronous disposal behavior.

The singleton factory is bound to one database registration:

```csharp
public interface IDataLinqUnitOfWorkFactory<TDatabase>
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    IDataLinqUnitOfWork<TDatabase> Create(
        TransactionType transactionType = TransactionType.ReadAndWrite);

    // Outcome-aware Execute and ExecuteAsync overloads.
}
```

`Create()` synchronously creates a logical transaction, mirroring `Database.Transaction()` and its existing lazy provider transaction initialization; it introduces no extra `Begin()` step. `Execute` and `ExecuteAsync` use the shared owning helpers for synchronous and asynchronous native-result callbacks, respectively, including explicit session/token delivery on the async path. Apply H10-3's result/veto/completion table and finish required cleanup before returning. Preserve H10-6 nullable/no-value behavior and existing `CommitAsync` callback semantics.

**Inspection:** `IsRollbackRequested` reports the business veto, not whether commit is otherwise permitted. A typed `TryGetRollbackReason<TFailure>(...)` exposes the original request reason; parent mapping changes the returned business result without replacing that original diagnostic reason. `FailureContext` retains the existing operational failure snapshot. [`DataLinqFailure.GetContext(exception)`](../../../src/DataLinq/Diagnostics/DataLinqFailure.cs) remains available after owning-helper cleanup. Keep business rollback reasons separate from operational cause/outcome/recovery information rather than introducing another general failure/state model.

**Verification gate:** compile the full inherited interface inventory against the existing transaction, verify session-aware generated helper binding and mutation return values, and test manual/generated participation, owner-only completion, sync/async factory execution, original-reason inspection, and post-cleanup operational diagnostics. Final namespace placement, complete overload/nullable annotations, and diagnostics spelling are bounded implementation API work, not reasons to reopen these ownership roles.

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

#### H10-6: Nullable And No-Value Results

**Accepted:** 2026-10-05. Complete H10-3's transaction-specific result semantics with nullable successes, an explicit success value for operations without a payload, non-null rollback reasons, and guarded inspection of uninitialized results. These belong to W4 because handwritten/generated transaction operations need them. This is not a general-purpose option/union library or broader ThrowAway replacement; exact helper/type spelling remains part of API review.

| Result | Meaning |
| --- | --- |
| Success containing a value | This operation succeeded with that value. |
| Success containing `null` for a nullable success type | This operation succeeded with no matching value. |
| Success with the no-value payload | This operation succeeded and has no value to return. |
| Rollback containing a non-null reason | Expected business failure, subject to the shared veto and owner completion rules. |
| Uninitialized/default result | Programming error; never permission to commit. |

The discriminator selects the case independently of payload nullness or default values. A nullable success payload is valid; it does not imply rollback. Business logic that requires a value explicitly requests rollback when that value is missing. For example:

```csharp
// Inside an operation returning TransactionResult<Order?, OrderError>.
Order? order = await session.Query().Orders.SingleOrDefaultAsync(
    x => x.Id == orderId, cancellationToken);

return order; // A missing order is a successful null result.
```

For operations without a return value, use a small DataLinq-owned value illustrated as `Unit` in the existing success slot: `TransactionResult<Unit>` for string failures or `TransactionResult<Unit, OrderError>` for typed failures. An explicit success helper can keep the business body concise:

```csharp
// Inside an operation returning TransactionResult<Unit, OrderError>.
if (order.HasShipped)
    return session.RequestRollback(OrderError.AlreadyShipped);

await MarkCancelledAsync(session, order, cancellationToken);
return TransactionResult.Success();
```

`Unit` and `TransactionResult.Success()` illustrate the accepted small API direction, not frozen names/signatures. The helper supplies a valid no-value success payload; it does not commit, clear a veto, or complete a participant's transaction. The enclosing result must still have its success case initialized. `default(TransactionResult<Unit, TFailure>)` remains invalid. Do not reinterpret `TransactionResult<TFailure>` as a no-value typed-failure result: its existing single generic argument is the success payload, with a string failure.

`TryUnwrap` returns `true` for a valid success, including a permitted null payload, and `false` only for a valid rollback case. Inspecting an uninitialized result through `TryUnwrap` throws rather than fabricating a business failure or a successful default value. Keep other wrong-case payload access guarded. Rollback reasons must be non-null; reject a null reason as invalid usage rather than a normal business result. Operational exceptions retain the existing exception/recovery path.

The owner's H10-3 completion table remains unchanged: nullable/no-value successes still require no veto and successful commit/finalization/cleanup before the owner returns normally. Participating success remains provisional. Complete nullable annotations, explicit construction/inspection signatures, generic conversion edge cases, and validation diagnostics during API review; verify them in handwritten and generated consumer examples before freezing the surface. Native .NET 11 union integration remains later work.

#### H10-7: Generated Service Authoring And Wiring

**Accepted:** 2026-10-05. An opted-in partial service has an ordinary constructor with an explicit transaction-factory dependency, one private authored business method per operation, and two generated public entry points. Extend the existing bundled generator to supply operation wrappers and registration wiring. A marker interface identifies the database and an attribute selects operation methods; `IDataLinqService<TDatabase>` and `[DataLinqOperation]` illustrate those roles. Keep manual construction available and transaction lifecycle behavior in shared runtime helpers.

Register a service against the database whose factory should own its standalone operations:

```csharp
services.AddSingleton<IOrderPolicy, OrderPolicy>();

services.AddDataLinq<ShopDb>(ShopConnection.Primary)
    .AsDefault()
    .UseMySqlConnectionString("ShopPrimary")
    .AddSingletonService<OrderService>();

services.AddDataLinq<ShopDb>(ShopConnection.SQLiteTest)
    .UseSQLite(sqliteConnectionString)
    .AddSingletonService<OrderService>();
```

Service registration binds the transaction factory to that database registration and resolves other constructor dependencies through ordinary DI. Services registered this way inherit its key; an explicitly selected default database also supplies the default service alias. Keyed/default aliases resolve the same service instance within its selected lifetime, without duplicate activation or disposal ownership. Other constructor dependencies do not automatically inherit the database key merely because the service's owning factory is bound to it.

```csharp
var orders = serviceProvider.GetRequiredService<OrderService>();
var sqliteOrders = serviceProvider.GetRequiredKeyedService<OrderService>(
    ShopConnection.SQLiteTest);
```

Use ordinary constructor injection in the authored service:

```csharp
public partial class OrderService(
    IDataLinqUnitOfWorkFactory<ShopDb> transactions,
    IOrderPolicy policy)
    : IDataLinqService<ShopDb>
{
    [DataLinqOperation]
    private async Task<TransactionResult<Order, OrderError>> PlaceCoreAsync(
        IDataLinqSession<ShopDb> session,
        CreateOrder command,
        CancellationToken cancellationToken)
    {
        var customer = await session.Query().Customers.SingleAsync(
            x => x.Id == command.CustomerId, cancellationToken);

        if (!policy.CanPlaceOrder(customer))
            return session.RequestRollback(OrderError.CustomerBlocked);

        return await session.InsertAsync(
            new MutableOrder { CustomerId = customer.Id }, cancellationToken);
    }
}
```

The generator identifies the matching factory parameter by type in a primary constructor and uses it in generated owning members. For traditional constructors, use an explicitly designated factory field/property rather than inferring arbitrary constructor assignments. The designation syntax and exact constructor-selection diagnostics remain bounded API details. The service can also be constructed directly, for example `new OrderService(testTransactionFactory, testPolicy)`; registration must not be required to initialize a hidden current session or factory afterward.

By convention, `PlaceCoreAsync` produces public `PlaceAsync` owning and participating methods, with generated-name collisions diagnosed at compile time:

```csharp
// Own a new transaction and finish completion/cleanup before returning.
Task<TransactionResult<Order, OrderError>> PlaceAsync(
    CreateOrder command, CancellationToken cancellationToken = default);

// Borrow the supplied session; do not physically complete or dispose it.
Task<TransactionResult<Order, OrderError>> PlaceAsync(
    IDataLinqSession<ShopDb> session,
    CreateOrder command, CancellationToken cancellationToken = default);
```

The owning method delegates to the shared outcome-aware helper and returns after commit or expected rollback and cleanup. The participating method also crosses a managed helper boundary: it validates participation and observes returned rollback results so the shared veto is retained, while leaving physical completion with the owner. The business method remains private so callers use these managed entry points.

The standalone method returns the union too: different success/failure types cannot both be returned as a plain `T`. A participating success means that service's work succeeded so far, not that the outer transaction committed. Services can therefore be used independently or stacked under one owner without duplicating their business code. Calling an owning overload from inside another operation still starts an independent unit; callers must use the session-taking overload to participate. Under H10-4, the supplied session determines the participant's database even when its standalone factory targets a different registration.

A parent can translate a child's failure while preserving the veto:

```csharp
// Inside an operation returning TransactionResult<Checkout, CheckoutError>.
var result = await orders.PlaceAsync(session, command.Order, cancellationToken);

if (!result.TryUnwrap(out var order, out var failure))
    return session.RequestRollback(CheckoutError.FromOrderError(failure));

return new Checkout(order.Id);
```

**Service lifetimes:** offer explicit singleton, scoped, and transient service registration, illustrated as `AddSingletonService`, `AddScopedService`, and `AddTransientService`. A stateless service may be singleton when its dependencies support that lifetime and concurrent use. Applications with scoped dependencies choose an appropriate service lifetime; database/provider/read infrastructure retains H10-2's singleton lifetime. Each operation creates or borrows its own session, with no current transaction stored on the service.

**Supported method forms:** generate the matching family for each authored operation:

| Authored return type | Generated entry points |
| --- | --- |
| `TransactionResult<...>` | Synchronous owning and participating methods. |
| `Task<TransactionResult<...>>` | Asynchronous owning and participating methods. |

An async authored method receives an explicit cancellation token; generated async entry points expose an optional token and forward it. Use the corresponding synchronous or asynchronous runtime execution path; do not synthesize sync-over-async or thread-pool wrappers. H10-6's no-value operations still return an explicit transaction result.

Keep all completion/recovery behavior in shared runtime helpers; generated methods forward to those helpers rather than copying rollback/finalization logic. The same protocol must be usable without generation. Compiler diagnostics cover missing `partial`, mismatched database/session types, ambiguous factory selection, unsupported returns such as `async void`, and generated-name collisions. Runtime enforcement remains necessary: generation cannot prove arbitrary application control flow handles every child result correctly.

**Verification gate:** compile and execute consumer-shaped primary/traditional-constructor services, direct construction, keyed/default activation, all three application-service lifetimes, sync/async wrappers, cancellation forwarding, and nested failure propagation. Verify that the participating wrapper records forwarded rollback cases and never completes/disposes its owner's transaction. Exact attribute/member designation spelling, registration bridge implementation, and the complete diagnostics/signature inventory remain API/implementation work; the authoring and wiring design is settled.

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

- complete the H10-8 overload/nullable inventory and namespace/diagnostic spelling against existing runtime conventions, including session-aware generated model helpers; the three interface roles, core members, inheritance, and operation families are settled;
- finalize H10-7's attribute/member designation spelling, constructor-selection and collision diagnostics, and generated registration bridge, preserving its settled constructor/factory, key/lifetime, naming-convention, and sync/async behavior;
- finalize H10-6's result surface spelling, nullable annotations, explicit construction/inspection, validation diagnostics, and unusual generic conversions; nullable/no-value semantics and invalid-default rejection are settled;
- prove generated and handwritten composition with consumer-shaped tests, including same-type/string payloads, failure mapping, ignored vetoes, post-veto execution rejection, borrowed completion, cancellation, cleanup errors, and no callback replay;
- retain separate follow-up for native union compilation/compatibility and for a possible repository-wide ThrowAway replacement.

Exact mechanics may be settled during API review; they must not silently change these accepted semantics. H10-4 settles registration/configuration behavior, H10-5 settles resource ownership/shutdown rules, H10-9 settles the registration/options surface and factory-ownership refinement, and H10-10 settles the existing-instance transfer boundary. Exact overloads and disposal bookkeeping remain for verification. W6B fakes consume the final W4 signatures and these behaviors rather than inventing a competing lifecycle.

## Service Lifetimes

### H10-2: Shared Singleton Read Services

**Accepted:** 2026-10-01. Register `Database<TDatabase>`, the provider's existing `ReadOnlyAccess<TDatabase>`, and that access object's existing generated `TDatabase` read root as singletons. Share these same instances across resolutions, requests, and DI scopes within one service provider/database registration. This supersedes the earlier scoped/transient read-facade proposal. The provider-specific database and its base `Database<TDatabase>` service identify the same database instance.

**Rationale:** reusable read infrastructure is part of DataLinq's design and a benefit to preserve in DI. The current provider already constructs and retains read access, which constructs and retains the generated root; `Database<TDatabase>.Query()` returns that root. Registration should expose those objects without recreating read facades or table/query roots per request. Singleton here means per application container/registration, not a process-global static shared by independent service providers.

Read roots retain no current request, user, request token, or active unit of work. Application-added partial members must respect their shared lifetime. Individual query executions, active enumerators, mutable query builders, connections, and transactions retain their existing operation/resource lifetimes; sharing the root does not authorize concurrent use of one active enumerator or transaction. An injected read root provides no operation-wide snapshot or transaction participation. Reads that must join a unit of work use that unit's transaction-bound root.

Expose `ReadOnlyAccess<TDatabase>` directly as well as the generated `TDatabase`; this decision does not introduce a new read-access interface. Ending a child DI scope must not dispose the shared database/provider or read services. Preserve one disposal owner for each owned database/provider pair under [H10-5](#h10-5-resource-ownership-and-shutdown), including its external-instance rules; concrete registration/disposal bookkeeping still requires implementation evidence. Transactions and unit-of-work instances remain explicitly created per operation, never shared singletons.

**Owner/gate:** H10/W4. Verify reference identity across repeated resolutions and independent scopes, identity with the provider's read access and `Database.Query()`, no repeated read-root construction per scope, direct consumption by singleton workers, concurrent independent reads and cancellation/failure isolation, and correct scope/host disposal. This is an accepted integration design, not evidence that DI registration is implemented.

Services exposed by each registration (unnamed or keyed under H10-4):

| Service | Lifetime | Reason |
| --- | --- | --- |
| `Database<TDatabase>` | Singleton | Owns provider/cache/state and is expensive enough to treat as app-level infrastructure. |
| Provider-specific database, e.g. `MySqlDatabase<TDatabase>` | Singleton | Same object as the base database registration. |
| `ReadOnlyAccess<TDatabase>` | Singleton | Exposes the provider's existing shared read access. |
| Generated `TDatabase` read root | Singleton | Exposes that read access object's existing root, also returned by `Database.Query()`. |
| `IDataLinqUnitOfWorkFactory<TDatabase>` | Singleton | Creates explicit transactions from the singleton database root. |
| `IDataLinqUnitOfWork<TDatabase>` | Explicit/disposable per operation | Owner-controlled completion under H10-3; participants borrow the explicit session. H10-8 settles the interface roles and core members; full overloads/annotations still require verification. |
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
- Preserve H10-2's single disposal owner across concrete/base and keyed/default aliases under H10-5. Exact container bookkeeping remains an implementation detail requiring verification.

**Dynamic selection and tests:** test fixtures can choose a registered target using standard keyed resolution:

```csharp
var database = serviceProvider.GetRequiredKeyedService<ShopDb>(connection);
var transactions = serviceProvider.GetRequiredKeyedService<
    IDataLinqUnitOfWorkFactory<ShopDb>>(connection);
```

Alternatively, build separate test containers with the chosen provider as the default so application constructors remain unchanged. Start with constructor injection and standard keyed lookup; the earlier separate named `IDataLinqDatabaseFactory<TDatabase>` resolver is not required for W4. Add a DataLinq-specific resolver only if concrete usage justifies it.

**Generated service composition:** H10-7 binds an explicit constructor factory through the service's database registration, without requiring connection selection in the authored business method. A standalone call uses its selected factory. A participating call uses the explicitly supplied session, including its database/provider identity; it must not open another transaction from its own factory or use an independently injected read root for transactional reads. The same operation can therefore participate against different providers in tests. Transactions from separate registrations do not become one atomic transaction; distributed transactions are outside this decision.

**Remaining API work:** finalize overload/key argument spelling, `.AsDefault()` registration mechanics and diagnostics, and generated service activation, applying H10-7's wiring contract and H10-9's typed options/precedence rules. Basic keyed support is accepted W4 scope, not deferred pending a new abstraction. No implementation or consumer verification is claimed by these examples.

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

### H10-9: Registration Surface, Options Precedence, And Factory Ownership

**Accepted:** 2026-10-05. Support simple provider setup, provider-specific options binding with explicit code overrides, existing-instance registration, and custom factory construction. Each model/key registration owns its configuration recipe and captures immutable effective settings under H10-4. Factory results are always container-owned; this explicitly narrows H10-5's earlier allowance for factories returning borrowed instances. The examples show the accepted public direction, with exact overload/type spelling still subject to consumer compilation and API review.

Simple setup remains short, with both literal and named connection-string forms:

```csharp
services.AddDataLinq<ShopDb>(ShopConnection.Primary)
    .AsDefault()
    .UseMySqlConnectionString("ShopPrimary");

// UseMySql(connectionString) supplies a literal directly instead.
```

Selecting a provider exposes its typed settings. `UseMySql()` configures MySQL options; `UseSQLite()` configures SQLite options. Common execution settings remain shared DataLinq types, while provider-specific settings stay with the provider package:

```csharp
services.AddDataLinq<ShopDb>(ShopConnection.Reporting)
    .UseMySql()
    .Bind(configuration.GetSection("DataLinq:Reporting"))
    .Configure(options =>
    {
        options.Execution = new DataLinqExecutionOptions
        {
            RecoveryRollbackTimeout = TimeSpan.FromSeconds(15)
        };
    });
```

Apply these configuration rules:

- Settings belong to one registration, including when multiple keys use the same database model and provider. Do not accidentally share a mutable/global options instance across those registrations.
- Apply **defaults → bound configuration → explicit code overrides → validation → immutable capture**. Binding precedes explicit overrides; multiple code configuration callbacks run in registration order.
- A literal connection string and a configuration connection-string name are alternative sources for one connection selection. An explicit code selection replaces the bound selection as a whole; it must not leave a stale competing bound source. Reject an ambiguous effective selection instead of silently choosing one. Final overload/options representation must preserve this behavior.
- Diagnose structural registration mistakes before provider construction. Resolve required configuration, validate effective settings, and capture them at first resolution, preserving H10-4's optional configuration/logging and no-live-reconfiguration rules. Diagnostic messages must not expose connection secrets.
- Provider type and service key remain code-level registration choices. Binding cannot change either one.
- Binding configures newly constructed databases; it cannot reconfigure an existing instance supplied through `UseInstance`.

For existing instances and custom construction, keep ownership visible:

```csharp
// The application retains ownership by default.
services.AddDataLinq<ShopDb>(ShopConnection.SQLiteTest)
    .UseInstance(testDatabase);

// A custom factory supplies a container-owned database.
services.AddDataLinq<ShopDb>(ShopConnection.Primary)
    .UseFactory(services =>
        new MySqlDatabase<ShopDb>(connectionString));

// Explicit transfer is supported; ownership enum/type spelling is illustrative.
services.AddDataLinq<ShopDb>(ShopConnection.Reporting)
    .UseInstance(reportingDatabase, ownership: DataLinqOwnership.Container);
```

These are independent registration examples. `UseFactory` participates in the first-resolution construction recipe and always supplies a container-owned result. It offers no application-owned/borrowed result option; use `UseInstance` when the application retains ownership. Explicit transfer remains available for an existing instance, under H10-5's single-owner and shutdown rules. This refines the earlier factory ownership default into an invariant and avoids requiring a borrowed-factory abstraction in W4. It follows the distinction between factory-created and supplied-instance disposal in [Microsoft's DI guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines).

**Ownership handoff:** [H10-10](#h10-10-existing-instance-ownership-handoff) resolves the follow-up: an explicit existing-instance transfer takes effect on successful first activation. The application retains ownership if the registration is never resolved or activation fails before handoff; the container owns cleanup after handoff. Verify this alongside concrete/base/keyed/default aliases and synchronous/asynchronous cleanup. Alias bookkeeping must also preserve H10-7's generated-service identity and disposal behavior.

**Verification gate:** compile consumer-shaped simple, bound, overridden, instance, and factory registrations. Verify per-key options isolation, ordered overrides, literal/name replacement and ambiguity failures, immutable capture, factory ownership, borrowed-instance survival, and the final explicit-transfer contract. Exact builder/options/ownership type names, overloads, and diagnostics are bounded implementation API work; this record does not claim those APIs are shipped or W4 is complete.

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

### H10-5: Resource Ownership And Shutdown

**Accepted:** 2026-10-04; factory ownership refined by [H10-9](#h10-9-registration-surface-options-precedence-and-factory-ownership) and explicit instance handoff settled by [H10-10](#h10-10-existing-instance-ownership-handoff) on 2026-10-05. Each database/provider lifetime has one disposal owner, regardless of how many service types or keys expose it. Ownership follows how the instance enters registration, with explicit transfer available for externally created instances. These are accepted W4 semantics; exact registration overloads and container disposal bookkeeping remain to be implemented and verified.

| Registration source | Disposal owner |
| --- | --- |
| DataLinq constructs the database from connection/settings configuration | The container. |
| An application supplies a database factory through `UseFactory` | The container, always under H10-9. |
| An application supplies an already-created instance | The application by default. |
| An application explicitly transfers ownership of an existing instance | The container after successful first activation under H10-10; the application before handoff. |
| Another service type/key or the default registration aliases that instance | The existing owner; the alias creates no additional owner. |

Factory registration and instance registration make that distinction explicit: `UseFactory` always supplies a container-owned result, while `UseInstance` keeps application ownership unless explicitly transferred. H10-9 supersedes the earlier allowance for a factory to declare a borrowed/external result. H10-10 specifies the existing-instance handoff point, including never-resolved registrations and failed activation. Exact ownership-option spelling and implementation evidence remain for review.

All concrete/base database services, keyed/default aliases, read services, and factories refer back to the same ownership arrangement. Container shutdown disposes an owned database/provider lifetime once, through its owning root; aliases must not cause duplicate root/provider cleanup. Child scope disposal leaves singleton infrastructure available. Startup validation borrows registered databases and never acquires their disposal ownership. Externally owned instances remain usable after container disposal until the application disposes them.

**Shutdown ordering:** application workers and operations stop accepting new work, await their in-flight work, and finish/dispose their transactions and readers before the owning database/provider is disposed. Manually created units of work remain caller-owned through `using`/`await using`; generated owning methods and owning callbacks finish completion/cleanup under H10-3, while participants never dispose the borrowed session. Root disposal does not implicitly take over application transaction completion or introduce a new transaction-draining mechanism.

Preserve the existing synchronous and asynchronous cleanup contracts, including failure reporting and once-only root cleanup. Use the appropriate root disposal path for synchronous or asynchronous container shutdown; unsupported async cleanup retains its existing explicit failure behavior rather than silently falling back to synchronous cleanup. H10-5 does not add a hard shutdown deadline or change the accepted cancellation/recovery policies.

**Verification gate:** prove container-created and factory-created ownership, borrowed external instances, explicit ownership transfer, and all concrete/base/keyed/default aliases. Check child-scope versus host disposal, synchronous and asynchronous shutdown, preserved cleanup failures, and dependent operation cleanup before root disposal. Verify H10-10's existing-instance handoff point, including never-resolved registrations and activation failures. Final overload names, alias tracking, and host integration evidence remain open; the ownership policy is settled.

### H10-10: Existing-Instance Ownership Handoff

**Accepted:** 2026-10-05. For an existing database registered with explicit container ownership, illustrated as `UseInstance(existing, ownership: DataLinqOwnership.Container)`, disposal responsibility transfers on **successful first activation of the DataLinq registration**. Registering the instance or merely building the container does not transfer ownership. This resolves H10-9's remaining lifecycle question; it is an accepted contract, not evidence of implemented container tracking.

| Registration state | Disposal responsibility |
| --- | --- |
| Registered, but not successfully activated | The application retains ownership. |
| Never resolved before container shutdown | The application retains ownership; the container must not dispose the instance. |
| Activation fails before handoff | The application retains ownership, including responsibility for eventual disposal. |
| First activation succeeds and handoff completes | The container owns cleanup through all concrete/base/keyed/default aliases. |
| A later operation or resolution fails after handoff | The container remains the owner; failure does not return ownership to the application. |

The boundary belongs to activation of the DataLinq registration, regardless of which registered service or alias first triggers it. Establish the handoff before publishing successfully activated services to callers, with no ownership gap or duplicate owner. Subsequent aliases do not transfer ownership again. Plain `UseInstance(existing)` remains application-owned throughout; H10-9's always-owned factory results retain their separate construction/cleanup contract.

**Verification gate:** prove that registration/container build alone and never-resolved shutdown leave the supplied instance application-owned. Inject activation failures before handoff and verify that container cleanup does not consume the supplied instance. Verify successful first activation through different aliases, once-only cleanup after handoff, and retained container ownership after later failures for synchronous and asynchronous shutdown. Apply the same final contract to testing helpers; implementation must demonstrate the handoff without silently changing the lazy registration policy.

## Implementation Slices

### Slice 1: Core DI Registration

- Add DI registration under extension namespaces in `DataLinq`, following H10-1.
- Add `AddDataLinq<TDatabase>(...)`.
- Register `Database<TDatabase>` as singleton.
- Register provider-specific database concrete type as singleton where possible.
- Register `ReadOnlyAccess<TDatabase>` and generated `TDatabase` read root for injection.
- Reuse the provider-owned singleton read instances under H10-2; do not construct per-scope/per-request facades.
- Implement H10-4's keyed registration, coherent service selection, explicit default aliases, and duplicate/missing-target diagnostics.
- Implement H10-5 ownership with H10-9's always-owned factories, borrowed instances by default, and H10-10's explicit transfer on successful first activation, with one disposal owner across all aliases.
- Wire optional `ILoggerFactory` automatically.
- Add unit tests for service resolution, lifetime behavior, and disposal ownership.

### Slice 2: Provider Registration Extensions

- Implement these extensions in the existing provider packages, following H10-1.
- Add `UseMySql(...)`.
- Add `UseMariaDb(...)`.
- Add `UseSQLite(...)`.
- Support direct connection strings and named connection strings.
- Support H10-9's provider-specific options binding and explicit overrides, with per-registration settings, deterministic precedence, and unambiguous connection-source selection.
- Record construction recipes without provider creation; resolve, validate, and capture immutable settings at first resolution under H10-4.
- Add tests that validate configuration binding and connection-string resolution without connecting to live databases.

### Slice 3: Explicit Unit of Work API

- Implement H10-3's participant/owner/factory contracts after the bounded signature review.
- Apply H10-8's separate participant interface, owner inheritance and existing transaction implementation, synchronous logical creation, owned execution helpers, and separate rollback/operational diagnostics.
- Add session-aware generated model helper overloads while preserving existing standalone ownership and transaction overload behavior.
- Reuse managed `Transaction<TDatabase>` behavior and return values, without a mandatory extra `Begin()` step or duplicate transaction engine.
- Keep physical commit, rollback, and disposal with the owner; add the irreversible participant rollback request and post-veto execution rejection.
- Add native typed results and an outcome-aware owning helper, preserving existing `CommitAsync` semantics.
- Extend the bundled generator with opted-in owner/participant service methods that share runtime lifecycle helpers.
- Apply H10-7's explicit constructor factory, private business method, generated owning/participating wrappers, keyed/default service registration, and chosen application-service lifetime.
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
- H10-9 provider settings remain isolated per model/key registration; defaults, binding, and explicit overrides follow the accepted precedence, and multiple code callbacks execute in order.
- explicit literal/named connection selections replace the bound selection as a whole; ambiguous effective selections fail clearly without exposing secrets.
- `UseInstance` retains existing database settings rather than applying binding to an already-created instance.
- missing connection string fails with a clear error.
- multiple model types can be registered independently.
- same-model registrations support both different providers and the same provider with different connection strings, with separate read roots/caches/settings and correctly bound factories.
- enum/string constructor injection and dynamic keyed lookup select the entire matching service set; concrete/base/default aliases preserve identity.
- duplicate model/key registrations and competing defaults fail; missing keys and absent defaults never use implicit fallback or registration-order selection.
- disposal happens once for container-created and application-factory-created singleton databases, including concrete/base/keyed/default aliases.
- externally supplied instances survive container disposal by default; explicit ownership transfer enables container disposal exactly once.
- `UseFactory` always supplies a container-owned result and has no borrowed-result option; H10-10 transferred instances remain application-owned until successful first activation, including never-resolved shutdown and activation failure before handoff, then remain container-owned after later failures.
- synchronous/asynchronous shutdown preserves the corresponding root cleanup and failure contracts, with dependent transactions/readers disposed before the root.

Unit-of-work tests:

- factory creates a transaction-backed unit of work.
- H10-8 owners can be passed directly as sessions, with matching query/mutation return values and session-aware generated model conveniences; the participant interface exposes no provider/raw access or completion/disposal members.
- original typed rollback reasons survive parent failure mapping, while operational snapshots remain available through existing transaction/exception diagnostics after helper cleanup.
- participant interface exposes no physical commit, rollback, or disposal; helper-owned lifecycle also rejects borrowed completion at runtime.
- coordinator commits once.
- participant rollback requests prevent commit and further database execution without prematurely disposing the shared transaction.
- disposal rolls back uncommitted work according to existing transaction semantics.
- generated services work both independently and as explicit participants, with one business implementation and no ambient session.
- H10-7 primary/traditional-constructor services support direct construction, matching synchronous/asynchronous wrappers, token forwarding, keyed/default service identity, and singleton/scoped/transient application-service lifetimes; invalid authoring shapes produce compile-time diagnostics.
- generated owning factory resolution honors its selected registration key; participating calls retain the supplied session even when the service's standalone factory targets another registration.
- string, typed, and same-type success/failure payloads preserve their cases; default results cannot commit.
- H10-6 nullable successes unwrap successfully even with a null payload; no-value successes initialize a valid case; null rollback reasons are rejected and uninitialized `TryUnwrap` throws. Verify generated and handwritten paths without treating local success construction as transaction completion.
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
- Read-service identity/lifetimes and direct read-access injection are resolved by [H10-2](#h10-2-shared-singleton-read-services): expose the existing database/read-access/generated-root instances as singletons. H10-5 settles their ownership rules; concrete alias bookkeeping still requires verification.
- Unit-of-work ownership/composition is resolved by [H10-3](#h10-3-explicit-composable-units-of-work): owner-only physical completion, explicit participants with rollback requests, native results, generated service entry points, and preparation for future native unions. Its bounded signature/generator follow-up is required before API freeze.
- Registration/configuration and first-slice keyed support are resolved by [H10-4](#h10-4-registration-configuration-and-keyed-instances): explicit provider selection, first-resolution immutable settings, optional logging/configuration dependencies, model/key identity, coherent keyed services, and explicit default aliases. H10-7/H10-9 settle generated-service wiring and options/connection conflict behavior; exact overloads and activation/disposal mechanics still require implementation evidence.
- Resource ownership/shutdown is resolved by [H10-5](#h10-5-resource-ownership-and-shutdown): container ownership for constructed/factory-created databases, application ownership for supplied instances unless explicitly transferred, one owner across aliases, and dependent operations completed before root disposal. Exact ownership option spelling and container implementation evidence remain required.
- Nullable/no-value result semantics are resolved by [H10-6](#h10-6-nullable-and-no-value-results): nullable successes, a small explicit no-value success payload, non-null rollback reasons, and invalid-default rejection including `TryUnwrap`. Exact public spelling/annotations and generic conversion evidence remain part of API review.
- Generated-service authoring/wiring is resolved by [H10-7](#h10-7-generated-service-authoring-and-wiring): explicit constructor factory, private business methods with two generated public entry points, matching sync/async forms, database-bound keyed/default service activation, and explicit service lifetimes. Exact designation syntax, registration bridge, and diagnostics/signature evidence remain implementation/API work.
- Session/owner/factory contracts are resolved by [H10-8](#h10-8-session-owner-and-factory-contracts): separate participant interface, owner inheritance with completion/disposal, synchronous `Create` plus owned `Execute`/`ExecuteAsync`, existing transaction operation families, session-aware model helpers, and separate business/operational diagnostics. The complete overload/annotation manifest and consumer evidence remain implementation gates.
- Registration/options surface and factory ownership are resolved by [H10-9](#h10-9-registration-surface-options-precedence-and-factory-ownership): typed per-registration provider settings, defaults/binding/code precedence, explicit connection-source replacement and ambiguity diagnostics, always-owned factory results, and borrowed existing instances unless explicitly transferred.
- Existing-instance transfer is resolved by [H10-10](#h10-10-existing-instance-ownership-handoff): explicit container ownership takes effect on successful first activation. Application ownership remains for never-resolved registrations and failures before handoff; container ownership remains after handoff, including later failures. Alias/disposal evidence is still required.

W4 design checkpoint:

H10-1 through H10-10 cover the broad W4 design topics and the explicit existing-instance handoff question. No further broad architecture discussion is required before preparing implementation. Public API completion and implementation evidence remain necessary; this is design agreement, not a W4 closeout.

Remaining implementation/API work:

- Finish the public overload/nullable/constraint manifest, builder/options/ownership names, result helper spelling, and generator attribute/member designation and diagnostic details.
- Verify generated service activation and concrete/base/keyed/default alias identity and disposal, including H10-10's transfer boundary, across supported consumers.
- Complete consumer, generator, runtime, dependency, and disposal tests/evidence before W4 closure. These checks may reveal necessary corrections; they are not additional broad design questions or permission to silently change accepted semantics.

Startup schema comparison/policy execution remains W5. W6B fakes depend on the final production contracts. This design checkpoint does not authorize implementation or close W4.

Later-scope questions:

- W4 participant sharing is explicit only under H10-3. Any ambient or savepoint scope is a separate later proposal.
- Should endpoint filters/middleware be included in the ASP.NET package or documented as application code?

## References

- ASP.NET Core dependency injection: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection>
- .NET dependency injection overview: <https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection>
- .NET generic host: <https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host>
- .NET MAUI dependency injection: <https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/dependency-injection>
- Avalonia dependency injection: <https://docs.avaloniaui.net/docs/app-development/dependency-injection>
