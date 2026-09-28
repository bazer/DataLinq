# Async Execution

This page describes the implemented **0.10 development APIs**, which are not yet released. Applications on 0.9 use the synchronous APIs. Async execution preserves the existing [query translation limits](Supported%20LINQ%20Queries.md) and provider capabilities.

## Queries and typed lookups

Import `DataLinq.Linq` for provider query terminals. Query construction stays synchronous; await execution:

```csharp
using DataLinq.Linq;

var departments = await db.Query().Departments
    .Where(department => department.Name.StartsWith("D"))
    .ToListAsync(cancellationToken);

var department = await Department.GetAsync("d005", db, cancellationToken);
```

The query surface includes list/array materialization, First/Single/Last and their default forms, Any/Count, and supported selector-based Sum/Average/Min/Max. Query predicates and selectors are expressions translated by the provider. A signature accepting an anonymous, interface or DTO result does not make an otherwise unsupported projection translatable. There is no fallback to synchronous enumeration or local evaluation of an unsupported query.

Most query terminals and key lookups return `ValueTask<T>`; await each value once. A cache hit or Memory operation can complete immediately. Prepared scalar execution returns `Task<TResult>`; prepared sequences return `IAsyncEnumerable<T>`, preserving their prepared argument binding and source restrictions.

Importing EF Core's extensions in the same file can make names such as `CountAsync` ambiguous. Use an explicit alias to choose the provider API:

```csharp
using DataLinqAsync = DataLinq.Linq.DataLinqAsyncQueryableExtensions;

var count = await DataLinqAsync.CountAsync(db.Query().Departments, cancellationToken);
```

These methods reject queries owned by another query provider. On .NET 8 and 9, DataLinq brings `System.Linq.AsyncEnumerable` transitively for local async sequence composition. .NET 10 uses the framework implementation.

## Deferred sequences and capture

```csharp
await foreach (var department in db.Query().Departments
    .AsAsyncEnumerable(cancellationToken))
{
    Console.WriteLine(department.Name);
}
```

Creating a query sequence performs no database I/O. Each `GetAsyncEnumerator` captures that invocation's values and source; the first move starts execution. Re-enumeration creates another invocation and may execute again. Terminal methods such as `ToListAsync` capture before their first suspension. Do not modify mutable inputs concurrently with capture.

Both the method token and an enumerator token supplied through `WithCancellation` apply. Disposing an unused enumerator performs no query. Dispose every started enumerator, including on early exit; `await foreach` does this automatically. Materializers return only after their owned reader cleanup succeeds, and do not return partial collections on failure.

An async sequence may buffer keys or rows. It does not promise one native read per yielded element or constant memory use. Transaction-bound sequences retain operation ownership until their work and cleanup finish; they do not commit or dispose the caller's transaction.

## Relations and reference navigation

Collection relation methods operate on asynchronously loaded related rows. They use local delegates after loading, unlike provider query expressions:

```csharp
var department = await Department.GetAsync("d005", db, cancellationToken)
    ?? throw new InvalidOperationException("Department not found.");
var managers = await department.Managers.ToListAsync(cancellationToken);
var any = await department.Managers.AnyAsync(manager => manager.emp_no > 10000,
    cancellationToken);
```

Use `relation.AsAsyncEnumerable()` for standard local async LINQ composition. `KeysAsync`, `ValuesAsync`, `ContainsKeyAsync`, `GetAsync` and `ToFrozenDictionaryAsync` retain relation membership semantics. Dictionary materialization rejects duplicate keys. Local numeric reductions follow the standard numeric/empty-sequence rules; they do not become SQL aggregates.

Generated reference navigation appends `Async` to the exact property name. For example, the `departments` property has `departmentsAsync(cancellationToken)`. Required references return a model or throw an identifying `InvalidOperationException`; optional references return null when absent. Async and synchronous navigation share relation state, but async navigation does not call the synchronous property getter. Invalidated state may need another load. Retain the awaited value when subsequent application code must avoid another navigation lookup.

Rebuild generated consumers to obtain these methods. `DLG004` identifies a user-declared or inherited member that conflicts with a generated async navigation call. Rename or correct that member; disabling nullable annotations does not make incompatible navigation safe. See the [relation migration notes](Relations%20and%20Joins.md#010-enumeration-migration-unreleased) for the separate keyed-enumeration and required-reference changes.

## Mutations and transaction ownership

Database mutation helpers own their transaction and finish commit, local finalization and cleanup before returning. Explicit transaction mutation methods leave completion to their caller:

```csharp
await using var transaction = db.Transaction();
var department = await Department.GetAsync("d005", transaction, cancellationToken)
    ?? throw new InvalidOperationException("Department not found.");
await transaction.UpdateAsync(department, changes => changes.Name = "Development",
    cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

Construction remains synchronous and provider transaction initialization is lazy. A transaction supports one active operation; overlapping reads, writes, completion or disposal are rejected. Await active work before starting the next operation. A reader that reached EOF still needs disposal before its transaction can complete.

For a short owned unit of work, use the async callback helper:

```csharp
var count = await db.CommitAsync(async (transaction, token) =>
{
    return await transaction.Query().Departments.CountAsync(token);
}, cancellationToken: cancellationToken);
```

Typed database and untyped provider helpers accept token-aware or token-free `Task` callbacks, with or without a result. The helper calls the callback once, then owns completion and cleanup. Await everything started inside the callback and leave commit/rollback/disposal to the helper. Do not pass an async lambda to the synchronous `Commit(Action<...>)` overload.

Generated mutation `changes` callbacks remain **synchronous local edits**. Never pass an async lambda to those `Action<TMutable>` parameters. Edits and input capture occur before suspension after validation/cancellation checks. The mutable remains reserved while the operation owns it; generated database-owned edited mutations retain that reservation through commit and cleanup. Mutating or resetting an actively reserved input is rejected. Generated immutable `SaveAsync` is an update alias, not an insert-on-null operation.

Complete attached transactions through their managed wrapper. Attachment consumes the transaction and connection as described in [Transactions](Transactions.md#attaching-an-existing-adonet-transaction). Standalone provider transactions have async commands and completion, but do not provide managed cache/mutable finalization. Raw writes require explicit cache invalidation where applicable.

## Raw commands, readers and fluent reads

`IDatabaseAccess` and `DatabaseAccess` expose string and `IDbCommand` overloads of `ExecuteNonQueryAsync`, `ExecuteScalarAsync`, `ExecuteScalarAsync<T>`, `ExecuteReaderAsync` and `ReadReaderAsync`. A string overload owns its created command. A caller-supplied command remains borrowed: keep it, its parameters and connection binding stable until execution and cleanup finish, and dispose it yourself afterward. Existing `ToDbCommand()` output is usable on its compatible access object.

```csharp
await using var reader = await db.Provider.DatabaseAccess.ExecuteReaderAsync(
    "SELECT 1", cancellationToken);
while (await reader.ReadNextRowAsync(cancellationToken))
{
    Console.WriteLine(reader.GetInt32(0));
}
```

The direct reader is `IDataLinqAsyncDataReader`. Its getters inspect the available row; only advancement performs another read. Use `ReadNextRowAsync` and `DisposeAsync` for async execution. The inherited synchronous methods remain available when explicitly selected; async calls do not fall back to them. Wait for each pending call before another call or disposal. Reader cleanup does not complete the surrounding transaction.

`ReadReaderAsync` sequences instead expose **borrowed current-row views**. Low-level access returns `IDataLinqDataReader` views; fluent `Select<T>.ReadReaderAsync` returns `IDataLinqAsyncDataReader` views. Neither view may be advanced or disposed by application code. A view expires when its enumerator advances or is disposed. Read the needed values inside the loop, or use `ReadRowsAsync` for detached rows.

Fluent selections also expose first-row, primary-key/grouped-key, model and scalar execution. `ExecuteAsAsync<T>` casts supported model instances; it is not arbitrary DTO mapping. `DataSourceAccess.GetFromQueryAsync<T>` and `GetFromCommandAsync<T>` retain the existing raw model-materialization rules. SQL that returns rows can still have effects; raw execution does not infer cache changes or safe retry permission.

## Cancellation, failure and disposal

Pass `CancellationToken` by name when optional arguments precede it. Argument, lifecycle and capability validation can fail before cancellation is observed. Cancellation is cooperative: it does not prove that SQL was never dispatched, that a write had no effect, or that a commit failed. Do not retry a write solely because an `OperationCanceledException` was thrown.

`DataLinqFailure.GetContext(exception)` in `DataLinq.Diagnostics` returns the immutable snapshot attached directly to that exception, or null. It does not search inner exceptions or aggregate branches. `Transaction.FailureContext` exposes the current managed transaction snapshot. Cause, stage, completion certainty and permitted recovery are separate fields; an unknown completion outcome must stay unknown until the application reconciles it from fresh committed state. Secondary failures retain their original exception instances and order. Existing exception types and original exception information remain available.

Recovery permission is a snapshot, and each later operation validates the current state. `DataLinqExecutionOptions.RecoveryRollbackTimeout` supplies an independent automatic rollback budget, defaulting to 30 seconds. It is captured at provider construction and must be between 1 and 4,294,967,294 milliseconds inclusive. It is neither the request timeout nor a hard cleanup deadline: ownership remains held until outstanding work and cleanup actually settle.

Use `await using` for roots/transactions/readers acquired for async work. Finish active work first; root disposal is not a draining service. A synchronous-only custom provider or access implementation receives an explicit unsupported result from the new defaults, without silently running its synchronous method. The old synchronous API remains available when deliberately selected.

## Provider and administration limits

MySQL/MariaDB use native driver async paths. SQLite's driver may execute synchronously inside async methods, including native command/row/cleanup work; async signatures do not make that work interruptible or move it to the thread pool. SQLite constructor keeper/journal setup and MariaDB's constructor server-version probe retain their synchronous timing. A later cancellation token cannot cancel work already done by a constructor.

The experimental Memory backend supports its existing read-only subset, including async query execution and `FindAsync`. It does not gain SQL navigation, mutations, transactions or prepared execution merely because the public query methods exist. Unsupported aggregates and other excluded shapes remain rejected.

Availability probes, metadata import, provisioning and SQLite journal configuration have async counterparts. Missing objects are distinct from cancellation or metadata-query failure. Provisioning can leave partial objects after failure; there is no automatic retry or fabricated atomic DDL guarantee. Runtime schema validation and hosting integration are separate later-wave work and are not provided by these W3 APIs.
