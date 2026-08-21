# Transactions

DataLinq supports both implicit and explicit transactions.

Use implicit transactions for one-off writes.
Use explicit transactions when several writes, reads, or relation updates must happen as one unit.

The managed `Transaction` wrapper is also the cache and mutable-lifecycle authority. Finish through `Commit()`, `Rollback()`, or `Dispose()` on that wrapper; low-level provider handles cannot complete DataLinq's local state safely.

The default transaction type is `TransactionType.ReadAndWrite`. There are also `ReadOnly` and `WriteOnly` modes when you want to be explicit about intent.

## Implicit Transactions

Single-operation write helpers open and complete the transaction for you.

Typical examples:

```csharp
var updated = employeesDb.Update(employeeMut);
var saved = employeesDb.Save(employeeMut);
var inserted = employeesDb.Insert(new MutableEmployee { /* ... */ });
employeesDb.Delete(existingEmployee);
```

This is the right choice when you only need one write and do not care about grouping several steps together.

## Explicit Transactions

Use an explicit transaction when you want several operations to succeed or fail together.

```csharp
using var transaction = employeesDb.Transaction();

var employee = transaction.Query().Employees.Single(x => x.emp_no == 999997).Mutate();
employee.birth_date = new DateOnly(1984, 12, 24);

transaction.Update(employee);
transaction.Commit();
```

Inside a transaction you can:

- query through `transaction.Query()`
- insert with `transaction.Insert(...)`
- update with `transaction.Update(...)`
- delete with `transaction.Delete(...)`
- save with `transaction.Save(...)`

## Convenience Transaction Callback

The test suite also uses the higher-level commit helper:

```csharp
employeesDb.Commit(transaction =>
{
    transaction.Insert(new MutableEmployee { /* ... */ });
    transaction.Insert(new MutableDepartment { /* ... */ });
});
```

That pattern is useful for short setup or maintenance operations.

## Attaching an Existing ADO.NET Transaction

If you already have a raw `IDbTransaction`, you can attach DataLinq to it:

```csharp
using IDbConnection dbConnection = employeesDb.Provider.GetDbConnection();
dbConnection.Open();

using var dbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);
using var transaction = employeesDb.AttachTransaction(dbTransaction);

var dept = transaction.Query().Departments.Single(x => x.DeptNo == "d099").Mutate();
dept.Name = "Transactional department";
transaction.Update(dept);
transaction.Commit();
```

This is an advanced ownership bridge to a provider-compatible ADO.NET transaction. The transaction must still be active on an open connection when it is attached.

After attachment, the DataLinq wrapper is the completion authority:

- perform mapped reads and writes through `transaction`
- call `transaction.Commit()`, `transaction.Rollback()`, or `transaction.Dispose()` to finish
- do not call `Commit()`, `Rollback()`, or `Dispose()` on the original `dbTransaction`
- do not complete through `transaction.DatabaseAccess` or `transaction.DatabaseAccess.DbTransaction`

Those low-level handles cannot finalize DataLinq's transaction-local rows, relation notifications, or mutable baselines. Current SQLite, MySQL, and MariaDB adapters also close and dispose the attached provider transaction and its connection during wrapper completion, so treat both as consumed instead of expecting to reuse the connection afterward.

If the original handle is completed externally anyway, DataLinq cannot infer whether it committed or rolled back. Supported providers detect the inactive handle on the next managed commit, rollback, read, write, transaction-bound fallback, or disposal operation. The wrapper then rejects the operation, invalidates transaction-derived mutable state, and clears caches conservatively instead of publishing a guessed result. Dispose the wrapper if that was not already the failing operation, discard transaction-bound rows and mutables, and materialize fresh committed rows through the database.

Raw writes are a separate boundary. DataLinq cannot reconstruct cache or relation effects for SQL executed before attachment or directly through the ADO.NET handles. If lower-level code changes mapped rows, either keep that workflow outside DataLinq's cache-coherent path or explicitly invalidate the affected DataLinq cache after completion; see [Explicit Cache Invalidation](Caching%20and%20Mutation.md#explicit-cache-invalidation).

Attached connections retain caller-selected isolation and provider settings. In particular, DataLinq does not rewrite SQLite pragmas on a caller-owned connection to make it match DataLinq-owned connection policy.

## Transaction Semantics

### Within a transaction

Within the same transaction:

- repeated reads of the same row return the same immutable instance
- transaction-local changes are visible through `transaction.Query()`
- relation updates are visible inside the transaction once inserted or updated rows exist there
- successful writes advance touched mutable baselines to transaction-local state; they are not committed baselines yet

If any managed mutation fails during provider execution, generated-value hydration, transaction-local cache application, or mutable finalization, the transaction becomes poisoned. The original mutation exception is preserved, touched mutables are invalidated with `MutationFailed`, and later managed reads, writes, and `Commit()` throw `TransactionPoisonedException`. Only `Rollback()` or `Dispose()` remains a valid managed recovery attempt.

### Completion outcomes

The database outcome and DataLinq's local outcome are separate facts:

| Outcome | What DataLinq does | What application code must do |
| --- | --- | --- |
| Clean commit | The provider commit succeeds, committed cache changes are published, transaction-local state is removed, touched mutable baselines are promoted, then committed status is observed. | Use returned/fresh immutable rows; treat the transaction as finished. |
| Mutation failure before completion | The transaction is poisoned and touched mutables are invalidated. No later managed read/write/commit is allowed. | Roll back or dispose; discard transaction rows and mutables; retry in a new transaction from fresh committed rows. |
| Clean rollback | Transaction-local state is removed and transaction-derived mutable baselines are invalidated as `RolledBack`. | Discard transaction rows and mutables; re-read through the database if continuing. |
| Database commit known, local finalization fails | `TransactionCommitFinalizationException` reports that the database committed, while DataLinq removes local state, clears committed caches conservatively, discards recovery notifications, and invalidates touched mutables. | Do **not** retry commit or report rollback. Re-read committed data through a fresh database scope. |
| Provider commit outcome unknown | The original provider exception is rethrown with DataLinq recovery context; local state is removed, committed caches are cleared conservatively, and touched mutables are invalidated as `CommitOutcomeUnknown`. | Do not assume commit or rollback. Dispose (or use the narrowly permitted rollback attempt only as provider recovery, not proof), then reconcile from fresh committed reads. |
| Rollback outcome unknown | DataLinq removes/clears uncertain state and invalidates mutables as `RollbackOutcomeUnknown`. | Do not claim the database rolled back. Dispose and reconcile from a fresh scope. |
| Attached transaction completed externally | DataLinq cannot infer commit versus rollback, clears uncertain cache state, and invalidates transaction-derived mutables as `ExternalCompletionUnknown`. | Dispose the wrapper, discard bound objects, and query fresh committed state. |
| Open transaction disposed | The provider is asked to dispose/roll back, transaction-local state is removed, and transaction-derived mutables are invalidated as `OpenTransactionDisposed`. | Treat the wrapper and all transaction-derived state as finished. |

`TransactionCommitFinalizationException` is intentionally different from an unknown commit exception: its existence means the database commit succeeded. Retrying the write can duplicate data.

### Mutable validity

A mutable created from or successfully written through a transaction is usable only while its baseline is trustworthy. A clean commit promotes that baseline only after cache publication and local cleanup succeed. Rollback, failed mutation, uncertain completion, external completion, disposal of an open transaction, or known-committed local finalization failure permanently invalidates it.

An invalid mutable throws an `InvalidOperationException` naming the invalidation reason when code tries to save, reset, or otherwise advance its baseline. Do not repair it by calling `Reset()` or copying values back into it. Materialize a fresh committed immutable row through the database, call `Mutate()` on that row, and retry the whole logical operation in a new transaction.

### Single-use lifecycle

The test suite explicitly covers that calling `Commit()` or `Rollback()` again after completion throws. Reads and writes through a committed or rolled-back wrapper are also rejected. Concurrent managed operations are fenced while mutation or completion is being finalized.

That is the correct behavior. A transaction object is not a reusable session object.

## Recovery Checklist

After any failure whose outcome or local state is uncertain:

1. Preserve the original exception and inspect its type and DataLinq context; do not translate every failure into “rolled back”.
2. Finish only through the managed wrapper using the operation still permitted by the diagnostic.
3. Discard transaction-bound immutable rows, relation results, and mutable instances.
4. Let DataLinq's conservative cache cleanup stand; if raw writes occurred outside the wrapper, explicitly invalidate the affected database/table/rows too.
5. Create a fresh database/transaction scope and re-read committed data before deciding whether to retry.

Retry the whole idempotent business operation only after reconciling state. Retrying an isolated statement after an unknown commit is how duplicate rows are born.

## Provider Caveat: SQLite vs MySQL/MariaDB

DataLinq-owned SQLite connections enforce `PRAGMA read_uncommitted = false`, including when a pooled connection previously enabled it. Owned SQLite transactions use `IsolationLevel.Serializable` in deferred mode. The owning transaction still sees its own writes, while an ordinary outside connection never receives those pending values.

That is **committed visibility**, not literal MySQL/MariaDB `READ COMMITTED` equivalence:

- SQLite has snapshot/serializable transaction behavior and a single-writer model.
- File-backed SQLite with WAL and private/default cache can let outside readers retain the last committed value during a pending write.
- Explicit SQLite shared-cache configurations can produce `SQLITE_LOCKED` instead of serving that committed snapshot. They still must not expose the pending value.
- MySQL and MariaDB continue to use `IsolationLevel.ReadCommitted`.

Attached SQLite transactions retain the caller's connection pragmas and isolation policy. DataLinq applies its committed-visibility policy only to connections and transactions it owns.

## Relations Inside Transactions

The transaction tests cover relation-aware inserts such as adding a salary row to an employee.

That matters because DataLinq is not just writing the row. It is also maintaining the relation view that the in-memory object graph sees.

Example pattern:

```csharp
using var transaction = employeesDb.Transaction();

var employee = transaction.Query().Employees.Single(x => x.emp_no == empNo);

var salary = transaction.Insert(new MutableSalaries
{
    emp_no = employee.emp_no.Value,
    salary = 50000,
    FromDate = new DateOnly(2020, 1, 1),
    ToDate = new DateOnly(2020, 12, 31)
});

transaction.Commit();
```

Within the transaction, relation reads such as `employee.salaries` are covered by tests and should reflect the transaction-local state.

## When to Use What

- Use implicit transactions for simple single writes.
- Use explicit transactions for multi-step workflows, relation-heavy updates, or when you need reads and writes to share one transaction scope.
- Use `AttachTransaction(...)` only when you already have a real reason to supply the ADO.NET transaction, then transfer completion and lifecycle coordination to the returned DataLinq wrapper.
