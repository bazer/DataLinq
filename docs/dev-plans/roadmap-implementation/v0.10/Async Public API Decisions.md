> [!WARNING]
> This is an API design record for a future release. Async examples below are proposed consumer code, not APIs shipped in DataLinq 0.9.

# 0.10 Async Public API Decisions

**Status:** Accepted for the decisions explicitly marked accepted below. Open questions and recommendations are not approved contracts.

**Target release:** 0.10 / A10 ([issue #107](https://github.com/bazer/DataLinq/issues/107)).

**Last reviewed:** 2026-09-04.

**Prerequisites:** The [W0 baseline and I/O audit](Implementation%20Order%20and%20Integration%20Plan.md#w0-baseline-and-io-inventory) and W1/W2 provider feasibility evidence remain required before implementation changes shared execution and freezes the complete public surface.

**Authority:** The [release roadmap](README.md) owns scope. This record owns the accepted public async API decisions and remaining questions under D10-1/D10-2. The [implementation order](Implementation%20Order%20and%20Integration%20Plan.md) owns sequencing; the [release evidence plan](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md) owns verification.

## Purpose And Decision Boundary

Record the API decisions agreed on 2026-08-30, 2026-08-31, and 2026-09-04 without pretending that every signature or failure case is settled. Public design discussion can precede implementation; it does not replace the before-state evidence or provider feasibility gate.

The async direction is additive: ordinary application code chooses synchronous or asynchronous execution at each operation. It does not choose a separate async database, transaction, or entity model. AAPI-11 separately approves a breaking correction to synchronous relation enumeration, and AAPI-16 approves enforcing required-reference nullability in both sync and async navigation. These are specific 0.10 compatibility corrections, not permission for unrelated API breaks.

## Accepted Decisions

### AAPI-1: Transaction Creation Remains Synchronous And Lazy

Keep the existing factory shape:

```csharp
Transaction<TDatabase> Transaction(
    TransactionType transactionType = TransactionType.ReadAndWrite);
```

Do not add `TransactionAsync()` merely to obtain a transaction object.

This preserves current behavior. `Database<T>.Transaction()` constructs a managed transaction; the SQLite and shared MySQL/MariaDB provider transaction constructors store configuration without opening a connection. Their connection access paths open the connection and begin the provider transaction only when database access is first required.

Current-code references, checked at `2a58a19a9ef2ec38cfbc1d303bd3c70c63515869`:

- [Database factory](../../../../src/DataLinq/Database.cs)
- [Managed transaction construction](../../../../src/DataLinq/Mutation/Transaction.cs)
- [SQLite provider transaction](../../../../src/DataLinq.SQLite/SQLiteDatabaseTransaction.cs)
- [MySQL/MariaDB provider transaction](../../../../src/DataLinq.MySql/Shared/SqlDatabaseTransaction.cs)

Required behavior:

- Creating a transaction performs no database I/O and therefore needs no cancellation token.
- The first operation requiring database access initializes the connection and provider transaction through that operation's synchronous or asynchronous execution path.
- The token supplied to an async operation also covers required connection opening and transaction initialization, subject to documented provider limitations.
- Subsequent operations may mix sync and async sequentially on the same transaction object. Initialization does not lock the transaction into one execution mode.
- Creating and disposing an unused transaction performs no database I/O. Completing an unused transaction must not open a connection merely to commit or roll back nothing; existing lifecycle rules still apply.
- Async initialization must not route through the existing synchronous connection getter.

This decision removes an unnecessary public async factory; native asynchronous connection opening and transaction begin remain required internally where the provider supports them.

### AAPI-2: Disposal Is Independent Of Construction

Transactions support both synchronous disposal and `IAsyncDisposable` in the target design:

```csharp
// Synchronous construction, asynchronous cleanup.
await using var transaction = db.Transaction();

var employee = await transaction.Query().Employees
    .SingleAsync(e => e.emp_no == employeeNumber);

var mutable = employee.Mutate();
mutable.birth_date = newBirthDate;

await transaction.UpdateAsync(mutable);
await transaction.CommitAsync();
```

`await using` selects `DisposeAsync()` at scope exit; it does not await construction. Ordinary `using` continues to select synchronous disposal. The choice matters when an exception or early return leaves an active transaction needing rollback and resource cleanup, even if the normal path calls `CommitAsync()`.

`DisposeAsync()` follows the standard parameterless `ValueTask` signature. AAPI-25 settles cleanup policy and failure precedence, including the limitation of preserving an arbitrary application exception during scope-exit disposal.

### AAPI-3: Async Is Chosen Per Execution Operation

Keep local operations synchronous: existing database/transaction `Query()` factories, query composition (`Where`, `Select`, ordering and paging), query preparation, `Mutate()`, and mutable property assignment do not receive async variants merely because they participate in an async workflow.

Provide explicit async counterparts for the supported execution families. Preserve existing sync behavior, query support limits, conversions, cache and invalidation rules, logging, metrics, and transaction terminal semantics. Synchronous APIs remain direct synchronous implementations.

Do not run simultaneous managed operations on the same transaction. Sequential mixing is supported. AAPI-20 requires rejection of another execution operation while a reader remains active, including between moves; exact diagnostics and the wider operation-gate contract remain under OAPI-6.

Database-level mutation helpers continue to own the implicit transaction and its completion. Transaction-level mutation helpers execute inside their existing transaction without committing it. `SaveAsync()` corresponds to DataLinq's current `Save()` behavior, not an EF-style change-tracker flush.

### AAPI-4: Cancellation Tokens Are Always Optional On Public Async Operations

When a public async operation accepts cancellation, use the last parameter:

```csharp
CancellationToken cancellationToken = default
```

Callers may omit it everywhere. Other required operation arguments, such as a model or key, remain required. Local factory and composition operations need no token; `DisposeAsync()` remains parameterless.

```csharp
var rows = await query.ToListAsync();
var cancelableRows = await query.ToListAsync(ct);

await transaction.CommitAsync();
// On a different active transaction, cancellation can be supplied:
await anotherTransaction.CommitAsync(ct);
```

Omitting a token means no caller-requested cancellation; it does not disable command timeouts. Do not store one mandatory ambient token on the transaction or require applications to supply `CancellationToken.None` as boilerplate. This public ergonomics decision does not prevent internal contracts from requiring an explicitly propagated token.

### AAPI-5: Singular Relation Methods Use The Property Name Plus Async

**Refined:** 2026-08-30. Generated async methods apply to single-reference navigation. Collection navigation retains its synchronous handle and uses execution methods under AAPI-9; the earlier generated `SalariesAsync()` proposal is superseded.

Generate an explicit method beside each supported single-reference navigation property:

```csharp
var department = await employee.DepartmentAsync();

var cancelableDepartment = await employee.DepartmentAsync(ct);
```

Use `<PropertyName>Async`, preserving the declared property name, without a `Load` or `Get` prefix. Do not make entities awaitable or replace navigation properties with `Task<T>` properties.

The generated method resolves through the internal relation metadata and loading services. It must not first evaluate the synchronous navigation getter. Current generated singular getters access an `IImmutableForeignKey<T>.Value`, which may synchronously load a row; wrapping that getter in an async method would not meet this contract. See [generator output construction](../../../../src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs) and [reference loading](../../../../src/DataLinq/Instances/ImmutableForeignKey.cs).

Retain the existing synchronous navigation properties for compatibility, including their current lazy-loading behavior. The async surface introduces an explicit way to perform that work. The roadmap's prohibition on hidden property I/O means no new hidden I/O mechanism and no synchronous getter evaluation inside async loading; it does not remove existing navigation behavior.

Async loading must preserve the relation's metadata, nullability contract, cache semantics, and source/transaction ownership. AAPI-15 settles generated method placement and collision handling; AAPI-16 settles optional versus required reference behavior. Exact diagnostic identifiers and exception types remain part of the signature/compatibility review. A warmed relation is not a permanent guarantee against future I/O: invalidation can make a later synchronous property access load again.

### AAPI-6: Use Familiar Async Query Execution Names

The accepted naming direction is the synchronous execution name plus `Async`, including `ToListAsync()` and `ToArrayAsync()`. Query composition remains ordinary LINQ.

| Surface | Intended names | Remaining work |
| --- | --- | --- |
| Buffered query results | `ToListAsync`, `ToArrayAsync` | Complete receiver/overload inventory; awaitable types are settled in AAPI-8 |
| Supported row terminals | `FirstAsync`, `FirstOrDefaultAsync`, `SingleAsync`, `SingleOrDefaultAsync`, `LastAsync`, `LastOrDefaultAsync` | Match current predicate, projection, empty-result, and cardinality contracts |
| Supported scalar reductions | `AnyAsync`, `CountAsync`, `SumAsync`, `MinAsync`, `MaxAsync`, `AverageAsync` | Exact selector, numeric, and nullable overloads |
| Key lookup | `GetAsync` on the database/transaction, relation handles, and generated model helpers | Typed/composite keys, source receivers, and nullability; `ValueTask<T>` family settled in AAPI-8 |
| Prepared queries | `ExecuteAsync`; preparation remains synchronous | AAPI-8/AAPI-17 settle scalar versus sequence return shapes; AAPI-18 settles capture timing; complete receiver/overload audit remains |
| Mutations | `InsertAsync`, `UpdateAsync`, `SaveAsync`, `DeleteAsync` | AAPI-27 through AAPI-30 settle input capture/lifetime, local edits, and multiple inputs; exact overload inventory remains |
| Completion | `CommitAsync`, `RollbackAsync`, `DisposeAsync` | AAPI-21 through AAPI-26 settle failure policy; AAPI-31 through AAPI-33 settle callback families/ownership/results; exact signatures and provider evidence remain |
| Async sequences | `AsAsyncEnumerable` | Accepted under AAPI-17 through AAPI-20; no universal streaming guarantee; exact query extension inventory remains |
| Provider metadata and lower-level SQL | Async counterparts for audited I/O operations | Explicit inclusion/exclusion list before W3 |

This table is an inventory starting point, not a declaration that all overloads or backend query shapes are supported. Async does not add `All`, `LongCount`, arbitrary joins, new mutation semantics, or unrestricted client-side fallback to the supported query language.

### AAPI-7: Be Explicit About Provider Limits

Use native asynchronous provider operations wherever they exist. Do not implement them with `Task.Run`, `.Result`, or `.GetAwaiter().GetResult()`.

The common awaitable API is not a guarantee that every provider performs nonblocking I/O. Microsoft.Data.Sqlite executes its async ADO.NET methods synchronously; the SQLite implementation and cancellation documentation must state that limitation. It is the explicit provider exception already allowed by A10, not permission to wrap genuinely asynchronous providers in synchronous work. AAPI-25's recovery budget does not create a hard interruption guarantee. Memory completion and unsupported backends require an explicit decision under OAPI-9.

### AAPI-8: ValueTask For Query And Relation Results, Key Lookup, And Disposal; Task Otherwise

**Accepted and revised:** 2026-08-30. Resolves OAPI-1. The final revision aligns query and collection terminals with the standard .NET async LINQ return types. It supersedes the earlier same-day decision to use `Task` for ordinary query/relation terminals; that earlier choice is no longer the contract.

Use the following public awaitable types consistently across providers, overloads, and cache-hit/cache-miss paths:

| Public operation family | Accepted return type |
| --- | --- |
| `GetAsync` on database/transaction access, relation handles, and generated key lookup helpers | `ValueTask<T>` with the existing lookup result's nullability, normally `ValueTask<T?>` for a nullable entity lookup |
| Generated direct single-reference loading, such as `DepartmentAsync` | `ValueTask<TResult>`; preserve declared relation nullability and enforce the required-reference behavior in AAPI-16 |
| `DisposeAsync` | Parameterless `ValueTask`, following `IAsyncDisposable` |
| `ToListAsync`, `ToArrayAsync`, row terminals, and scalar reductions on both queries and collection relations | `ValueTask<TResult>`, including `ValueTask<List<T>>`, `ValueTask<T[]>`, nullable `ValueTask<T?>` for nullable row results, `ValueTask<bool>` for `AnyAsync`, and `ValueTask<int>` for `CountAsync` |
| Relation `ValuesAsync`, `KeysAsync`, `ContainsKeyAsync`, and `ToFrozenDictionaryAsync` | `ValueTask<TResult>` with the collection result types specified in AAPI-12 |
| Prepared scalar/row execution | `Task<TResult>`, even when a particular execution can use cached data |
| `InsertAsync`, `UpdateAsync`, `SaveAsync`, and other result-returning mutation counterparts | `Task<TResult>` |
| `DeleteAsync`, `CommitAsync`, `RollbackAsync`, and other no-result execution counterparts | `Task` |
| Result-returning transaction callback helpers | `Task<TResult>` |
| Async transaction callback delegates | `Func<..., Task>` or `Func<..., Task<TResult>>`; transaction-only and transaction-plus-token families under AAPI-31 |
| Other public awaitable operations, including metadata and lower-level SQL execution | `Task` or `Task<TResult>` according to the synchronous result contract |

The rule selects public awaitables. AAPI-17 separately settles sequence results: `AsAsyncEnumerable()` and prepared-sequence `ExecuteAsync(...)` return `IAsyncEnumerable<T>` directly, without a task wrapper. Its enumeration/disposal protocol retains the standard `ValueTask<bool>`/`ValueTask` members.

Rationale: standard `System.Linq.AsyncEnumerable` terminals use `ValueTask<TResult>`. DataLinq adopts that family for its LINQ terminals on both `IQueryable<T>` and relation handles, so changing the execution surface does not change the public awaitable family. This deliberately follows framework async LINQ rather than EF Core's task-returning query extensions. DataLinq-specific collection accessors use `ValueTask` for the same consistency, even though they have no exact framework counterparts. Key and single-reference lookups also have paths that return cached rows without database I/O, which `ValueTask<T>` can carry without allocating an operation-specific task. Prepared scalar/row execution, mutations, transaction completion/callbacks, metadata, and other non-LINQ awaitable operations retain `Task` composition and reusable task semantics. Optional cancellation tokens and synchronous lazy transaction creation remain unchanged.

Consumption and implementation requirements:

- Treat each returned `ValueTask` or `ValueTask<T>` as a single-consumption awaitable. Calling a method again creates a new awaitable and remains valid even when both calls return the same cached entity.
- Callers needing to share or repeatedly await an operation convert it to a `Task` or `Task<T>` with `.AsTask()` once and retain that task. Do not promise allocation savings when callers convert every operation.
- Cache-miss execution remains genuinely asynchronous where the provider supports it; `ValueTask<T>` is not a synchronous-only API.
- The initial implementation may use directly completed values and task-backed slow paths. This decision does not introduce custom `IValueTaskSource<T>` pooling.
- Internal return types remain implementation choices, informed by measured costs. A public `ValueTask<T>` may still wrap a task on the asynchronous path; the signature alone does not prove an allocation improvement.
- Do not change the public awaitable type according to the provider, cache state, or a particular query optimization.

Benchmark cached non-null results, misses, mixed workloads, cancellation/failure, and `.AsTask()` consumption through the real call chain. These measurements validate the chosen implementation and identify regressions; they are not a condition for accepting this API decision. No measured performance improvement is claimed by this record. A material problem requires an explicit design revision rather than silently changing public return types.

### AAPI-9: Collection Relation Handles Stay Synchronous; Execution Gets Async Counterparts

**Accepted:** 2026-08-30. Refines AAPI-5/AAPI-8 and settles the collection execution boundary within OAPI-2.

Obtaining a collection relation remains synchronous and performs no database I/O. Preserve `IImmutableRelation<T>` and its `IEnumerable<T>` row surface, with the explicit naming correction in AAPI-11. Do not add queryable-interface inheritance or a relation query-composition entry point in 0.10. Place async execution on the relation operations rather than generating `SalariesAsync()`:

```csharp
var salaries = employee.Salaries;

var first = await salaries.FirstOrDefaultAsync(ct);
var single = await salaries.SingleOrDefaultAsync(ct);
var any = await salaries.AnyAsync(ct);
var count = await salaries.CountAsync(ct);
var salary = await salaries.GetAsync(salaryKey, ct);
var list = await salaries.ToListAsync(ct);
var array = await salaries.ToArrayAsync(ct);
```

All shown cancellation tokens are optional. Include supported `First`, `Single`, `Last`, and `OrDefault` counterparts in the complete overload audit. Existing synchronous operations remain available on the same relation object. Synchronous local work, such as clearing the relation's local cached state, needs no async counterpart.

Contract:

- A terminal promises its result and cardinality, not that it loads or primes the entire relation. Preserve ordering, source/transaction visibility, row identity, and cache/invalidation correctness.
- `GetAsync(key)` is scoped to membership in this relation, not only the existence of a row with that primary key.
- `SingleAsync`/`SingleOrDefaultAsync` must detect multiple matches. A partial fetch must never publish the relation as completely loaded.
- `ToListAsync` and `ToArrayAsync` explicitly materialize all matching rows into their named result types and dispose owned readers before successful completion. Enumerating that returned collection performs no database I/O; accessing a different lazy navigation on an element can still do so.
- Reuse native asynchronous loading services. An extension or default interface method that calls a synchronous terminal and wraps the result in a completed task does not meet the async contract.
- Per-operation optimizations such as limited row fetches, existence queries, or counts remain implementation opportunities, not additional 0.10 scope or guaranteed query plans.

Current [collection relation code](../../../../src/DataLinq/Instances/ImmutableRelation.cs) materializes complete relation values for these operations. This describes the existing implementation, not a restriction imposed on future implementations by the accepted API.

### AAPI-10: Relation Query Composition Deferred Beyond 0.10

**Superseded scope decision:** 2026-08-30. The earlier acceptance of relation `Query()` for 0.10 is withdrawn. Its proposal, potential, unresolved constraints, and future evidence now live in the unscheduled [Relation-Scoped Queries](../../query-and-runtime/Relation-Scoped%20Queries.md) backlog document. No relation query API, parser work, testing capability, or release gate is required by 0.10. This identifier is retained only to make the scope revision traceable; existing database/transaction query roots are unaffected.

### AAPI-11: AsEnumerable Enumerates Rows; AsKeyValuePairs Names Keyed Enumeration

**Accepted breaking change:** 2026-08-30, for 0.10. The synchronous rename is implemented separately in [PR #111](https://github.com/bazer/DataLinq/pull/111), independently of this planning record and the remaining async runtime work; it is not part of the shipped 0.9 API.

Remove the pair-returning `AsEnumerable()` instance member from `IImmutableRelation<T>`, `ImmutableRelation<T, TKey>` (including its inherited one-parameter form), and `ImmutableRelationMock<T>`. Retain keyed enumeration under the explicit name:

```csharp
IEnumerable<KeyValuePair<DataLinqKey, T>> AsKeyValuePairs();
```

Do not add a replacement instance `AsEnumerable()` or an obsolete forwarding alias. With `using System.Linq`, the ordinary framework extension now handles the row view:

```csharp
IEnumerable<Salary> rows = relation.AsEnumerable();
IEnumerable<KeyValuePair<DataLinqKey, Salary>> keyedRows = relation.AsKeyValuePairs();
```

The framework `AsEnumerable()` returns the same relation as `IEnumerable<T>` without loading, copying, or creating a snapshot. Enumerating it follows the existing synchronous relation-loading path. `AsKeyValuePairs()` retains the old keyed operation: keys are the related rows' primary keys, including composite keys, not the parent relation's foreign key. Calling it may synchronously load rows and construct the frozen dictionary; it does not acquire a deferred-I/O guarantee from the rename. It promises no new ordering. `Values`, `Keys`, the indexer, `Get`, and `ToFrozenDictionary` keep their existing contracts. The existing mock's unimplemented behavior is not repaired by this rename; T10 owns that work.

Migration and compatibility requirements:

- Change old pair-consuming `relation.AsEnumerable()` calls to `relation.AsKeyValuePairs()`. Row consumers can now use standard `AsEnumerable()` or enumerate the relation directly.
- Recompile consumers and update custom interface implementations, explicit implementations, mocks, reflection/member references, and generated consumer artifacts that name the old member. This is an intentional source and binary break, not an additive alias.
- Some old calls using inferred types can still compile after recompilation but now enumerate rows. Successful compilation alone is not a complete migration check; audit every relation `AsEnumerable()` call for its intended element type and loading timing.
- Record the exact break in the 0.10 compatibility review and release migration notes. Do not hide it by rewriting the 0.9 baseline or broadly suppressing unrelated ApiCompat diagnostics.
- Consumer-shaped tests must exercise both interface-typed and concrete relations, standard row enumeration, deferred row-view construction, and retained keyed lookup/identity behavior, including empty and composite-key relations.

**Owner/gate:** A10, D10-1 with T10 compatibility follow-through; the narrow synchronous correction may land before async execution changes. W0/W1/W2 still gate shared async execution work.

### AAPI-12: Explicit Async Row Enumeration And Collection Accessors

**Accepted direction:** 2026-08-30. Keep the synchronous relation handle and add an explicit `AsAsyncEnumerable(CancellationToken cancellationToken = default)` row view returning `IAsyncEnumerable<T>`. Do not add `IAsyncEnumerable<T>` as another base interface on the relation, which can make existing LINQ extension calls ambiguous. AAPI-14 settles member placement; AAPI-17 through AAPI-20 settle enumeration, token, and lifetime contracts.

Use standard async LINQ after selecting that view:

```csharp
var rows = await relation.AsAsyncEnumerable()
    .Where(row => MatchesLocally(row))
    .ToListAsync(ct);
```

Collection predicate terminals use local `Func<T, bool>` delegates. Provider expression predicates remain available through existing database/transaction query roots; 0.10 adds no automatic conversion from a relation to such a query. Select the async view before applying local async LINQ operators. `relation.Where(...)` already produces a synchronous `IEnumerable<T>` pipeline, and DataLinq must not add a generic wrapper that secretly enumerates it synchronously or recover relations from framework iterator internals.

The accepted collection accessor names and result types are:

| Relation operation | Return type |
| --- | --- |
| `ValuesAsync(CancellationToken cancellationToken = default)` | `ValueTask<ImmutableArray<T>>` |
| `KeysAsync(CancellationToken cancellationToken = default)` | `ValueTask<ImmutableArray<DataLinqKey>>` |
| `ContainsKeyAsync(DataLinqKey key, CancellationToken cancellationToken = default)` | `ValueTask<bool>` |
| `ToFrozenDictionaryAsync(CancellationToken cancellationToken = default)` | `ValueTask<FrozenDictionary<DataLinqKey, T>>` |

These preserve the synchronous result shapes and relation membership semantics. They are DataLinq collection APIs, not claims that identically named framework operators exist. Keyed async materialization is available through `ToFrozenDictionaryAsync`; this decision does not add an `AsKeyValuePairsAsync` member. Local cache clearing remains synchronous.

Start from asynchronous loading into a completed relation snapshot where that preserves current behavior. Use the returned snapshot directly after awaiting, not a synchronous getter that could load again after invalidation. An `IAsyncEnumerable<T>` return type does not promise database streaming: local `Take(10)` may follow a complete relation load. Callers needing provider filtering/paging can use an ordinary database/transaction query with an explicitly written relation predicate. AAPI-17 through AAPI-20 define execution start, buffering, capture/re-enumeration, token combination, and reader lifetime. AAPI-21 through AAPI-26 define cancellation/failure policies; OAPI-6 retains load coordination and atomic publication details.

### AAPI-13: Standard Async LINQ With A Conditional Transitive Dependency

**Accepted:** 2026-08-30. Use framework async LINQ on .NET 10 and a normal transitive `System.Linq.AsyncEnumerable` package dependency on .NET 8 and .NET 9. Do not build a parallel DataLinq local async operator library or require consumers to discover/install the package themselves.

At async-surface implementation, add this reference to `src/DataLinq/DataLinq.csproj`, with the selected compatible version pinned centrally in `src/Directory.Packages.props`:

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net8.0'
                   Or '$(TargetFramework)' == 'net9.0'">
  <PackageReference Include="System.Linq.AsyncEnumerable" />
</ItemGroup>
```

Do not use `PrivateAssets="all"`; consumers need the dependency's compile/runtime assets. NuGet pack must emit the dependency for DataLinq's .NET 8/9 groups and omit it from DataLinq's .NET 10 group. Another dependency may still bring the package into a .NET 10 application; this condition only controls DataLinq's contribution. Avoid the older, overlapping `System.Linq.Async` package in the supported consumer setup and document potential extension-method conflicts.

This records the dependency policy, not an installed package or selected version. Verify the packed package dependency groups and compile/run ordinary async-LINQ consumers targeting .NET 8, 9, and 10, using both token-free and token-supplied calls. Project-reference builds alone do not prove transitive NuGet behavior. No package publication is authorized by this decision.

### AAPI-14: Relation Execution Members With Overridable Async Defaults

**Accepted:** 2026-08-30. Put relation async execution on `IImmutableRelation<T>`: the explicit async row view, scoped key lookup, supported row terminals/reductions, and collection materializers/accessors belong to the relation contract. Supply overridable default interface implementations where a shared implementation can preserve the operation's semantics.

Defaults may compose standard async LINQ over a genuine async row source. They must not evaluate `Values`, a synchronous getter, or a synchronous terminal that can perform database I/O and then wrap the result in a completed awaitable. Implementations can override individual operations for cached lookup, cardinality checks, or future limited execution without being forced to load the full relation. This does not promise those optimizations in 0.10.

Standard local async composition remains framework extensions after `AsAsyncEnumerable()`. Async terminals on existing DataLinq `IQueryable<T>` roots remain DataLinq query extensions. Do not add a catch-all async extension over arbitrary `IEnumerable<T>`.

Implementation and compatibility requirements:

- Define the small set of required execution primitives and the defaults derived from them in the complete signature inventory; custom implementations and test doubles should not have to reproduce every terminal algorithm.
- Preserve dispatch to an implementation's overrides. Default interface members are available through interface receivers, not automatically through a concrete class receiver; deliberately expose the intended surface on built-in concrete relations and public testing helpers as well.
- Native provider loading must remain asynchronous where supported. An entirely in-memory implementation may complete immediately; an implementation without async execution capability must fail explicitly rather than quietly performing synchronous database work.
- Review new interface requirements against external implementations and binary consumers. Default bodies reduce repeated implementation work but are not a blanket compatibility guarantee.
- Test local overload binding, interface/concrete receivers, default dispatch and overrides, and the absence of synchronous I/O in cold-cache async execution. Existing translated relation predicates retain their current synchronous LINQ shapes.

**Owner/gate:** A10 with T10; exact primitive/overload inventory and custom-implementation migration before W3. Relation query composition is not part of this contract.

### AAPI-15: Generated Single-Reference Methods On Public Model Bases

**Accepted:** 2026-08-30. Generated `<PropertyName>Async(CancellationToken cancellationToken = default)` instance methods must be callable through the public model base type used by applications, not only the generated immutable implementation. Use an overridable implementation backed by shared relation loading, metadata, cache, and source ownership; never obtain the result by evaluating the synchronous navigation property first.

Do not automatically add these methods to every generated model interface: those interfaces may also be implemented by mutable models. This is not an interface-first model rewrite or approval for separate generated test-shape interfaces. T10 builders/doubles must support the accepted public model navigation behavior without requiring a database.

If the generated name conflicts with a user-defined member or inheritance/overload rules make the intended call ambiguous, emit a focused generator diagnostic. Do not silently rename the accepted API or assume a user-defined method with a matching signature supplies DataLinq's loading contract. Audit inherited members and optional-token calls as well as direct name collisions.

**Owner/gate:** A10 generator/public-surface work with T10 consultation; consumer compilation and exact diagnostic review before W3.

### AAPI-16: Required References Return A Row Or Fail In Both Sync And Async

**Accepted compatibility correction:** 2026-08-30, for 0.10. Required single-reference navigation must enforce its non-nullable public contract at runtime. Apply the same rule to the existing synchronous property and the generated async method.

| Reference contract | No matching target | Exactly one target | Multiple matching targets |
| --- | --- | --- | --- |
| Optional (`T?` / `ValueTask<T?>`) | Return `null` | Return the row | Cardinality failure |
| Required (`T` / `ValueTask<T>`) | Clear relation-resolution failure | Return the row | Cardinality failure |

A required reference with no usable foreign key or a dangling target must not silently return `null`. The generated getter currently suppresses nullable analysis on the underlying nullable value; that suppression is not a runtime check. Preserve duplicate-target detection instead of choosing an arbitrary first row. See [generator output construction](../../../../src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs) and [reference resolution](../../../../src/DataLinq/Instances/ImmutableForeignKey.cs).

This corrects navigation behavior, not general key-lookup semantics: a nullable `Get`/`GetAsync` miss remains nullable. The lower-level reference holder may likewise represent absence, provided required navigation enforces the contract before returning to its caller.

Requirements:

- Enforce the same result/cardinality rule for sync and async navigation, cold and warm loads, and reloads after invalidation. Do not publish `null` as a successful required navigation result.
- Keep optional missing-reference behavior nullable, including absent keys and missing targets; validate scalar/composite and converted-key cases against relation metadata.
- Resolve through shared loading/validation behavior. Nullability enforcement must not add a second load, and async validation must not call a synchronous getter after awaiting.
- Report the affected model/relation clearly. Exact exception types and diagnostic details must be selected during the compatibility review, rather than invented independently by sync and async paths. Cancellation and provider failures keep their own meaning.
- T10 reference/graph helpers must reproduce optional, required-missing, and duplicate-target outcomes. A test double must not make an invalid required graph look valid by returning `null`.
- Document the deliberate sync behavior change in 0.10 migration/release evidence. Existing consumers relying on a broken required reference yielding `null` must correct the data or declare the relationship optional. Do not relax the public annotation to preserve the bug or treat this as permission for unrelated lookup changes.

**Owner/gate:** A10 owns the sync/async runtime and generator correction, with T10 parity and explicit compatibility evidence. Record the plan now; implementation still follows W0/W1/W2 and the agreed public-surface gate.

### AAPI-17: Async Sequences Do Not Promise Database Streaming

**Accepted:** 2026-08-30. Use `IAsyncEnumerable<T>` directly for the explicit relation/query `AsAsyncEnumerable(CancellationToken cancellationToken = default)` view and for prepared-sequence `ExecuteAsync(source, argument, cancellationToken = default)`. Do not wrap either sequence in `Task<IAsyncEnumerable<T>>` or `ValueTask<IAsyncEnumerable<T>>` merely to obtain it. Prepared scalar/row execution retains `Task<TResult>` under AAPI-8.

Consumer shape:

```csharp
var rows = query.AsAsyncEnumerable(ct);

await foreach (var row in rows)
{
    Process(row);
}

await foreach (var row in preparedSequence.ExecuteAsync(db, arguments, ct))
{
    Process(row);
}
```

The sequence contract permits buffering. It does not guarantee a live database reader, constant memory, one database fetch per move, or a provider-side row limit from a local `Take(10)`. The initial relation implementation may asynchronously obtain a complete collection before yielding; future implementations may fetch differently while preserving the accepted result, cardinality, visibility, and cache contracts. Do not require a new streaming/query engine for 0.10.

`ToListAsync()`, `ToArrayAsync()`, and `ValuesAsync()` explicitly return completed materialization. Owned readers are closed before successful completion, and enumerating that returned collection requires no database I/O. Other lazy navigation reached through its rows can still load. Callers requiring a completed collection must use such a materializer rather than rely on the current buffering implementation of an async view.

**Owner/gate:** A10, D10-1; validate return shapes and buffering/materialization boundaries before W3. Exact supported query receivers/overloads remain under OAPI-7.

### AAPI-18: Separate Parameter Capture From Deferred Sequence I/O

**Accepted:** 2026-08-30. Preserve the current ordinary and prepared query argument boundaries. Obtaining an async sequence or its enumerator performs no DataLinq database I/O; sequence execution may begin with the first `MoveNextAsync()`.

| Operation | Capture bound query arguments | Start database I/O |
| --- | --- | --- |
| Ordinary query `AsAsyncEnumerable()` | At `GetAsyncEnumerator()` for each enumeration | First `MoveNextAsync()` |
| Prepared sequence `ExecuteAsync(source, argument, ct)` | At the `ExecuteAsync(...)` call | First `MoveNextAsync()` |
| Materializing query terminal such as `ToListAsync()` | During the method call, before its first suspension | As part of that operation |

Obtaining a relation async view also performs no database I/O; any relation loading occurs during enumeration. Local argument validation and prepared argument capture may happen synchronously. Database execution failures arise when enumeration executes. Calling an awaitable terminal starts that operation; the later `await` is not its start trigger.

An async iterator must not accidentally move prepared binding/snapshotting into its lazy body. Ordinary queries currently parse/bind in the [enumerator/provider path](../../../../src/DataLinq/Linq/Planning/Expressions/ExpressionPlanQueryable.cs). [Prepared execution](../../../../src/DataLinq/Linq/PreparedQuery.cs) binds before returning the lazy sequence, with [before-enumeration snapshot coverage](../../../../src/DataLinq.Tests.Compliance/Translation/PreparedQueryTests.cs).

For a prepared sequence, mutating a supported invocation array/local sequence after `ExecuteAsync(...)` must not change that invocation's bound values. This preserves the existing snapshot contract; it does not introduce arbitrary deep cloning or change standard local delegate/closure semantics. A parameter snapshot is not a database snapshot: row visibility still depends on execution time and transaction isolation.

Sequential repeated enumeration is supported without promising identical results:

- An ordinary query creates another execution and captures the current bound parameter values when its new enumerator is obtained.
- A prepared sequence returned by one `ExecuteAsync(...)` invocation reuses that invocation's captured arguments on each enumeration, but database results can change. Another `ExecuteAsync(...)` call captures a new invocation.
- A relation can reuse a valid cache or reload after invalidation. Reusing its async view does not create permanent result caching or require an extra database read on a valid cache hit.
- Retain a materialized list, array, or immutable values collection when the application needs the same collection again. This fixes that collection, not every navigation reachable from its elements.

**Owner/gate:** A10, D10-1/D10-2; capture tests must distinguish sequence construction, enumerator construction, and first movement, including mutation between those stages and repeated enumeration.

### AAPI-19: Honor Both Method And Enumerator Cancellation Tokens

**Accepted:** 2026-08-30. Optional cancellation on the sequence factory/execution method and standard `.WithCancellation(...)` both apply to enumeration:

```csharp
await foreach (var row in relation.AsAsyncEnumerable(ct))
{
    Process(row);
}

await foreach (var row in relation.AsAsyncEnumerable().WithCancellation(ct))
{
    Process(row);
}
```

If different method and enumerator tokens are supplied, cancellation of either requests cancellation of that enumeration. Do not ignore or overwrite one of them. Follow standard async-iterator `[EnumeratorCancellation]` behavior, including equivalent custom-enumerator implementations and disposal of any owned linked token source. The same/default-token cases must retain the same meaning without requiring unnecessary linking.

Observe cancellation during buffered row iteration as well as asynchronous loading. Buffering does not make a large local enumeration uncancelable. A retained sequence with a method token retains that token's cancellation constraint on later enumerations; passing another enumerator token does not undo it.

This decides token delivery and combination, not all failure semantics. AAPI-21 through AAPI-26 settle execution-entry cancellation, initialization recovery, operation/completion outcomes, cleanup policy, and structured failure reporting. Shared-load coordination and precise provider interruption limits remain under OAPI-6/OAPI-9, with exact public signatures under OAPI-7. No mandatory ambient transaction token or public token on `DisposeAsync()` is introduced.

**Owner/gate:** A10 with T10; verify token-free calls, either token alone, equal/different tokens, buffered iteration, repeat enumeration, and linked-token resource cleanup before W3.

### AAPI-20: Enumeration Owns Its Resources, Not The Caller Transaction

**Accepted:** 2026-08-30. The enumerator owns the execution resources it creates. Completion, failure, cancellation, and early `break` must deterministically dispose owned readers/commands and other owned execution resources. Disposing an unused enumerator must not initialize database access. `await foreach` awaits enumerator disposal; callers obtaining an enumerator manually must dispose it themselves.

Enumerator disposal must not commit or dispose a caller-owned transaction. A live reader remains bound to its execution source and must not migrate to a new source midway through enumeration or continue using a disposed source.

Reject another execution operation on the same transaction while a reader remains active, including between `MoveNextAsync()` calls while application code processes the current row. A pause in row production does not release the reader. Exact diagnostics and wider overlap/disposal coordination remain under OAPI-6; this restriction does not serialize independent database-root operations.

Callers needing nested operations on the transaction should explicitly finish materialization first:

```csharp
var employees = await transaction.Query().Employees.ToListAsync(ct);

foreach (var employee in employees)
{
    var department = await employee.DepartmentAsync(ct);
}
```

Do not make correctness depend on an async view happening to buffer in the current implementation. Materialized collections can be enumerated without the original reader, but subsequent navigation can still need a valid read source.

Preserve the existing validated post-transaction relation rules. [Collection relations](../../../../src/DataLinq/Instances/ImmutableRelation.cs) and [reference relations](../../../../src/DataLinq/Instances/ImmutableForeignKey.cs) can switch to committed reads after certain completed transactions, subject to [terminal trust checks](../../../../src/DataLinq/Mutation/Transaction.cs). Do not impose a blanket rule that every relation becomes unusable after commit. Later relation access may follow that existing transition; an active reader may not change source midway through execution.

**Owner/gate:** A10, D10-1/D10-2 with H10/T10 consultation; verify resource ownership, early/manual disposal, active-reader overlap, and allowed/rejected terminal-source transitions. AAPI-25 settles cleanup failure precedence; the complete operation gate remains open under OAPI-6.

### AAPI-21: Validate First, Then Honor Pre-Cancellation Even On Cache Hits

**Accepted:** 2026-08-31. Resolves the execution-entry portion of OAPI-4; AAPI-22 through AAPI-26 settle the remaining failure-policy decisions.

For an otherwise valid asynchronous execution call, observe an already-canceled token before cache lookup, database initialization, or mutation work. An immediately available result does not bypass cancellation. Ordinary argument and transaction-state validation takes precedence; cancellation must not hide an invalid call or make an unusable transaction appear usable. This precedence does not require database I/O to discover errors before checking cancellation.

| Situation | Accepted behavior |
| --- | --- |
| Cached `GetAsync(key, canceledToken)` or another cached execution result | Report cancellation rather than return the cached result |
| `ToListAsync(canceledToken)` | Report cancellation without execution or a partial successful result |
| `CommitAsync(canceledToken)` on a valid unused transaction | Report cancellation without committing, rolling back, or initializing the transaction; leave it unused |
| Invalid arguments or an unusable transaction | Report the applicable argument/lifecycle error before checking cancellation |
| Cancellation requested after the operation has completed successfully | Preserve success rather than retroactively reporting cancellation |

Rejecting a call solely for pre-execution cancellation does not poison an otherwise usable transaction or change its prior work. The caller may make a later valid call with another token. This is not a promise that a transaction remains usable after cancellation during initialization, provider execution, mutation, or completion.

This rule applies at execution and does not move the sequence construction, parameter capture, or deferred I/O boundaries accepted in AAPI-18. Async enumeration must observe pre-cancellation before executing or yielding cached/buffered rows. Synchronous local factories remain synchronous, and parameterless `DisposeAsync()` does not acquire a caller token.

The late-cancellation rule preserves completed success; it does not by itself settle where cancellation may be observed during a multi-step operation, whether commit has a known outcome, or how subsequent finalization/cleanup failures are reported. Apply AAPI-22 through AAPI-26 for those distinctions.

**Owner/gate:** A10, D10-2; prove warm/cold execution parity, validation precedence, absence of pre-canceled I/O/mutation, unchanged transaction usability, unused-commit behavior, and completed-success preservation in W1/W2 before W3.

### AAPI-22: Interrupted First-Use Initialization Makes The Wrapper Unusable

**Accepted:** 2026-08-31. Once first-use initialization starts, an initialization failure or observed cancellation makes that transaction wrapper unusable for further managed reads, writes, and commit. Clean up owned resources and require a new transaction; disposal remains the managed resource-recovery path. Do not automatically reset the wrapper to unused, reconnect it, retry initialization, or replay the first operation.

| Initialization boundary | Required behavior |
| --- | --- |
| Cancellation observed before initialization starts | Leave the transaction unused and reusable under AAPI-21 |
| Connection opening, required configuration, or provider begin fails/is interrupted | Clean up and reject subsequent business operations through the wrapper |
| Initialization succeeds, then cancellation is observed before the application command starts | Keep the initialized transaction usable; no application statement executed |
| Initialization cleanup also fails | Preserve the initialization failure, expose cleanup failures, and retain the unusable state |

A cancellation request alone does not establish initialization failure. Keep connection/transaction resources private until the whole initialization sequence succeeds, including required provider configuration, then publish usable state. Do not expose a half-initialized connection or transaction as open/usable. This refines the current synchronous providers' early `Open` publication; implementation must verify the shared state transitions without inventing unrelated compatibility changes.

**Owner/gate:** A10, D10-2; W1/W2 fault injection at opening, configuration, begin, publication, and cleanup. Prove no initialization replay and distinguish successful initialization followed by pre-command cancellation from interrupted initialization.

### AAPI-23: Read Recovery Requires Trust; Interrupted Mutations Poison

**Accepted:** 2026-08-31. Apply cancellation checkpoints during fetching/materialization and release owned execution resources before allowing later execution. A buffered terminal must not return partial contents as success; a stream cannot recall rows already delivered to application code.

Never publish an incomplete relation load as complete. Existing complete, valid cached rows need not be discarded solely because later reading was canceled; preserve normal cache/invalidation rules. The complete shared-load/invalidation coordination mechanism remains under OAPI-6.

A canceled ordinary DataLinq read may leave its caller-owned transaction reusable only after cleanup succeeds and provider evidence establishes that the intended transaction remains trustworthy. An open connection alone is insufficient. If integrity cannot be established, reject further reads, writes, and commit, allowing only recovery operations valid for the remaining provider state. Do not reconnect and silently continue the old transaction. Do not assume a raw command is harmless merely because it returns rows; its execution contract belongs to the OAPI-7 inventory.

| Mutation boundary | Required behavior |
| --- | --- |
| Cancellation observed before this operation starts mutation work | Preserve prior transaction work under AAPI-21 |
| Cancellation during a write, with effects possible or uncertain | Fail and poison the transaction |
| Cancellation during required generated-value/authoritative-row loading after a write | Fail and poison because database effects and local model state may disagree |
| Provider work succeeds and short local cache/baseline finalization remains | Complete the consistency-critical local work without cancellation checkpoints midway through it |
| Mutation completes successfully before cancellation is observed | Preserve success |

Poisoning retains the existing managed mutation rule: block reads, writes, and commit, invalidate affected mutable state, and require rollback or disposal. Required post-write database reads remain cancelable; actual local finalization errors still poison. Do not make all remaining I/O uncancelable after the first write.

For a multi-model mutation helper, observed cancellation after that call has performed writes must prevent committing its completed prefix. AAPI-30 settles input enumeration/capture; this decision does not introduce a batching engine.

Transaction-level mutations do not automatically roll back a caller-owned transaction. Database-level helpers own their implicit transaction and perform required rollback/cleanup under AAPI-25, without treating an uncertain commit as a proved rollback.

**Owner/gate:** A10, D10-2 with T10 consultation; W1/W2 cover reusable/broken read sources, reader cleanup failures, partial buffers/streams, partial relation publication, write/hydration cancellation, local finalization, and interrupted multi-model execution.

### AAPI-24: Database Completion Outcome Survives Cancellation And Cleanup Failure

**Accepted:** 2026-08-31. Track database completion separately from overall async method success. Propagate caller cancellation through provider commit/rollback where supported, but never infer rollback or absence of commit solely from cancellation.

| Commit evidence | Required behavior |
| --- | --- |
| Cancellation observed before commit dispatch | Report cancellation without commit/rollback; preserve prior transaction state |
| Commit confirmed, finalization and cleanup succeed | Return success even if the token is now canceled |
| Commit confirmed, local committed-state finalization fails | Preserve `TransactionCommitFinalizationException` and its known-committed meaning |
| Commit confirmed, resource cleanup fails | Report cleanup failure while retaining confirmed committed outcome |
| Commit attempted, outcome cannot be established | Report the original failure with unknown commit outcome; never automatically retry |

After a failed commit attempt, reject further business operations and another commit through that wrapper. Preserve the narrowly permitted rollback/disposal recovery paths. A later rollback attempt cannot establish that an earlier uncertain commit did not happen. After confirmed commit, necessary local finalization and cleanup proceed independently of the request token.

The provider adapter must record confirmed database completion separately from subsequent telemetry/status notification, local finalization, and disposal. Current provider `Commit()` methods combine several of these stages, while the managed wrapper conservatively classifies a failure from the combined call as unknown. Refine that internal boundary so later cleanup cannot erase established completion evidence; do not infer confirmation from a prematurely published status alone.

For rollback, pre-dispatch cancellation preserves the prior state, including an already-poisoned state. Confirmed rollback remains confirmed after cleanup failure. An interrupted rollback without confirmation has unknown rollback outcome. After a failed rollback attempt, allow disposal rather than resumed business operations. Do not manufacture a definite database outcome from connection closure or recovery attempts.

**Owner/gate:** A10, D10-2 with H10/T10 consultation; W1/W2 distinguish dispatch, server confirmation, lost confirmation, local finalization, observer failure, and resource cleanup. Verify terminal restrictions and no automatic replay.

### AAPI-25: Independent Recovery Budget And Explicit Cleanup Failure Precedence

**Accepted:** 2026-08-31. Public `RollbackAsync(ct)` honors its optional caller token. DataLinq-initiated recovery rollback after an implicit mutation/callback failure uses an independent recovery token. `DisposeAsync()` remains parameterless and uses internal cleanup policy where applicable; an already-canceled request token must not prevent necessary recovery from starting.

Use a configurable 30-second starting budget for DataLinq-initiated rollback attempts, subject to W1/W2 provider verification before freezing the configuration surface. This is a cooperative rollback budget, not a guaranteed deadline for all cleanup/disposal. It does not make non-cancelable provider calls interruptible or remove SQLite's synchronous-driver limitation. The exact option name/location and validated provider behavior remain under OAPI-7/OAPI-9.

Do not merely stop awaiting rollback and return its connection to a pool while work continues. Any provider-specific abort/release strategy must prove safe ownership and non-reuse. Continue other safe, independent cleanup steps after one fails; do not skip them because the original caller token was canceled.

When DataLinq owns both operation execution and its cleanup:

- Preserve the primary operation exception, including its type and stack, when rollback/disposal/recovery also fail.
- Expose secondary failures as structured information under AAPI-26; logging alone is insufficient.
- If execution succeeded and cleanup alone failed, report the cleanup failure with any known database outcome preserved.
- Do not rethrow an already-reported cleanup failure during later scope disposal merely because it was recorded earlier. New cleanup failures remain reportable.

Retain throwing disposal. Parameterless `DisposeAsync()` does not receive an arbitrary exception from surrounding application code. Under C# `await using`, a disposal exception can replace an exception already propagating; the same limitation applies when an `await foreach` body fails and enumerator disposal then fails. Document this limitation instead of promising universal exception preservation or making disposal silently nonthrowing. DataLinq-owned callback helpers can preserve the operation exception because they control its execution/cleanup boundary; AAPI-31 through AAPI-33 settle their callback families, ownership, and results.

**Owner/gate:** A10, D10-2 with H10/T10 consultation; W1/W2 verify explicit versus recovery tokens, configurable budget/default feasibility, cooperative limits, no unsafe abandoned work, continued cleanup, primary/secondary failure access, and no duplicate failure masking.

### AAPI-26: Structured Failure Context Separates Cause, Outcome, And Recovery

**Accepted:** 2026-08-31. Preserve ordinary provider/application exception types and add supported structured DataLinq failure information rather than wrapping every failure in a generic exception or requiring message/internal `Exception.Data` parsing.

| Information dimension | Required distinction |
| --- | --- |
| Failure cause | Observed caller cancellation, command timeout, provider error, local finalization error |
| Operation stage | Initialization, command execution, row loading, commit, rollback, cleanup |
| Database completion outcome | Completion not attempted, confirmed committed, confirmed rolled back, unknown, as applicable |
| Permitted recovery | Continue after successful cleanup, rollback/dispose only, dispose only, as applicable |
| Secondary failures | Rollback, resource disposal, cache recovery, notification failures |

These are semantic dimensions, not frozen public enum/property names. Caller cancellation and unknown commit outcome may both be true. Outcome, cause, and permitted recovery must not be collapsed into one mutually exclusive error classification. Do not confuse no completion attempt with no prior transaction writes.

Use `OperationCanceledException` for cancellation actually observed through the operation's cancellation path. Preserve provider timeout exceptions and error codes, unrelated provider/application failures even when the token is also canceled, and `TransactionCommitFinalizationException` for its existing known-committed meaning. Do not catch every exception under `ct.IsCancellationRequested` and relabel it cancellation. For races, use provider results and available evidence without claiming an unknowable event ordering.

Failure information must reach callers of database-level helpers after their implicit transaction is disposed, not only callers retaining an explicit transaction. Freeze exact accessors, compatibility, and operation-specific classifications in OAPI-7 after provider evidence under OAPI-9. Do not introduce a new generic exception hierarchy, automatic retry policy, or public provider-plugin API through this decision.

**Owner/gate:** A10, D10-1/D10-2 with H10/T10 consultation; W1/W2 fault injection and W3 consumer coverage must prove exception identity/stack, cause/outcome independence, recovery restrictions, and structured secondary failures for both explicit and implicit transaction APIs.

### AAPI-27: Capture Mutation Inputs Before The First Suspension

**Accepted:** 2026-09-04. Capture the operation's identity and relevant mapped values during the async mutation method call, before its first suspension. Connection opening, provider execution, or the caller's later `await` must not move this boundary. Local editing delegates run before the final capture under AAPI-29; multiple inputs follow AAPI-30.

Reuse the existing [mutation snapshot mechanism](../../../../src/DataLinq/Mutation/MutationSnapshot.cs), including supported array copies and mutation/version validation, instead of introducing a competing representation. `SaveAsync` selects insert versus update from the captured mutable lifecycle, preserving current `Save()` semantics rather than introducing a database upsert or change-tracker flush.

This does not promise arbitrary deep cloning. Existing snapshots copy arrays without recursively cloning arbitrary reference-valued scalar objects. Supported scalar values must remain stable under their existing contracts; mutations through escaped references or custom implementations cannot be assumed universally detectable.

**Owner/gate:** A10, D10-1/D10-2; W1/W2 prove identity/value/lifecycle capture before first suspension, delayed-await independence, supported array isolation and mutation checks, and consistent provider/cache/finalization inputs. Measure snapshot costs against the W0 baseline.

### AAPI-28: Pending Mutations Have Exclusive Use Of Their Mutable Inputs

**Accepted:** 2026-09-04. Give a pending mutation exclusive use of each mutable input until its public operation finishes. Through supported DataLinq mutation paths, reject conflicting assignments, resets, and another mutation using that same object, even through another transaction. Rejection must occur before changing the object. This protects successful [baseline replacement](../../../../src/DataLinq/Instances/Mutable.cs) from silently discarding edits made while database execution was pending.

| Lifetime boundary | Required behavior |
| --- | --- |
| Transaction-level mutation | Exclusive use lasts through that operation's local finalization; it need not extend to transaction commit |
| Database-level mutation helper | Exclusive use lasts through the helper's commit and cleanup |
| Successful operation finishes | Later edits/operations are allowed subject to normal lifecycle rules |
| Failed operation invalidates the mutable | Ending exclusive use does not restore a trustworthy baseline |
| Unsupported/custom mutation bypasses the guard | Retain snapshot/version checks and distinguish pre-execution rejection from inconsistency after writes |

This is a bounded ownership contract, not general mutable thread safety or universal interception of escaped array/object changes. OAPI-6 owns the wider operation coordination mechanism; OAPI-7 owns custom/generated implementation and compatibility coverage. The restriction applies to conflicting use during pending async work and does not authorize unrelated synchronous API changes.

**Owner/gate:** A10 with T10 consultation, D10-1/D10-2; verify supported setter/reset paths, same-object submission across transactions, transaction versus database-helper lifetime, release on every exit, and continued invalidation after failure.

### AAPI-29: Retain Synchronous Local Editing Delegates

**Accepted:** 2026-09-04. Retain synchronous `Action<TMutable>`-style local editing convenience overloads for supported async mutations. Invoke the editing delegate exactly once before execution; do not add an asynchronous editing-delegate family. Async orchestration belongs in task-returning transaction callbacks under AAPI-31.

Required ordering:

1. Validate arguments and lifecycle.
2. Check cancellation before invoking local edits.
3. Invoke the local editing delegate once.
4. Validate and capture the resulting mutation.
5. Check cancellation before database execution.
6. Execute asynchronously with the captured inputs and exclusive-use rules.

A delegate failure before database execution does not by itself poison the transaction. Assignments already made to a caller-owned mutable remain visible; DataLinq does not undo arbitrary delegate side effects. Cancellation observed after the delegate similarly prevents execution without reversing its local edits. These rules assume the delegate performs its intended local editing role, not hidden database operations.

Document the C# limitation: an async lambda can convert to `Action<TMutable>` as `async void`. DataLinq cannot await its completion or reliably handle later exceptions. Retaining `Action` is an ergonomics choice, not compile-time prevention of asynchronous misuse, and does not approve analyzers or runtime heuristics as new release scope.

**Owner/gate:** A10, D10-1/D10-2; consumer tests cover local edits, exactly-once invocation, validation/cancellation ordering, failures retaining local assignments without pre-write poisoning, and documented async-void limitations. Exact overloads remain under OAPI-7.

### AAPI-30: Capture Finite Multiple-Model Inputs Before Execution

**Accepted:** 2026-09-04. For supported async multiple-model convenience operations, enumerate the finite input exactly once and capture every model before the first suspension or DataLinq database write. Fix membership, order, identity, and mapped values for that operation. The existing synchronous multi-insert interleaves enumeration and writes; this decision specifies the new async counterpart without changing that synchronous implementation.

| Input condition | Required behavior |
| --- | --- |
| Input enumeration or local model validation fails | Fail before writes begin |
| Same mutable object appears twice | Reject before writes begin |
| Caller later changes the original list's membership | Captured operation membership is unchanged |
| Caller edits a captured mutable | Reject supported conflicting edits under AAPI-28 |
| Cancellation is observed after this call has written models | Apply AAPI-23; prevent committing the completed prefix |

Repeated-object detection uses object identity; distinct objects with conflicting database keys remain subject to normal validation/provider constraints. Preflight cannot guarantee success against later database constraints or concurrent database changes.

Upfront CPU/memory grows with input size. This is a finite, materializing convenience API, not an unbounded import facility. Input enumeration remains synchronous application code; DataLinq cannot turn hidden I/O in an `IEnumerable` iterator into async I/O. Applications should prepare such inputs using explicit async reads first.

Dependencies on generated values from earlier writes require separate awaited operations, normally inside a transaction callback. Do not add `IAsyncEnumerable<T>` mutation inputs, bulk execution, or automatic dependency ordering in 0.10.

**Owner/gate:** A10, D10-1/D10-2; W1/W2 cover single enumeration, ordering, all-model capture/preflight, duplicate object identity, cancellation before/after writes, ownership release on capture failure, and bounded workload allocation evidence. OAPI-7 identifies which existing multiple-model families receive counterparts.

### AAPI-31: Task-Returning Transaction Callback Families

**Accepted:** 2026-09-04. On `Database<TDatabase>`, provide these `CommitAsync` callback families, preserving the transaction-type option and an optional final cancellation token:

| Callback | Helper result |
| --- | --- |
| `Func<Transaction<TDatabase>, Task>` | `Task` |
| `Func<Transaction<TDatabase>, Task<TResult>>` | `Task<TResult>` |
| `Func<Transaction<TDatabase>, CancellationToken, Task>` | `Task` |
| `Func<Transaction<TDatabase>, CancellationToken, Task<TResult>>` | `Task<TResult>` |

Representative target signature:

```csharp
Task<TResult> CommitAsync<TResult>(
    Func<Transaction<TDatabase>, CancellationToken, Task<TResult>> action,
    TransactionType transactionType = TransactionType.ReadAndWrite,
    CancellationToken cancellationToken = default);
```

Do not add `Action<Transaction<TDatabase>>` or unrestricted synchronous `Func<Transaction<TDatabase>, TResult>` overloads to `CommitAsync`. Preserve AAPI-8's task-returning callback policy. Exact signatures, overload resolution, and the audited lower-level receiver surface remain subject to OAPI-7 consumer/compatibility review.

Guide asynchronous transaction work to `CommitAsync`. The current synchronous generic `Commit<TResult>` can receive a task as its result and then commit immediately after obtaining that task; the async examples must avoid this hazard. This decision does not silently redesign the existing synchronous overloads.

**Owner/gate:** A10 with H10 consultation, D10-1/D10-2; W3 consumers cover both callback arities, result/no-result forms, optional/named tokens and transaction type, method groups/async lambdas, and absence of unintended overload binding.

### AAPI-32: Callback Helpers Own Completion And Await Borrowed Work

**Accepted:** 2026-09-04. A `CommitAsync` helper owns its transaction lifecycle. Invoke its callback once and await the returned task; never automatically retry/replay it. The callback borrows the transaction for operations and may pass it to application services. Reject callback attempts to commit, roll back, or dispose the helper-owned transaction before those actions reach the provider. Applications needing manual completion use `Transaction()`.

Transaction operations started by the callback must finish before the callback returns. The enforcement and recovery mechanics for unfinished operations/readers and escaped work remain under OAPI-6; this decision does not authorize committing while they are active.

| Cancellation boundary | Required behavior |
| --- | --- |
| Observed before callback invocation | Do not invoke the callback |
| During callback work | Nested operations observe the tokens actually passed to them |
| Callback succeeds, then cancellation is observed before commit | Do not commit; perform independent recovery under AAPI-25 |
| Commit is confirmed | Follow AAPI-24; do not retroactively report cancellation |

The token-aware callback receives the helper's operation token. No mandatory ambient transaction token is introduced: an inner operation that omits its token does not inherit cancellation merely from the surrounding helper. Arbitrary callback code cannot be forcibly interrupted, and the helper still checks cancellation before committing.

Swallowing a mutation failure inside the callback cannot repair a poisoned transaction; final commit must reject it. Rollback does not undo external side effects such as HTTP requests or messages. Preserve AAPI-25/AAPI-26 failure and recovery reporting throughout helper-owned cleanup.

**Owner/gate:** A10 with H10/T10 consultation, D10-1/D10-2; prove invocation count, awaited callback completion, borrowed completion rejection, nested-service participation, explicit token delivery, pre-commit cancellation, poisoned-state rejection, and no automatic replay.

### AAPI-33: Deliver Callback Results After Completion And Cleanup

**Accepted:** 2026-09-04. Deliver callback `TResult` only after callback execution, commit, required finalization, and cleanup have all succeeded. Returning a value from the callback is an intermediate step; a known-committed cleanup failure is reported under AAPI-24 through AAPI-26 instead of returning ordinary success.

Prefer materialized immutable rows, lists/arrays, keys, scalars, and application DTOs. Callers must materialize deferred work bound to the helper's transaction inside the callback. The helper does not automatically enumerate arbitrary returned sequences, inspect whole result graphs, or secretly keep the transaction alive. Deferred work against an independent database root is not thereby made transaction-bound.

Returning immutable rows remains valid, including the accepted later relation transitions to committed reads under AAPI-20 when their source/lifecycle checks permit it. Do not impose a blanket post-transaction invalidity rule on all returned entities. The helper returns completed work and releases its transaction; it does not return an ongoing transaction session.

**Owner/gate:** A10 with H10/T10 consultation, D10-1/D10-2; W1/W2/W3 cover delayed result delivery, commit/finalization/cleanup failures, materialized results, rejection at execution of invalid deferred transaction-bound work, and valid later relation transitions without hidden transaction retention.

### OAPI-1: Task Versus ValueTask

**Resolved:** 2026-08-30 by [AAPI-8](#aapi-8-valuetask-for-query-and-relation-results-key-lookup-and-disposal-task-otherwise), including its final framework-alignment revision. The OAPI identifier is retained for existing references. Public awaitable types are decided; performance, consumption, and compatibility verification remain part of implementation evidence.

**Owner/gate:** A10, D10-1; validate in W1/W2 and verify exact public signatures in W3.

## Open Decisions Before The Complete API Is Frozen

The remaining questions in this section are open. Resolved portions are identified explicitly; recommendations are discussion starting points, not additional accepted requirements.

### OAPI-2: Relation Surface And Generator Compatibility

**Structural decisions resolved:** 2026-08-30 by AAPI-9 and AAPI-11 through AAPI-16. These settle the synchronous collection handle, async execution members/defaults, explicit row view and standard async LINQ, generated method placement/collisions, and required-reference behavior. AAPI-10 removes relation query composition from 0.10 entirely, including any requirement for queryable standalone test relations.

The remaining work is the exact primitive/overload inventory, exception/diagnostic selection, and compatibility verification under OAPI-7. Check interface and concrete receivers, custom implementations, inherited generated members, scalar/composite/converted keys, and existing local LINQ and translated navigation predicates. Default methods do not remove those checks.

AAPI-17 through AAPI-20 settle enumeration lifetime, capture, token combination, and reader/source ownership; direct `await foreach` on the relation itself is not an accepted addition. AAPI-21 through AAPI-26 define failure policies; OAPI-6 retains shared load coordination, complete cache publication, and invalidation across awaits. In particular, use the loaded result directly after awaiting: warming a cache and then calling a synchronous getter can reintroduce I/O.

**Owner/gate:** A10 with T10 consultation, D10-1; generated API and compatibility review before W3.

### OAPI-3: Streaming, Invocation Snapshots, And Reader Ownership

**Design decisions resolved:** 2026-08-30 by AAPI-17 through AAPI-20: direct async-sequence results without a universal streaming promise, distinct ordinary/prepared capture boundaries, deferred sequence I/O, repeated enumeration, combined method/enumerator tokens, deterministic resource disposal, rejection of another execution during an active transaction reader, and preservation of existing validated relation source transitions.

The identifier is retained for traceability. Exact signature/receiver inventory remains under OAPI-7, broader operation/load coordination under OAPI-6, and provider limits under OAPI-9. Failure and cleanup policies are accepted under AAPI-21 through AAPI-26. These remaining reviews do not reopen accepted contracts without an explicit revision.

**Owner/gate:** A10, D10-1/D10-2; lifetime feasibility in W2, complete contract before W3.

### OAPI-4: Cancellation, Commit Outcomes, And Cleanup Failures

**Design decisions resolved:** 2026-08-31 by AAPI-21 through AAPI-26: validation/pre-cancellation, terminal interrupted initialization, conditional read recovery and mutation poisoning, cancellation/finalization checkpoints, independent database completion outcomes, cooperative recovery budget, throwing disposal and exception precedence, and structured failure information.

The identifier remains for traceability. Exact public exception/context/configuration signatures and compatibility remain under OAPI-7; provider-specific classification, interruption limits, and verification of the 30-second starting recovery budget remain under OAPI-9 and W1/W2. AAPI-27 through AAPI-33 settle mutation-input/callback policy; wider operation/shared-load coordination remains under OAPI-6. These implementation/signature gates do not reopen accepted policies without an explicit revision.

**Owner/gate:** A10, D10-2; deterministic fault-injection and provider evidence in W1/W2 before W3.

### OAPI-5: Mutation Inputs And Transaction Callback Overloads

**Design decisions resolved:** 2026-09-04 by AAPI-27 through AAPI-33: mutation capture before suspension, exclusive mutable use, synchronous local editing delegates, finite multi-model capture, task-returning callback families, helper-owned completion/cancellation, and results delivered after cleanup.

Exact receiver/overload and custom/generated compatibility inventory remains under OAPI-7. OAPI-6 owns wider operation/cache coordination, internal ownership enforcement, and unfinished callback work. Existing local-edit `Action` ergonomics are accepted with the documented async-void limitation; no asynchronous editing delegates, unbounded mutation streams, bulk engine, or automatic dependency ordering are approved. These remaining gates do not reopen the accepted contracts without an explicit revision.

**Owner/gate:** A10 with H10 consultation, D10-1/D10-2; before W3 and final unit-of-work design.

### OAPI-6: Concurrency And Cache Coordination

Sequential mixing is accepted, and AAPI-20 requires rejection of another execution operation while a transaction reader remains active. The exact public diagnostic and the wider operation gate still need definition across awaits, transaction disposal during an active operation, cancellation of one waiter, and relation/cache invalidation while loading.

Recommendation: reject overlapping transaction operations deterministically rather than implicitly queueing them. Do not extend that restriction to independent database-root operations without evidence. If existing cache coordination shares work across callers, one caller's cancellation must not silently cancel unrelated consumers or publish incomplete cached values. This is preservation of existing cache guarantees, not authorization for new coalescing or cache policies.

**Owner/gate:** A10, D10-2; runtime tests in W1/W2 and H10 lifetime review.

### OAPI-7: Complete Overload Inventory And Public Extension Boundaries

Audit LINQ receivers and overloads, key lookup, prepared execution, SQL builders, raw command/reader APIs, schema metadata, existence checks, attached provider transactions, and disposal of owned database/provider resources. Give every public I/O boundary an explicit supported counterpart or documented exclusion.

Recommendation: put DataLinq query extensions in a deliberate namespace, reject incompatible query providers rather than falling back, and avoid an `IEnumerable<T>` catch-all that could hide synchronous database enumeration. Check ambiguity when EF Core or async-LINQ extensions are also imported. Review additions to existing public interfaces for source/binary compatibility with external implementations; keep backend internals private.

**Owner/gate:** A10, D10-1; W0 audit, W3 ApiCompat and consumer-shaped compilation coverage.

### OAPI-8: Synchronous Navigation Use In Async Applications

Existing lazy navigation remains supported. Decide whether guidance and explicit methods are sufficient for 0.10 or whether a separately approved diagnostic for accidental synchronous I/O is needed.

Recommendation: first document the boundary and use explicit relation results in async examples. A `ThrowOnSyncIo` mode, analyzers, eager-loading APIs, and batching are not silently approved by this record. Async loading a relation does not make every later synchronous navigation access permanently I/O-free.

**Owner/gate:** A10; any added feature requires the release scope process.

### OAPI-9: Backend Capability And Immediate Completion

Decide which async query operations the read-only Memory backend exposes, how it represents immediate completion, and how unsupported execution sources report the absence of async support. Define SQLite cancellation limits per operation rather than promising interruption the driver cannot provide.

Recommendation: retain the same backend capability validation and query rejection as synchronous execution. Completing immediately for in-memory work is legitimate; inventing SQL behavior or using `Task.Run` to suggest native async support is not. Memory writes and transactions remain out of scope.

**Owner/gate:** A10 with T10 consultation, D10-1/D10-2; provider/capability evidence before W3.

## Recommended Decision Order

OAPI-1, OAPI-2's structural choices, OAPI-3's enumeration contracts, OAPI-4's failure policies, and OAPI-5's mutation/callback contracts are resolved. Continue with:

1. OAPI-6: settle broader concurrency/cache coordination, operation ownership across awaits, and unfinished callback work before provider/public implementation is frozen.
2. Complete OAPI-7 and OAPI-9 across every audited boundary, including exact callback/failure-context/configuration signatures and provider evidence. Keep OAPI-8 within the agreed compatibility and release scope.

## Required Exit Evidence

- A complete signature inventory identifies receiver, result type, token position/default, backend support, ownership, and failure behavior.
- Consumer-shaped compilation tests cover token-free and token-supplied calls, generated single-reference methods, collection relation terminals, sync/async mixing, and both disposal forms.
- Signature/consumer checks enforce AAPI-8, including task-returning callbacks, direct single consumption of value tasks, and one-time `.AsTask()` conversion for reusable operations.
- Benchmarks cover cache-hit/miss and mixed execution without presenting the return-type decision itself as proof of an allocation or latency improvement.
- Controllable providers prove no I/O at transaction creation/unused disposal, correct first-use dispatch, and deterministic initialization failure/cleanup.
- Provider tests prove result, cache, mutation, telemetry, and terminal-state parity without synchronous fallback on native async paths.
- AAPI-17/AAPI-18 evidence covers direct `IAsyncEnumerable<T>` sequence results, no database I/O at sequence/enumerator construction or unused disposal, ordinary parameter capture at enumerator construction, prepared invocation capture at the execution call, terminal capture before first suspension, and sequential repeated enumeration without permanent result caching. Explicit materializers close owned readers before success; async views remain free to buffer.
- AAPI-19/AAPI-20 evidence covers optional method/enumerator tokens alone and combined, cancellation while iterating buffered rows, linked-token cleanup, reader/command disposal on all exits, preservation of caller transaction ownership, rejection of execution during a live reader, and valid/invalid later relation source transitions without migrating an active reader.
- AAPI-21 evidence covers pre-canceled warm/cold execution, argument/lifecycle validation precedence, no pre-canceled initialization or mutation, no partial materializer success, unchanged prior transaction work/usability, unused `CommitAsync` remaining unused, and no retroactive cancellation after completed success. Preserve AAPI-18's deferred sequence boundaries.
- AAPI-22 through AAPI-26 evidence covers private initialization publication/terminal interruption, reusable versus untrustworthy canceled reads, mutation/hydration poisoning and uninterrupted local finalization, partial multi-model cancellation, confirmed versus unknown completion independent of cleanup, explicit versus recovery tokens, cooperative budget feasibility, primary/secondary exception reporting and scope-disposal limitations, and structured cause/outcome/recovery information for explicit and implicit helpers. The release evidence plan owns the detailed matrix; no failure policy is considered implemented merely because it is accepted here.
- AAPI-27 through AAPI-33 evidence covers capture before suspension, exclusive mutable input use and lifetime, local delegate order/failures, finite single enumeration and all-model capture, duplicate object rejection, task-returning callback overload binding, borrowed completion restrictions, explicit token propagation, awaited callback work and post-cleanup result delivery. Preserve independent deferred database roots and validated later entity-relation transitions; measure input snapshot costs and verify custom/generated compatibility before claiming the surface complete.
- Generator and ApiCompat evidence cover new members, collisions, nullability, and existing consumer compatibility.
- Relation API evidence proves I/O-free collection handle access, relation-scoped keyed lookup, result/cardinality parity, no false complete-cache publication after partial execution, and preserved local LINQ/translated navigation predicate binding.
- AAPI-11 evidence records the approved source/binary and loading-timing migration, proves row versus key/value enumeration on interface and concrete receivers, and reviews exact ApiCompat diagnostics without hiding unrelated breaks.
- AAPI-12/AAPI-13 evidence covers local predicates, exact collection result/awaitable types, overload resolution with standard async LINQ, and packed .NET 8/9/10 consumers plus per-target dependency groups.
- AAPI-14/AAPI-15 evidence covers interface and concrete callers, shared async defaults and overrides, custom/test implementation compatibility without synchronous I/O fallback, public model-base visibility, and generated-name/inheritance diagnostics.
- AAPI-16 evidence covers optional versus required references, missing/duplicate targets, scalar/composite/converted keys, warm/cold/invalidation behavior, and test-helper parity in both sync and async paths. Release migration notes explicitly identify the required-reference behavior correction; nullable key lookup remains unchanged.
- Cancellation/failure tests distinguish database outcome from local finalization and cleanup outcomes.
- Evidence is recorded under [#106](https://github.com/bazer/DataLinq/issues/106); this decision record alone does not close W0, A10, or a release gate.

## Explicit Non-Goals

No separate async transaction type/factory, awaitable entities, task-valued navigation properties, new automatic lazy-loading mechanism, sync-over-async, general backend plugin API, relation-scoped query composition, broadened LINQ support, asynchronous editing-delegate family, async-stream mutation inputs, new batching/bulk engine or automatic mutation dependency ordering, Memory mutation/persistence, migrations, or release publication.

## External Comparisons

These references explain the accepted conventions and dependency policy; they do not add operations beyond DataLinq's supported query surface:

- [.NET's `ValueTask<T>` contract](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1?view=net-10.0) documents single consumption, one-time `.AsTask()` conversion, and the performance trade-offs behind AAPI-8.
- [EF Core's removal of direct `IAsyncEnumerable<T>` implementation from `DbSet`](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-6.0/breaking-changes#dbset-no-longer-implements-iasyncenumerable) illustrates LINQ overload ambiguity on receivers exposing multiple query/enumeration protocols; its mitigation is an explicit async-enumerable view.
- [.NET 10 async LINQ guidance](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/asyncenumerable) describes framework/package availability and package conflicts. Standard [`AsyncEnumerable.ToListAsync`](https://learn.microsoft.com/en-us/dotnet/api/system.linq.asyncenumerable.tolistasync?view=net-10.0) returns `ValueTask<List<T>>`; AAPI-8 aligns DataLinq's LINQ terminal awaitables with that convention.
- [Standard `Enumerable.AsEnumerable`](https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.asenumerable?view=net-10.0) returns the existing sequence with an `IEnumerable<T>` compile-time type, supporting the naming correction in AAPI-11.
- [NuGet conditional package references](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#adding-a-packagereference-condition) support AAPI-13's per-target dependency policy.
- [C# async-stream guidance](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream) explains `await foreach` disposal and enumerator cancellation. [Async iterator mechanics](https://learn.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8) explain method/enumerator token combination under `[EnumeratorCancellation]`, supporting AAPI-19/AAPI-20; its historical async-LINQ package advice does not replace AAPI-13.
- EF Core provides [`ToListAsync`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.entityframeworkqueryableextensions.tolistasync?view=efcore-10.0) and [`ToArrayAsync`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.entityframeworkqueryableextensions.toarrayasync?view=efcore-10.0), with optional cancellation tokens. [Query composition stays synchronous](https://learn.microsoft.com/en-us/ef/core/miscellaneous/async).
- EF Core's [`BeginTransactionAsync`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Storage/RelationalConnection.cs) opens a connection and starts the provider transaction immediately. It is not equivalent to DataLinq's lazy `Transaction()` factory.
- [Microsoft.Data.Sqlite async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async) explain why a shared awaitable surface cannot promise nonblocking SQLite I/O.
- [Microsoft cancellation guidance](https://devblogs.microsoft.com/premier-developer/recommended-patterns-for-cancellationtoken/) and [canceling an operation versus stopping the wait](https://devblogs.microsoft.com/dotnet/how-do-i-cancel-non-cancelable-async-operations/) support the consistency and resource-ownership boundaries in AAPI-23 through AAPI-25.
- [MySqlConnector cancellation](https://mysqlconnector.net/overview/command-cancellation/) documents distinct cancellation/timeout mechanisms and connection consequences; verify the pinned provider's behavior rather than inferring transaction usability from the exception name alone.
- [C# exception propagation through `finally`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/statements#1311-the-try-statement) explains AAPI-25's scope-disposal limitation.
- [C# async return types](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-return-types#void-return-type) explains why retained local-edit `Action` callbacks cannot safely await async-void work and why transaction callbacks return tasks.
