> [!WARNING]
> This document is roadmap/design material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# SQLite Transaction Isolation Alignment

**Status:** Draft design note.

## Purpose

DataLinq currently opens SQLite transactions and non-transactional SQLite commands with `ReadUncommitted`, while MySQL and MariaDB transactions use `ReadCommitted`.

That difference leaks into user-visible behavior:

- SQLite can expose uncommitted writes to other DataLinq reads when shared-cache mode and `PRAGMA read_uncommitted = true` are both active.
- MySQL and MariaDB do not expose those pending writes under the current `ReadCommitted` transaction path.
- Some relation and transaction tests now encode SQLite-specific dirty visibility instead of provider-independent committed visibility.

The goal is to define a path where DataLinq-visible SQLite behavior aligns with MySQL/MariaDB for the important ORM surface: uncommitted writes should be visible inside the owning transaction and invisible outside it until commit.

## Current DataLinq Shape

The relevant implementation points are:

- `src/DataLinq.SQLite/SQLiteDatabaseTransaction.cs`
  - opens SQLite provider transactions with `IsolationLevel.ReadUncommitted`
  - sets `PRAGMA read_uncommitted = true`
- `src/DataLinq.SQLite/SQLiteDbAccess.cs`
  - sets `ReadUncommitted` for non-transaction SQLite commands too
- `src/DataLinq.SQLite/SQLiteProvider.cs`
  - enables WAL through `PRAGMA journal_mode = WAL`
- `src/DataLinq.Testing/Environment/PodmanTestEnvironmentSettings.cs`
  - creates SQLite file and named in-memory test connections with `Cache=Shared`
- `src/DataLinq.MySql/Shared/SqlDatabaseTransaction.cs`
  - opens MySQL/MariaDB transactions with `IsolationLevel.ReadCommitted`
- `src/DataLinq/Mutation/Transaction.cs`
  - applies transaction changes to provider state immediately after executing each write
- `src/DataLinq/Cache/TableCache.cs`
  - invalidates committed/global row and relation cache state as part of that immediate state application

That immediate state application is the important DataLinq-specific part. A transaction write executes against the database, then the cache is changed or invalidated before the database transaction commits. Outside relation objects can then refresh while SQLite still has an uncommitted write open. Under `ReadUncommitted`, SQLite can return the pending row to that outside read.

## SQLite Semantics That Matter

SQLite is not MySQL with a smaller parser. The isolation model is structurally different.

SQLite's default isolation is serializable. Separate connections do not see each other's uncommitted changes unless both of these are true:

- the connections use shared cache
- the reader enables `PRAGMA read_uncommitted`

The current SQLite test/provider shape does exactly that for file and named in-memory connections.

WAL is the right SQLite feature for file-backed reader/writer concurrency. Shared-cache mode is not. SQLite documents shared cache as obsolete and discouraged, and Microsoft documents that `Cache=Shared` changes transaction/table locking behavior. Microsoft also warns that mixing shared-cache mode and WAL is discouraged.

One more catch: `IsolationLevel.ReadCommitted` is not a true SQLite equivalent of MySQL/MariaDB `READ COMMITTED`. `Microsoft.Data.Sqlite` treats the requested isolation as a minimum and promotes to either read-uncommitted or serializable. So switching from `ReadUncommitted` to `ReadCommitted` is effectively switching SQLite to committed/serializable behavior, not to MySQL-style per-statement read committed snapshots.

That is acceptable if the product goal is committed visibility, but the docs and tests should not claim literal backend equivalence.

## Desired Semantics

For DataLinq-managed entity and relation reads:

- a transaction sees its own inserts, updates, and deletes
- other transactions do not see those changes before commit
- normal `database.Query()` reads do not see those changes before commit
- rollback discards transaction-local state without invalidating committed state
- commit publishes changes only after the underlying database commit succeeds
- relation caches outside the transaction are notified after commit, not during the pending transaction

That target should be named something like **committed visibility semantics**, not `ReadCommitted` semantics. The latter implies more precision than SQLite can honestly provide.

## Non-Goals

DataLinq should not pretend it can fully sanitize arbitrary SQLite dirty reads while the SQLite connection itself remains in `ReadUncommitted` mode.

The following surfaces cannot be made fully safe by a cache overlay alone:

- raw SQL executed through `DatabaseAccess`
- arbitrary projections
- aggregate queries such as `COUNT(*)`
- joins that bypass entity materialization
- external connections not created through DataLinq
- external transactions attached after they already performed writes

DataLinq can control the ORM-visible object graph. It cannot rewrite every possible SQL result into a committed view without becoming a query engine bolted onto SQLite.

## Proposed Direction

The defensible design is hybrid:

1. Use committed SQLite behavior at the database level.
2. Use a DataLinq transaction-local overlay for own-write visibility.
3. Publish global cache changes only after commit.

For file-backed SQLite:

- remove `Cache=Shared` from normal file-backed connection strings unless a caller explicitly opts into it
- keep WAL enabled
- stop setting `PRAGMA read_uncommitted = true`
- open SQLite transactions at the provider-supported committed/serializable level
- set a deliberate busy timeout or retry policy where lock contention is expected

For named in-memory SQLite:

- keep `Mode=Memory;Cache=Shared` only because it is required for multiple connections to share one in-memory database
- do not treat shared in-memory SQLite as the concurrency truth source
- prefer temporary file-backed SQLite with WAL for isolation/concurrency compliance tests
- consider serializing shared in-memory mutation tests if they remain useful as a fast lane

## Transaction-Local Overlay

DataLinq already has transaction-specific row storage through `TableCache.TransactionRows`, but the lifecycle is too eager.

The cache/state model should split into two phases.

### Pending Phase

After a write succeeds inside an open transaction:

- store inserted or updated immutable rows in transaction-local row cache
- record deleted primary keys in transaction-local tombstones
- maintain transaction-local relation/index state needed for relation reads inside the transaction
- do not invalidate committed row cache
- do not invalidate committed relation caches
- do not notify outside relation subscribers

Conceptually:

```csharp
Provider.State.ApplyPendingChanges(changes, transaction);
```

Pending state belongs to the transaction. It should disappear on rollback or disposal.

### Commit Phase

After `DbTransaction.Commit()` succeeds:

- merge or invalidate committed row cache entries affected by the transaction
- clear committed relation/index cache entries affected by the transaction
- notify relation subscribers
- remove transaction-local overlay state
- mark the transaction committed

Conceptually:

```csharp
DatabaseAccess.Commit();
Provider.State.ApplyCommittedChanges(Changes);
Provider.State.RemoveTransactionFromCache(this);
```

The commit order matters. DataLinq should not publish committed cache effects before the database has actually committed.

### Rollback Phase

After rollback:

- roll back the database transaction
- discard the transaction-local overlay
- do not touch committed cache entries except for defensive cleanup of transaction-owned state
- mark the transaction rolled back

Rollback should be quiet. If rollback causes global cache invalidation, the model is still leaking pending state.

## Read Path Rules

For a `ReadOnlyAccess` or normal `database.Query()`:

- read only from committed/global row cache and committed database state
- ignore all transaction overlays
- use normal relation caches

For `transaction.Query()`:

- check transaction-local tombstones first
- check transaction-local rows next
- fall back to committed/global cache or the underlying database
- materialize missing committed rows into the transaction-local cache when needed for graph identity
- load relation collections through the transaction's data source so pending inserts/deletes are represented consistently

This preserves the core rule: a transaction sees itself; outsiders do not.

## Relation Cache Rules

Relations are where the current dirty-read behavior becomes most visible.

Existing immutable relation objects subscribe to table cache changes and clear their cached collection when a related table changes. That is fine for committed changes. It is wrong for pending transaction changes.

Pending transaction writes should invalidate only transaction-local relation views. Outside relation objects should keep their committed snapshot until commit. After commit, global notification clears those outside relation caches so the next read sees committed data.

This means tests like `Transaction_InsertRelations_PersistsAfterCommit` should eventually assert the same behavior for SQLite, MySQL, and MariaDB:

- outside `employee.salaries` remains empty before commit
- transaction-local `transactionEmployee.salaries` sees the inserted salary before commit
- outside `employee.salaries` sees the salary after commit

## External Transactions

Attached transactions need a narrower contract.

If DataLinq attaches to an existing `IDbTransaction` before DataLinq performs writes, the overlay model works normally.

If the caller performs raw writes before attaching the transaction, DataLinq cannot reconstruct those writes into a transaction-local overlay. It can still read them through the attached same-connection transaction, but it cannot prevent provider-specific behavior for outside raw reads if SQLite is configured for dirty reads.

Recommended contract:

- `AttachTransaction` supports DataLinq-managed writes with normal overlay semantics after attachment.
- raw writes performed before attachment are outside DataLinq's cache model
- docs and tests should distinguish "same attached transaction can read its own raw writes" from "outside DataLinq reads should not see attached raw writes"

## Test Strategy

Add or update TUnit coverage in the active test projects only.

Suggested test slices:

- SQLite provider unit tests proving `PRAGMA read_uncommitted` is no longer enabled by default
- connection-string tests proving file-backed SQLite no longer forces `Cache=Shared`
- compliance tests for transaction-local insert/update/delete visibility
- compliance tests for outside relation reads before and after commit
- rollback tests proving outside relation caches are not invalidated into pending state
- temporary file-backed SQLite WAL tests for concurrency/isolation behavior
- named in-memory SQLite tests documenting its narrower role and any required serialization

Do not use shared in-memory SQLite as the final arbiter for concurrent committed visibility. It is a useful fast lane, but it is the wrong substrate for this specific behavior.

## Implementation Slices

1. **Document and test current boundaries**
   - Add explicit failing or skipped tests that capture the desired cross-provider behavior.
   - Keep existing provider caveat docs honest until implementation lands.

2. **Split pending and committed cache application**
   - Introduce pending transaction cache APIs.
   - Stop global relation notifications during open transactions.
   - Preserve transaction graph identity through the pending overlay.

3. **Change SQLite default isolation**
   - Stop setting `PRAGMA read_uncommitted = true`.
   - Begin SQLite transactions with the provider-supported committed/serializable behavior.
   - Preserve same-transaction visibility through the overlay, not dirty reads.

4. **Remove shared cache from file-backed SQLite defaults**
   - Keep shared cache only for named in-memory databases where multiple connections must share one database.
   - Keep WAL for file-backed SQLite.

5. **Retune tests**
   - Move SQLite isolation assertions toward provider-independent committed visibility.
   - Keep provider-specific tests only where SQLite is genuinely different, such as snapshot behavior and single-writer limits.

6. **Update shipped docs**
   - Replace the current SQLite `ReadUncommitted` caveat with the final committed visibility behavior once implemented.
   - Keep a SQLite caveat for snapshot/serializable semantics versus MySQL/MariaDB `ReadCommitted`.

## Risks And Open Questions

- **Query overlay complexity:** Entity reads can be made correct. Arbitrary SQL cannot. The public contract must say where the guarantee applies.
- **Transaction snapshot differences:** SQLite committed/serializable behavior is not MySQL/MariaDB `ReadCommitted`. Some long-lived read transaction scenarios may still differ.
- **In-memory behavior:** Named in-memory SQLite relies on shared cache. It may need a different test lane or explicit serialization.
- **External raw writes:** DataLinq cannot model writes that bypass its mutation pipeline unless it reloads or invalidates broadly.
- **Busy handling:** Removing `ReadUncommitted` may expose lock timeouts that were previously hidden by dirty reads. File-backed WAL plus private cache should reduce this, but writer contention still needs realistic retry/timeout policy.
- **Cache invalidation order:** Publishing global cache changes before database commit is the central bug shape to avoid.

## Recommendation

Do this, but do it under the right name.

The target should be **DataLinq committed visibility semantics for SQLite**, not literal SQLite `ReadCommitted`. The implementation should stop using dirty reads as a feature, remove shared cache from file-backed SQLite, and make the transaction-local cache overlay responsible for same-transaction visibility.

Trying to keep SQLite in `ReadUncommitted` and hide the result inside the ORM would protect only the narrowest happy path. It would still leak through raw SQL and aggregate/projection paths. That is not a real alignment strategy.

## References

- [SQLite isolation](https://www.sqlite.org/isolation.html)
- [SQLite shared-cache mode](https://www.sqlite.org/sharedcache.html)
- [SQLite transactions](https://www.sqlite.org/lang_transaction.html)
- [SQLite WAL](https://www.sqlite.org/wal.html)
- [Microsoft.Data.Sqlite transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)
- [Microsoft.Data.Sqlite connection strings](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings)
- [Microsoft.Data.Sqlite in-memory databases](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/in-memory-databases)
