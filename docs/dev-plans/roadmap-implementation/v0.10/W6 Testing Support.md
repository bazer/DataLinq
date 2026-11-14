# W6 Testing Support

**Scope accepted on 2026-11-14 for DataLinq 0.10.** Ship one public `DataLinq.Testing` library for isolated SQLite-in-memory databases, transactional seeding, and application DI replacement. The package, assembly and root namespace use that name. Rename the internal harness to `DataLinq.TestInfrastructure`. This is the implementation plan. Fixture and DI method spellings below illustrate the intended public surface.

This decision supersedes the November 1 row and graph builder design, including typed graph handles and automatic foreign-key propagation. New Memory backend work is deferred to a later release. W6 introduces zero public mock types. The release tracks these decisions in the [T10 contract](README.md#t10-application-testing-support), [implementation order](Implementation%20Order%20and%20Integration%20Plan.md#w6-testing-support), and [release evidence plan](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md#testing-support).

## Package migration

1. Move the internal project, assembly and namespaces to `DataLinq.TestInfrastructure` and retain `IsPackable=false`. Update the solution, imports, tooling paths and five direct project consumers (Testing CLI, Benchmark, Unit, Compliance and MySql tests). The CLI keeps its existing command and assembly name.
2. Create a fresh public project for `DataLinq.Testing`, referencing core and SQLite. The current harness brings in Tools, repository models, Bogus and MySqlConnector. Keep that dependency tree with the harness. The consumer supplies its own SQLite-compatible model. The SQLite native runtime is an intentional package dependency. [Current internal project](../../../../src/DataLinq.Testing/DataLinq.Testing.csproj)
3. Add the public project to the solution, packaging inventory, package/API checks and DocFX metadata. Exercise ordinary external consumers on .NET 8, 9 and 10 using the packed library. Inspect the dependency closure and verify that the renamed harness stays out of the package.

## Fixture contract

Creation provisions an isolated schema from generated metadata. The fixture exposes the real SQLite database, a committed query root, and the W4 factory that the application takes in its constructor. Use one fresh fixture per test and dispose it after its transactions, readers and host.

```text
SqliteTestDatabase<TDatabase>.Create() / CreateAsync(cancellationToken)
fixture.Database          -> SQLiteDatabase<TDatabase>
fixture.Query()           -> TDatabase
fixture.UnitOfWorkFactory -> IDataLinqUnitOfWorkFactory<TDatabase>
fixture.Seed(action) / Seed<TResult>(function)
fixture.SeedAsync(asyncAction, cancellationToken) / SeedAsync<TResult>(asyncFunction, cancellationToken)
fixture.Dispose() / DisposeAsync()
```

The sync callbacks take `IDataLinqSession<TDatabase>`. The async callbacks also receive the cancellation token and return `Task` or `Task<TResult>`. Each accepted seed call delegates once to the real factory, owns a single transaction, and returns its result after successful commit and cleanup. A callback exception rolls back earlier writes. Preserve provider cancellation and failure behavior. A schema setup failure releases the fixture's resources before returning the error. [Session contract](../../../../src/DataLinq/Interfaces/IDataLinqSession.cs) · [Factory contract](../../../../src/DataLinq/Interfaces/IDataLinqUnitOfWorkFactory.cs)

Synchronous `Seed` accepts only synchronous callbacks. In particular, `await fixture.Seed(async s => { ... })` must fail to compile or be rejected before the callback runs, with a diagnostic directing the caller to `SeedAsync`. Cover inferred and explicitly typed delegates returning `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>`, and an async lambda converted to the void action overload. Inspecting the result after invocation is too late because the callback may already have written a row. The synchronous factory begins completion when its delegate returns. [Synchronous callback completion](../../../../src/DataLinq/Mutation/Transaction.SyncCallback.cs)

Give each fixture a unique named in-memory store shared by that fixture's connections. Reuse the provider's keeper lease, enable FK enforcement on every connection, and use `Pooling=False` on fixture-owned connections so the final close releases the store. Unsupported schema features must surface during setup. Schema creation uses the effective connection identity and requires only model metadata. [SQLite provider](../../../../src/DataLinq.SQLite/SQLiteProvider.cs) · [Keeper ownership](../../../../src/DataLinq.SQLite/SQLiteConnectionStringFactory.cs)

For example, `Create` and `CreateAsync` may return a fixture for a model with `[Check("ck_amount", "amount > 0")]` only if SQLite enforces that constraint. Today's SQLite DDL generator omits model checks and W5 skips their comparison on SQLite. Until provisioning supports a check applicable to SQLite, fail creation with a diagnostic naming the table and constraint. If provisioning gains support, verify enforcement with an otherwise valid insert whose amount is negative. Passing W5 validation alone cannot establish enforcement. [SQLite DDL generator](../../../../src/DataLinq.SQLite/SqlFromSQLiteFactory.cs) · [Validation capabilities](../../../../src/DataLinq.SharedCore/Validation/SchemaValidationCapabilities.cs)

## Seeding single and related rows

```csharp
using DataLinq.Testing;

await using var fixture = await SqliteTestDatabase<EmployeesDb>.CreateAsync(
    cancellationToken: token);

var department = await fixture.SeedAsync((s, ct) =>
    s.InsertAsync(new MutableDepartment("d007", "Research"), ct),
    cancellationToken: token);

var (ada, salary) = await fixture.SeedAsync(async (s, ct) =>
{
    var employee = await s.InsertAsync(NewAda(), ct);
    var salaryRow = await s.InsertAsync(new MutableSalaries
    {
        emp_no = employee.emp_no!.Value,
        FromDate = new DateOnly(2026, 1, 1),
        ToDate = new DateOnly(2026, 12, 31),
        salary = 75_000
    }, ct);
    return (employee, salaryRow);
}, cancellationToken: token);
```

Here `NewAda()` is a test-local factory for a fresh `MutableEmployee` with its required scalar fields set and its generated key omitted. The salary seed uses the key returned by the employee insert. Both rows become available to the test after the callback transaction commits and cleans up. Two separate calls to `SeedAsync` own two separate transactions.

Use test-local model factories for shared values, fixed dates and explicit IDs. Production mutables, converters, provider defaults and ordinary inserts handle the rest. Tests can return a tuple of related rows and use their keys in later committed reads.

The planned production relation API queues `employee.salaries.Insert(child)` or binds `child.employees.Set(employee)` on a mutable. The root insert or save orders dependent writes, hydrates generated keys and returns the immutable root. The November 1 fixture graph had parent-first declaration order and rejected every conflicting explicit FK. The production draft orders dependencies and allows `Set` to supersede an earlier scalar assignment while rejecting a later conflict. W6 uses today's explicit inserts and returned keys as above. When the runtime gains relation-aware mutation, tests can use it inside the same seed callback. The production design owns ordering and FK conflict rules. [Relation-aware mutation draft](../../query-and-runtime/Relation-Aware%20Mutation%20API.md) · [Release roadmap](../../../Roadmap.md)

## Testing a handler and its failures

For a directly constructed handler, pass `fixture.UnitOfWorkFactory`. For a hosted test, configure the application's services, call the planned `services.ReplaceDataLinqWithSqliteInMemory<ShopDb>(fixture)` during test setup, then build the host and resolve the handler normally. In both cases the handler receives the usual unit-of-work contract. Its queries and writes execute on the fixture's real SQLite database.

For example, suppose a handler marks an order submitted and inserts an outbox message with the request ID inside one transaction. Seed a draft order and an existing message with that unique request ID. Invoke the handler and assert its documented duplicate-request outcome. After completion, verify that the stored order is still draft and the original message is the only one for that ID. Use a direct SQLite read on the fixture's store or call `fixture.Database.Provider.State.ClearCache()` and requery the order and messages. Another `fixture.Query()` shares the provider's cache and could return the seeded draft even if the update was committed. When also testing cache freshness, assert the ordinary read first, then verify persisted state independently. The successful case uses a fresh request ID and checks both committed changes the same way. [Database query root](../../../../src/DataLinq/Database.cs) · [Constraint rollback test](../../../../src/DataLinq.Tests.Compliance/Transactions/EmployeesTransactionFailureTests.cs)

A separate test can supply a spy for an application dependency such as a receipt sender and assert whether it was called. Code accepting a stand-alone collection can use the existing core double. W6 requires no new fake factory, call recorder, reference double or fault injector. Detailed provider failures such as lost commit confirmation belong in the existing controlled provider tests. [Collection double](../../../../src/DataLinq/Instances/ImmutableRelation.cs) · [Provider completion tests](../../../../src/DataLinq.Tests.Compliance/Transactions/EmployeesTransactionCommitOutcomeTests.cs)

## DI replacement and W5 startup

The replacement helper selects one registration for the requested database model during test setup, before the service provider is built.

| Service key supplied by the test | Target | Failure during the replacement call |
|---|---|---|
| Omitted | The model's default registration, either unkeyed or a keyed registration marked with `AsDefault()` | If the model has no default, fail and identify the model as missing a default. A lone keyed registration still requires its key unless marked as default. |
| Explicit key | The registration for that model and key | If the key is missing for that model, fail and identify the model and requested key, even when the model has a default. |

The selected registration's generated services, query root, unit-of-work factory and any default alias must all resolve to the fixture. Other registrations retain their original bindings. Generated services capture the canonical registration, so changing only an interface descriptor would leave a path back to the original database. [Registration](../../../../src/DataLinq/Extensions/DependencyInjection/DataLinqRegistration.cs) · [Generated service routing](../../../../src/DataLinq/Extensions/DependencyInjection/DataLinqServiceRegistration.cs)

Preserve compatible concrete aliases. When replacing a keyed `UseSQLite()` registration, resolving `SQLiteDatabase<TDatabase>` with that key must return the exact `fixture.Database` instance. If the registration is also the default via `AsDefault()`, ordinary unkeyed resolution must return that same instance. When displacing an incompatible concrete type such as `MySqlDatabase<TDatabase>`, remove its aliases for the selected registration so required resolution of that type fails for its key and any default alias it owned. [Concrete SQLite alias tests](../../../../src/DataLinq.Tests.Unit/Core/DataLinqConfigurationTests.cs)

The supplied fixture is borrowed. Disposing a host or scope must leave the fixture usable until the test disposes it. Preserve service lifetimes and existing W4 ownership rules. Rebind W5's attached and keyed selections. Enabled startup validation inspects the fixture's actual initialized schema. Disabled validation performs no validation I/O or activation of otherwise unused targets. Explicit creation of the fixture has already provisioned its schema.

## Source audit

Sources inspected on checkout `c0abafa742c73d6274d970408b44a14528bbaacc`. These references identify existing behavior that implementation can reuse.

| Area | Current evidence | Implication for W6 |
|---|---|---|
|---|---|---|
| Existing SQLite path | [SQLiteProvider](../../../../src/DataLinq.SQLite/SQLiteProvider.cs) normalizes anonymous memory stores to unique names and acquires a keeper lease. [SQLiteConnectionStringFactory](../../../../src/DataLinq.SQLite/SQLiteConnectionStringFactory.cs) normalizes named shared-memory identity and reference-counts keeper ownership. | Reuse real provider ownership. Prove fixture isolation, effective connection identity, and cleanup across transaction/read connections; avoid a second keeper registry. |
| Existing fixture evidence | [SQLiteInMemoryBehaviorTests](../../../../src/DataLinq.Tests.Compliance/Query/SQLiteInMemoryBehaviorTests.cs) exercise isolation and named schema observation. [TemporaryModelTestDatabase](../../../../src/DataLinq.Testing/Lifecycle/TemporaryModelTestDatabase.cs) demonstrates metadata-driven schema setup. | Reuse runtime/provider APIs and learn from internal fixtures, without copying their Podman/environment dependencies into the public package. |
| DI replacement | [DataLinqRegistration](../../../../src/DataLinq/Extensions/DependencyInjection/DataLinqRegistration.cs) manages an activation and its aliases. [Generated application services](../../../../src/DataLinq/Extensions/DependencyInjection/DataLinqServiceRegistration.cs) capture the registration's factory. | Replace the target recipe and factory capture together. Replacing only an interface descriptor cannot guarantee generated services use the fixture. |
| DI ownership | [Ownership proofs](../../../../src/DataLinq.Tests.Unit/DependencyInjectionOwnershipProofTests.cs) and [service registration tests](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.ServiceRegistration.cs) cover real container capture and cleanup. | Preserve borrowed fixtures, default/keyed identity, disposal restrictions, and singleton/scoped/transient behavior. |
| W5 validation | [Startup selections](../../../../src/DataLinq/Extensions/Hosting/SchemaValidationRegistration.cs) may attach to registration objects. [The hosted validator](../../../../src/DataLinq/Extensions/Hosting/SchemaValidationHostedService.cs) uses the selected real provider. | Rebind selections to the fixture target, create schema before host startup, and perform real validation if enabled. Preserve disabled behavior without hidden provider activation. |
| Tooling | [Publish inventory](../../../../publish-nuget.ps1), [package consumers](../../../../src/DataLinq.DevTools/PackageConsumerSmokeRunner.cs), [inspection policy](../../../../src/DataLinq.DevTools/PackageInspectionPolicy.cs), and [DocFX exclusions](../../../../docfx.json) contain explicit rules. | Register the new package and its native dependencies deliberately; include its API docs despite the broad `*Test*` project exclusion. |

## Implementation sequence and gates

1. **Package identity.** Rename the internal library, run its direct consumers, and wire the new public package into the tooling.
2. **Fixture and seed.** Cover parallel isolation, keeper cleanup, FK enforcement on read/write connections, setup failure, cancellation, generated IDs, converted/composite keys, real navigation and result timing. Verify that failure of a dependent insert rolls back the principal in the same seed call.
3. **Application workflows.** Use the normal factory for insert, update, delete, read-your-writes, visibility after commit, rollback after a constraint failure, and disposal without commit. Check cache freshness before clearing the cache. Verify persisted outcomes with direct SQLite reads or a cache clear followed by a new query.
4. **DI and startup.** Exercise direct injection and host replacement, one keyed target with a default alias, a generated service, unrelated targets, borrowed fixture ownership, and enabled/disabled W5 selection.
5. **Consumer handoff.** Use active TUnit projects for repository verification and framework-independent packed consumers for .NET 8/9/10. Document SQLite's schema, SQL and native runtime limits. Keep MySQL/MariaDB and existing Memory regression evidence in their provider lanes. W8/W9 own performance and final release acceptance.
