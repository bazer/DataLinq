> [!WARNING]
> This document is roadmap/design material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# SQLite Transaction Isolation Alignment

**Status:** Accepted.

**Last reviewed:** 2026-07-12.

**Target:** 0.9, before any memory mutation or cross-provider transaction-parity claim.

**0.9 execution plan:** [SQL Transaction and Mutable Lifecycle Implementation Plan](../roadmap-implementation/v0.9/SQL%20Transaction%20and%20Mutable%20Lifecycle%20Implementation%20Plan.md).

**Implementation progress:** `SQ-1` and `SQ-2` are complete. Every DataLinq-owned SQLite scalar, reader, non-query, and transaction connection resets `PRAGMA read_uncommitted = false`; owned transactions use deferred `IsolationLevel.Serializable`; attached connections retain caller policy; CLI and test-harness file defaults omit `Cache`; named memory retains shared cache; file/WAL evidence covers pending insert/update/delete, rollback, explicit shared-cache locking, and bounded writer contention; and the full SQLite compliance lane is green at 732/732. Only the remaining `SQ-3` contention/diagnostic matrix is open.

## Purpose

At the start of this plan, DataLinq opened SQLite transactions and non-transactional commands with `ReadUncommitted`, while MySQL and MariaDB used `ReadCommitted`. `SQ-1` has removed that owned-connection dirty-read policy.

That former difference leaked into user-visible behavior:

- SQLite can expose uncommitted writes to other DataLinq reads when shared-cache mode and `PRAGMA read_uncommitted = true` are both active.
- MySQL and MariaDB do not expose those pending writes under the current `ReadCommitted` transaction path.
- Some relation and transaction tests encoded SQLite-specific dirty visibility instead of provider-independent committed visibility; `SQ-1` retuned those boundaries.

The goal is to define a path where DataLinq-visible SQLite behavior aligns with MySQL/MariaDB for the important ORM surface: uncommitted writes should be visible inside the owning transaction and invisible outside it until commit.

## Current DataLinq Shape

The relevant implementation points are:

- `src/DataLinq.SQLite/SQLiteConnectionPolicy.cs`
  - centralizes DataLinq-owned committed visibility as `PRAGMA read_uncommitted = false`
  - defines owned transaction isolation as `IsolationLevel.Serializable`
- `src/DataLinq.SQLite/SQLiteDatabaseTransaction.cs`
  - applies the owned policy before beginning a deferred serializable transaction
  - deliberately leaves attached caller-owned connections unchanged
- `src/DataLinq.SQLite/SQLiteDbAccess.cs`
  - routes non-query, scalar, and reader connections through the same owned policy
- `src/DataLinq.SQLite/SQLiteProvider.cs`
  - enables WAL through `PRAGMA journal_mode = WAL`
- `src/DataLinq.CLI/CliConfigInit.cs`
  - generates file-backed SQLite connection strings without a `Cache` key
- `src/DataLinq.Testing/Environment/PodmanTestEnvironmentSettings.cs`
  - creates file-backed test connections with private/default cache
  - retains `Mode=Memory;Cache=Shared` for named in-memory test connections
- `src/DataLinq.MySql/Shared/SqlDatabaseTransaction.cs`
  - opens MySQL/MariaDB transactions with `IsolationLevel.ReadCommitted`
- `src/DataLinq/Mutation/Transaction.cs`
  - routes successful statement changes through `State.ApplyChanges(changes, this)` while the transaction is open
  - commits the provider transaction before applying the accumulated changes globally
  - removes transaction-local rows on rollback or disposal
- `src/DataLinq/Cache/TableCache.*`
  - already has transaction-scoped row storage and transaction-scoped notification paths
- active compliance tests
  - already cover transaction-local row visibility, outside relation stability before commit, and rollback preserving committed cache state

The earlier version of this plan treated pending-versus-committed cache application as missing. Current code has most of that overlay and publication order already, so `SQ-1` reused it instead of building a second cache overlay. Ordinary owned SQLite access no longer opts into dirty reads, and `SQ-2` removed DataLinq-generated shared cache from file-backed defaults without rewriting explicit caller settings.

## SQLite Semantics That Matter

SQLite is not MySQL with a smaller parser. The isolation model is structurally different.

SQLite's default isolation is serializable. Separate connections do not see each other's uncommitted changes unless both of these are true:

- the connections use shared cache
- the reader enables `PRAGMA read_uncommitted`

At the start of this plan, the SQLite test/provider shape did exactly that for file and named in-memory connections. `SQ-1` removed the reader-side prerequisite from DataLinq-owned paths; file-backed generated/test defaults still request shared cache until `SQ-2`.

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

DataLinq should not pretend it can sanitize arbitrary caller-owned SQLite connections that remain in `ReadUncommitted` mode. `SQ-1` controls DataLinq-owned connections; attached and external connections retain caller policy.

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
- preserve/configure the provider `DefaultTimeout` and add contention diagnostics/tests; 0.9 does not invent a retry policy

For named in-memory SQLite:

- keep `Mode=Memory;Cache=Shared` only because it is required for multiple connections to share one in-memory database
- do not treat shared in-memory SQLite as the concurrency truth source
- prefer temporary file-backed SQLite with WAL for isolation/concurrency compliance tests
- consider serializing shared in-memory mutation tests if they remain useful as a fast lane

## Transaction-Local Overlay

DataLinq already has transaction-specific row storage, scoped notifications, commit-time global publication, and rollback cleanup. These are the baseline to preserve and harden.

The cache/state model is still described in phases because those phases are the required contract. Implementation should reuse the existing machinery and fix only evidenced gaps.

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

Relations were where the former dirty-read behavior became most visible.

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
- once DataLinq has touched a mutable through an attached transaction, commit or rollback must be observed through the DataLinq wrapper; supported providers now detect an inactive original handle before later managed read/write/fallback/dispose, invalidate transaction-derived baselines, and evict caches conservatively without guessing whether the external action committed or rolled back

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

1. **Rebaseline the existing transaction/cache boundary**
   - Characterize `State.ApplyChanges(changes, transaction)`, transaction rows/tombstones, scoped relation notifications, provider-first commit, and rollback/disposal cleanup.
   - Add failures only for behavior that is still wrong; do not replace the working overlay with parallel APIs.
   - Coordinate mutable provenance, poisoned-transaction behavior, and publication gaps through the 0.9 transaction execution plan.

2. **Change SQLite default isolation — complete (`SQ-1`)**
   - DataLinq-owned paths set `PRAGMA read_uncommitted = false` through one shared policy.
   - Owned SQLite transactions begin with deferred `IsolationLevel.Serializable`.
   - Same-transaction visibility remains green through the provider transaction and overlay, not dirty reads.

3. **Remove shared cache from file-backed SQLite defaults — complete (`SQ-2`)**
   - CLI and Testing environment file defaults omit the `Cache` key and therefore use private/default cache.
   - Named in-memory databases retain shared cache where multiple connections must share one database.
   - Explicit caller-supplied cache settings are preserved rather than rewritten.

4. **Characterize contention without inventing retries**
   - Preserve/configure `DefaultTimeout` deliberately.
   - Add temporary file-backed WAL contention tests and actionable lock/busy diagnostics.
   - Defer automatic retry/backoff policy.

5. **Retune tests**
   - Move SQLite isolation assertions toward provider-independent committed visibility.
   - Keep provider-specific tests only where SQLite is genuinely different, such as snapshot behavior and single-writer limits.

6. **Update shipped docs — complete for `SQ-1` and `SQ-2`**
   - Shipped transaction, troubleshooting, backend, and roadmap pages describe committed visibility.
   - The SQLite caveat names snapshot/serializable and single-writer behavior instead of claiming MySQL/MariaDB `ReadCommitted` equivalence.
   - File-backed config examples omit `Cache`; named-memory and explicit caller settings remain documented exceptions.

## Risks And Open Questions

- **Query overlay complexity:** Entity reads can be made correct. Arbitrary SQL cannot. The public contract must say where the guarantee applies.
- **Transaction snapshot differences:** SQLite committed/serializable behavior is not MySQL/MariaDB `ReadCommitted`. Some long-lived read transaction scenarios may still differ.
- **In-memory behavior:** Named in-memory SQLite relies on shared cache. It may need a different test lane or explicit serialization.
- **External raw writes:** DataLinq cannot model writes that bypass its mutation pipeline unless it reloads or invalidates broadly.
- **Busy handling:** Removing `ReadUncommitted` may expose lock timeouts that were previously hidden by dirty reads. File-backed WAL plus private cache and an explicit `DefaultTimeout` are the 0.9 boundary; retry/backoff policy remains later work.
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
