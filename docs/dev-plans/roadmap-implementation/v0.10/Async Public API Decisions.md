> [!WARNING]
> This is an API design record for a future release. Async examples below are proposed consumer code, not APIs shipped in DataLinq 0.9.

# 0.10 Async Public API Decisions

**Status:** Accepted for the decisions explicitly marked accepted below. Open questions and recommendations are not approved contracts.

**Target release:** 0.10 / A10 ([issue #107](https://github.com/bazer/DataLinq/issues/107)).

**Last reviewed:** 2026-08-30.

**Prerequisites:** The [W0 baseline and I/O audit](Implementation%20Order%20and%20Integration%20Plan.md#w0-baseline-and-io-inventory) and W1/W2 provider feasibility evidence remain required before implementation changes shared execution and freezes the complete public surface.

**Authority:** The [release roadmap](README.md) owns scope. This record owns the accepted public async API decisions and remaining questions under D10-1/D10-2. The [implementation order](Implementation%20Order%20and%20Integration%20Plan.md) owns sequencing; the [release evidence plan](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md) owns verification.

## Purpose And Decision Boundary

Record the API decisions agreed on 2026-08-30 without pretending that every signature or failure case is settled. Public design discussion can precede implementation; it does not replace the before-state evidence or provider feasibility gate.

The accepted direction is additive: ordinary application code chooses synchronous or asynchronous execution at each operation. It does not choose a separate async database, transaction, or entity model.

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

`DisposeAsync()` follows the standard parameterless `ValueTask` signature. Failure precedence and cleanup behavior after cancellation still need the explicit decisions listed below.

### AAPI-3: Async Is Chosen Per Execution Operation

Keep local operations synchronous: `Query()`, query composition (`Where`, `Select`, ordering and paging), query preparation, `Mutate()`, and mutable property assignment do not receive async variants merely because they participate in an async workflow.

Provide explicit async counterparts for the supported execution families. Preserve existing sync behavior, query support limits, conversions, cache and invalidation rules, logging, metrics, and transaction terminal semantics. Synchronous APIs remain direct synchronous implementations.

Do not run simultaneous managed operations on the same transaction. Sequential mixing is supported; exact overlap diagnostics and what counts as an active operation during streaming remain open under OAPI-3/OAPI-6.

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

### AAPI-5: Relation Methods Use The Property Name Plus Async

Generate an explicit method beside each supported navigation property:

```csharp
var department = await employee.DepartmentAsync();
var salaries = await employee.SalariesAsync();

var cancelableDepartment = await employee.DepartmentAsync(ct);
var cancelableSalaries = await employee.SalariesAsync(ct);
```

Use `<PropertyName>Async`, preserving the declared property name, without a `Load` or `Get` prefix. Do not make entities awaitable or replace navigation properties with `Task<T>` properties.

The generated method resolves through the internal relation metadata and loading services. It must not first evaluate the synchronous navigation getter. Current generated singular getters access an `IImmutableForeignKey<T>.Value`, which may synchronously load a row; wrapping that getter in an async method would not meet this contract. See [generator output construction](../../../../src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs) and [reference loading](../../../../src/DataLinq/Instances/ImmutableForeignKey.cs).

Retain the existing synchronous navigation properties for compatibility, including their current lazy-loading behavior. The async surface introduces an explicit way to perform that work. The roadmap's prohibition on hidden property I/O means no new hidden I/O mechanism and no synchronous getter evaluation inside async loading; it does not remove existing navigation behavior.

Async loading must preserve the relation's metadata, nullability contract, cache semantics, and source/transaction ownership. Collection return types, missing-target diagnostics, and generated-name collisions remain open below. A warmed relation is not a permanent guarantee against future I/O: invalidation can make a later synchronous property access load again.

### AAPI-6: Use Familiar Async Query Execution Names

The accepted naming direction is the synchronous execution name plus `Async`, including `ToListAsync()` and `ToArrayAsync()`. Query composition remains ordinary LINQ.

| Surface | Intended names | Remaining work |
| --- | --- | --- |
| Buffered query results | `ToListAsync`, `ToArrayAsync` | Awaitable types and complete receiver/overload inventory |
| Supported row terminals | `FirstAsync`, `FirstOrDefaultAsync`, `SingleAsync`, `SingleOrDefaultAsync`, `LastAsync`, `LastOrDefaultAsync` | Match current predicate, projection, empty-result, and cardinality contracts |
| Supported scalar reductions | `AnyAsync`, `CountAsync`, `SumAsync`, `MinAsync`, `MaxAsync`, `AverageAsync` | Exact selector, numeric, and nullable overloads |
| Key lookup | `GetAsync` on the database/transaction and generated model helpers | Typed/composite keys, source receivers, nullability, awaitable types |
| Prepared queries | Async execution counterparts; preparation remains synchronous | Scalar versus sequence signatures and snapshot/streaming semantics |
| Mutations | `InsertAsync`, `UpdateAsync`, `SaveAsync`, `DeleteAsync` | Single/multiple model and change-delegate overloads; mutable input lifetime |
| Completion | `CommitAsync`, `RollbackAsync`, `DisposeAsync` | Callback overloads, failure reporting, cleanup precedence |
| Streaming | `AsAsyncEnumerable` is the candidate direction | Public contract is not frozen; see OAPI-3 |
| Provider metadata and lower-level SQL | Async counterparts for audited I/O operations | Explicit inclusion/exclusion list before W3 |

This table is an inventory starting point, not a declaration that all overloads or backend query shapes are supported. Async does not add `All`, `LongCount`, arbitrary joins, new mutation semantics, or unrestricted client-side fallback to the supported query language.

### AAPI-7: Be Explicit About Provider Limits

Use native asynchronous provider operations wherever they exist. Do not implement them with `Task.Run`, `.Result`, or `.GetAwaiter().GetResult()`.

The common awaitable API is not a guarantee that every provider performs nonblocking I/O. Microsoft.Data.Sqlite executes its async ADO.NET methods synchronously; the SQLite implementation and cancellation documentation must state that limitation. It is the explicit provider exception already allowed by A10, not permission to wrap genuinely asynchronous providers in synchronous work. Memory completion and unsupported backends require an explicit decision under OAPI-9.

## Open Decisions Before The Complete API Is Frozen

Everything in this section is open. Recommendations are discussion starting points, not additional accepted requirements.

### OAPI-1: Task Versus ValueTask

Decide the public awaitable type for each family, especially cache-hit key lookups and relation access. Changing `Task<T>` to `ValueTask<T>` later is a public API change even if simple `await` call sites look identical.

Recommendation: start with `Task`/`Task<T>` for ordinary query materialization, mutations, completion, and callback delegates; measure representative cache-hit lookup/relation paths before deciding whether public `ValueTask<T>` is justified. Account for composition and repeated-await expectations as well as allocations. Internal choices may differ. `DisposeAsync()` already has its standard `ValueTask` shape.

**Owner/gate:** A10, D10-1; measure in W1/W2 and freeze before W3.

### OAPI-2: Relation Result Shape And Generator Compatibility

Decide whether `SalariesAsync()` returns a fully materialized immutable collection, another read-only collection, or a relation handle. Also define missing optional versus broken required references, duplicate reference targets, and behavior after transaction completion.

Recommendation: prefer a buffered result that callers can enumerate without keeping a reader alive. Compare `ImmutableArray<T>` with `IReadOnlyList<T>` against existing [relation values](../../../../src/DataLinq/Instances/ImmutableRelation.cs). Preserve existing lookup/cardinality behavior unless a compatibility change is explicitly approved; do not silently turn missing required data into a new exception policy.

Decide how generated methods appear on partial base models, generated implementations, and testing surfaces. Cover collisions with user-defined `<PropertyName>Async` members, inheritance, nullability, and scalar/composite relation keys. Recommendation: issue a focused collision diagnostic rather than silently renaming the accepted API. Do not require an unrelated generator/interface rewrite.

**Owner/gate:** A10 with T10 consultation, D10-1; generated API and compatibility review before W3.

### OAPI-3: Streaming, Invocation Snapshots, And Reader Ownership

Decide the exact `AsAsyncEnumerable()` and prepared-sequence execution signatures, when database execution starts, when current argument values are captured, how cancellation is supplied during enumeration, and what repeated enumeration means.

Preserve the existing prepared-query guarantee: mutable invocation values are snapshotted at `Execute(...)` call time, before lazy enumeration. An async iterator must not accidentally move that snapshot into the first `MoveNextAsync()`. See [prepared queries](../../../../src/DataLinq/Linq/PreparedQuery.cs).

Recommendation: use `IAsyncEnumerable<T>` for streaming, keep query composition separate from execution, and define ordinary versus prepared invocation timing explicitly. Require deterministic reader/command disposal on completion, exceptions, cancellation, and early `break`. Document that enumeration cannot outlive its execution source. Decide whether another operation during a live stream is rejected; do not assume a partially enumerated reader permits nested queries on the same transaction.

**Owner/gate:** A10, D10-1/D10-2; lifetime feasibility in W2, complete contract before W3.

### OAPI-4: Cancellation, Commit Outcomes, And Cleanup Failures

Define pre-canceled calls including cache hits; cancellation during initialization, command execution, row loading, and mutable finalization; timeout classification; and which transaction states allow further work after each failure.

The accepted A10 requirements already distinguish caller cancellation, timeout, provider failure, rollback failure, and uncertain commit. They do not yet specify every exception type or public outcome property. A known database commit with failed local finalization must retain its existing distinct meaning.

Recommendations for discussion:

- Check pre-cancellation consistently even when a read could hit cache; do not return a partially filled buffered collection as success.
- Do not report confirmed commit success as cancellation solely because the token changed afterward, and never equate cancellation with rollback.
- Keep uncertain outcomes explicit and prevent unsafe automatic retry or publication of guessed committed state.
- Do not let an already-canceled operation token prevent necessary cleanup. Decide any bounded cleanup policy separately.
- Preserve the original operation failure when rollback or disposal also fails, while exposing the cleanup failure rather than hiding it.
- Define whether a canceled first-use initialization can be retried on the same wrapper or makes it terminal; do not publish a half-initialized connection/transaction as usable.

**Owner/gate:** A10, D10-2; deterministic fault-injection and provider evidence in W1/W2 before W3.

### OAPI-5: Mutation Inputs And Transaction Callback Overloads

List async counterparts for current change delegates, multiple-model operations, and result-returning `Commit` callbacks. Decide when mutable values and input sequences become fixed relative to the first await and how callers are prevented from changing in-flight inputs.

Recommendation: preserve the distinction between local change delegates and asynchronous transaction callbacks. Execute local edits synchronously; use task-returning callbacks for actual async operations. Define callback token propagation, result delivery after successful commit, and failure cleanup without adding `async void` or ambiguous delegate overloads. Existing multi-model convenience methods do not authorize a new batching/bulk engine.

**Owner/gate:** A10 with H10 consultation, D10-1/D10-2; before W3 and final unit-of-work design.

### OAPI-6: Concurrency And Cache Coordination

Sequential mixing is accepted; the exact public response to overlap is not. Define the operation gate across awaits, transaction disposal during an active operation, cancellation of one waiter, and relation/cache invalidation while loading.

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

1. OAPI-1 and OAPI-2: awaitable and relation result types determine the signatures application code will consume.
2. OAPI-3: settle streaming and snapshot timing before committing to sequence APIs.
3. OAPI-4 through OAPI-6: settle failure, mutation-input, and concurrency contracts together, before provider/public implementation is frozen.
4. Complete OAPI-7 and OAPI-9 across every audited boundary. Keep OAPI-8 within the agreed compatibility and release scope.

## Required Exit Evidence

- A complete signature inventory identifies receiver, result type, token position/default, backend support, ownership, and failure behavior.
- Consumer-shaped compilation tests cover token-free and token-supplied calls, generated relation methods, sync/async mixing, and both disposal forms.
- Controllable providers prove no I/O at transaction creation/unused disposal, correct first-use dispatch, and deterministic initialization failure/cleanup.
- Provider tests prove result, cache, mutation, telemetry, and terminal-state parity without synchronous fallback on native async paths.
- Streaming tests cover snapshot timing, early disposal, cancellation, source lifetime, and active-reader overlap.
- Generator and ApiCompat evidence cover new members, collisions, nullability, and existing consumer compatibility.
- Cancellation/failure tests distinguish database outcome from local finalization and cleanup outcomes.
- Evidence is recorded under [#106](https://github.com/bazer/DataLinq/issues/106); this decision record alone does not close W0, A10, or a release gate.

## Explicit Non-Goals

No separate async transaction type/factory, awaitable entities, task-valued navigation properties, new automatic lazy-loading mechanism, sync-over-async, general backend plugin API, broadened LINQ support, new batching/bulk engine, Memory mutation/persistence, migrations, or release publication.

## External Comparisons

These references explain familiar conventions, not dependencies or additional DataLinq scope:

- EF Core provides [`ToListAsync`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.entityframeworkqueryableextensions.tolistasync?view=efcore-10.0) and [`ToArrayAsync`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.entityframeworkqueryableextensions.toarrayasync?view=efcore-10.0), with optional cancellation tokens. [Query composition stays synchronous](https://learn.microsoft.com/en-us/ef/core/miscellaneous/async).
- EF Core's [`BeginTransactionAsync`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Storage/RelationalConnection.cs) opens a connection and starts the provider transaction immediately. It is not equivalent to DataLinq's lazy `Transaction()` factory.
- [Microsoft.Data.Sqlite async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async) explain why a shared awaitable surface cannot promise nonblocking SQLite I/O.
