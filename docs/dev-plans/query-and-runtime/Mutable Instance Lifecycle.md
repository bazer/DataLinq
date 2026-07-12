> [!WARNING]
> This document is roadmap or specification material. It describes planned behavior rather than current DataLinq behavior.

# Specification: Mutable Instance Lifecycle

**Status:** Accepted.
**Last reviewed:** 2026-07-12.
**Target:** 0.9 for existing SQL providers; required before memory mutation or shared committed-change batches.
**Goal:** Define consistent semantics for reusing the same mutable instance across `Insert`, `Update`, and `Save` calls, especially when explicit transactions, rollback, and cache baselines are involved.
**0.9 execution plan:** [SQL Transaction and Mutable Lifecycle Implementation Plan](../roadmap-implementation/v0.9/SQL%20Transaction%20and%20Mutable%20Lifecycle%20Implementation%20Plan.md).

**Implementation progress:** The bounded provenance/guard, touched-authority, confirmed-commit, known-committed recovery, mutation-poisoning, managed-wrapper rollback/open-disposal, managed provider-call uncertainty-recovery, active attached wrapper completion, and inactive attached-handle detection/recovery slices are implemented. A throwing managed `Commit()` becomes permanently `CommitOutcomeUnknown`; transaction-derived state is invalidated, managed reuse is rejected, and provider-wide committed rows/indices are conservatively evicted before recovery notifications. Active attached transactions promote or invalidate through wrapper-only commit/rollback across all providers. If the original attached handle is completed externally, wrapper commit and rollback retain their distinct unknown-outcome diagnostics, while first managed read/write/fallback/dispose records permanent `ExternalCompletionUnknown`; all of those paths invalidate transaction-derived state and recover caches without pretending to know whether the database committed. Provider-specific outcome evidence after a throwing commit, raw low-level escape prevention, arbitrary local-cleanup fault injection, full provider commit-fault evidence, and full concurrency remain open; this specification does not turn those gaps into shipped support.

## 1. Design Position

Mutable row objects should not be globally single-use command objects.

That would be too strict for DataLinq's current model. A mutable instance is a working copy with tracked changes and a baseline. Reusing it after a successful save is useful, already covered by tests, and fits the API:

```csharp
var employee = database.Query().Employees.Single(x => x.emp_no == 999796).Mutate();

employee.Save(x => x.birth_date = newBirthDate, database);
employee.Save(x => x.hire_date = newHireDate, database);
```

The second save should preserve the first persisted value and apply only the new change.

The important rule is narrower:

> A mutable instance may be reused only while its baseline is trustworthy.

If the baseline is committed, reuse is fine. If the baseline belongs to the same active transaction, reuse is fine. If the baseline belongs to another active transaction, a rolled-back transaction, or an unknown failed write, DataLinq should throw.

## 2. Why "Figure It Out" Is the Wrong Default

DataLinq should not silently reload the row and diff the mutable object against the database to guess what still needs updating.

That sounds helpful, but it creates fake certainty:

- without row versions or concurrency tokens, DataLinq cannot know which values came from the caller and which values came from someone else
- database triggers, defaults, generated columns, and external writes can change values outside the mutable object's knowledge
- comparing current database state to current mutable state does not reconstruct user intent
- retrying or reshaping writes across transaction boundaries can accidentally turn a rolled-back write into a later committed write

The honest model is:

- a mutable tracks explicit assignments since a known baseline
- `Update()` writes those tracked assignments
- after a successful write and trusted commit boundary, the returned immutable row becomes the new baseline
- if the baseline cannot be trusted, mutation APIs throw

## 3. Terminology

### Mutable working copy

A generated `Mutable{Model}` or runtime `Mutable<T>` instance that exposes row properties for assignment.

### Baseline

The row snapshot the mutable currently compares against. Assignments after that baseline become tracked changes.

### Committed baseline

A baseline that reflects committed database state and can be safely reused across later implicit or explicit transactions.

### Transaction-local baseline

A baseline created by a successful `Insert`, `Update`, or `Save` inside an explicit transaction before that transaction commits.

It is real inside that transaction. It is not yet committed truth.

### Invalid baseline

A baseline that must not be used for later writes. The common causes are confirmed rollback, rollback with an outcome that could not be confirmed, disposal of an open transaction, a failed mutation path, or a known-committed database change whose local state finalization failed.

## 4. Required Semantics

### 4.1. New mutable plus `Insert`

`Insert(newMutable)` inserts the row and turns the mutable into an existing row working copy.

After success:

- `IsNew()` is false
- generated primary keys are hydrated
- tracked changes are cleared
- the returned immutable instance is the authoritative row
- a second `Insert()` with the same mutable throws because the object is no longer new

### 4.2. New mutable plus `Save`

`Save(newMutable)` is equivalent to insert while the mutable is new.

After the insert succeeds, later `Save()` calls on the same mutable are update attempts.

### 4.3. Existing mutable plus `Update`

`Update(existingMutable)` writes only tracked changes.

If there are no tracked changes, `Update()` should remain a no-op that returns the current immutable row instead of issuing a meaningless write.

### 4.4. Existing mutable plus repeated `Save`

Repeated `Save()` on the same mutable is allowed when each call starts from a trusted baseline.

Example:

```csharp
var mutable = employee.Mutate();

database.Save(mutable, x => x.birth_date = newBirthDate);
database.Save(mutable, x => x.hire_date = newHireDate);
```

Expected behavior:

- first call updates `birth_date`
- mutable baseline advances to the returned row
- second call updates `hire_date`
- final row contains both changes

This is the behavior users will reasonably expect from a working copy API.

### 4.5. Same mutable inside the same explicit transaction

Reusing a mutable inside the same active explicit transaction is allowed.

Example:

```csharp
using var transaction = database.Transaction();

var mutable = transaction.Query().Employees.Single(x => x.emp_no == empNo).Mutate();

transaction.Save(mutable, x => x.birth_date = newBirthDate);
transaction.Save(mutable, x => x.hire_date = newHireDate);

transaction.Commit();
```

The first write creates a transaction-local baseline. The second write can safely use that baseline because it executes in the same transaction and sees the same transaction-local state.

### 4.6. Same mutable in another transaction while the first transaction is open

This should throw.

Example:

```csharp
using var transactionA = database.Transaction();
using var transactionB = database.Transaction();

transactionA.Save(mutable, x => x.birth_date = newBirthDate);
transactionB.Save(mutable, x => x.hire_date = newHireDate); // throw
```

The mutable is bound to transaction A's uncommitted baseline. Transaction B must not treat that baseline as committed truth.

Implicit writes should follow the same rule. If the mutable is bound to an active explicit transaction, an implicit `database.Save(mutable)` should throw.

### 4.7. Same mutable after commit

After the transaction commits, the transaction-local baseline is promoted to a committed baseline.

At that point, reusing the same mutable in a later transaction is allowed.

Example:

```csharp
using (var transaction = database.Transaction())
{
    transaction.Save(mutable, x => x.birth_date = newBirthDate);
    transaction.Commit();
}

database.Save(mutable, x => x.hire_date = newHireDate); // allowed
```

### 4.8. Same mutable after rollback

After a managed rollback attempt, the mutable must not be reused for writes.

This is the dangerous case. A mutable may have been reset to transaction-local values after `Update()` or `Insert()` succeeded, but those values were never committed. If DataLinq then allows `Save()` with no tracked changes, or with only later changes, it can accidentally preserve rolled-back state.

Required behavior:

- mark mutables touched by the transaction as invalid with `RolledBack` only when provider status reached rolled back, otherwise with `RollbackOutcomeUnknown`
- future `Insert`, `Update`, or `Save` calls on those mutables throw
- the exception should tell the user to fetch or create a fresh mutable from committed state
- if provider rollback throws while the provider remains open, block every managed transaction operation except disposal
- discard exact transaction rows and subscriptions without clearing committed/global cache or relation state
- defer wrapper `RolledBack` status publication until token/mutable/registry/cache finalization is complete

Suggested exception text:

```text
This mutable instance was written inside transaction 42, but that transaction was rolled back. Create a fresh mutable instance from a committed row before writing again.
```

### 4.9. Disposal of an open transaction

Directly disposing an open or poisoned managed wrapper uses the provider rollback/disposal path but records a distinct lifecycle reason.

Mutable lifecycle should apply the same conservative trust boundary as rollback:

- provider disposal is attempted before local finalization
- exact transaction rows and subscriptions are discarded without clearing committed/global state
- newly terminalized touched mutables and the ownership token become invalid with `OpenTransactionDisposed`; an earlier `MutationFailed` or other terminal token outcome is preserved
- later write attempts throw
- provider cleanup failure remains the primary exception while DataLinq finalization context and secondary failures remain inspectable

### 4.10. Failed mutation execution

If a mutation command throws before DataLinq has a confirmed hydrated row, the mutable baseline must not advance.

The 0.9 rule is deliberately strict:

- an explicit write transaction that sees a mutation exception becomes poisoned
- a poisoned transaction cannot accept more writes or commit; only rollback or disposal is legal
- a failed `StateChange` must not remain eligible for committed cache publication
- touched transaction-bound mutables are invalidated when the poisoned transaction rolls back or is disposed
- an implicit write failure does not advance the mutable baseline or silently clear the user's tracked changes
- commit failure also leaves the transaction uncommittable and its touched mutable baselines untrusted

This is stricter than guessing from provider-specific exception types, and that is intentional. Retrying a poisoned unit of work can be designed later if evidence justifies it.

### 4.11. Delete

Although this plan focuses on `Insert`, `Update`, and `Save`, delete must follow the same lifecycle rules.

For a mutable passed to `Delete()`:

- after committed delete, the mutable is deleted and future mutation calls throw
- after transaction-local delete, the mutable is bound to that transaction
- after rollback, the mutable should be invalid rather than pretending it is a clean committed row

This avoids the same class of rollback lies.

### 4.12. Primary key mutation

Primary key updates should be rejected for ordinary mutable row updates.

The current conceptual model is provider-key identity plus immutable read rows. Allowing a mutable to change its primary key makes update targeting, cache invalidation, equality, and hash-code behavior much harder to reason about.

If DataLinq later supports primary-key migration, it should be a dedicated API with explicit cache and relation semantics. It should not fall out of ordinary property assignment.

## 5. State Model

Runtime mutable instances need explicit lifecycle/provenance state. The implemented bounded model separates baseline kind, row kind, invalidation reason, and transaction-owner outcome instead of multiplying combined states:

- baseline kinds: new/no baseline, committed, transaction-local, invalid
- row kinds: new, existing, or deleted
- invalidation reasons: `RolledBack`, `RollbackOutcomeUnknown`, `OpenTransactionDisposed`, `MutationFailed`, `CommitOutcomeUnknown`, `ExternalCompletionUnknown`, and `CommittedStateFinalizationFailed`
- transaction-owner outcomes: unresolved, committed, rolled back, rollback outcome unknown, open transaction disposed, commit outcome unknown, external completion unknown, or committed-state finalization failed

The exact names are not important. The important part is that the mutable knows:

- whether it is new or existing
- whether its baseline is committed or transaction-local
- which provider owns the baseline
- which transaction owns a transaction-local baseline
- whether it has been invalidated, why it is invalid, and whether deletion is transaction-local or committed

## 6. Transaction Responsibilities

Transactions should track mutable instances they touch.

On successful mutation execution:

1. execute SQL
2. hydrate the row inside the same transaction
3. reset the mutable to the hydrated transaction-local row
4. bind the mutable to the transaction-local baseline

On commit:

1. commit the provider transaction
2. merge transaction-local cache state into global cache
3. promote touched mutable baselines to committed
4. remove transaction-local ownership

On explicit managed rollback:

1. attempt provider rollback
2. classify the owner as `RolledBack` only if provider status reached rolled back, otherwise `RollbackOutcomeUnknown`
3. discard exact transaction rows and subscriptions without clearing committed/global state
4. invalidate touched mutable baselines without overwriting an earlier `MutationFailed`
5. clear the touched registry and defer wrapper `RolledBack` observation until finalization is complete
6. if the provider remains open after failure, permit only disposal through the managed wrapper

On direct disposal of an open or poisoned managed wrapper:

1. attempt provider rollback/resource disposal
2. classify unresolved ownership as `OpenTransactionDisposed`
3. perform the same exact transaction cleanup, invalidation, and registry clearing even when provider cleanup throws
4. preserve an earlier terminal token/mutable outcome rather than reclassifying it

Do not promote mutable baselines before the commit completes.

For attached external transactions, once DataLinq writes through the wrapper and binds a mutable to it, completion must also be observed through the DataLinq wrapper. If the original handle is instead completed externally, supported providers expose it as inactive. The wrapper then rejects continued managed use, permanently invalidates touched/token-derived baselines, removes scoped state, and evicts provider-wide committed caches because the external action may have committed. Wrapper `Commit()` and `Rollback()` preserve their operation-specific unknown outcomes; first managed read, write, fallback, or disposal records `ExternalCompletionUnknown`. None of those classifications guesses the database result.

## 7. API Guard Rules

Before `Insert`, `Update`, `Save`, or `Delete`, DataLinq should validate:

- the transaction is writable
- the mutable belongs to the same provider/database instance
- the mutable is not deleted
- the mutable is not invalid
- the mutable is not bound to a different active transaction
- `Insert` receives a new mutable
- `Update` receives an existing mutable
- ordinary update does not include primary-key changes

The read-only transaction guard should be covered by tests. Write APIs must reject `TransactionType.ReadOnly`.

## 8. Public Documentation Requirements

This behavior must be clearly specified in public documentation when implemented.

At minimum, update:

- `docs/Caching and Mutation.md`
- `docs/Transactions.md`
- `docs/Troubleshooting.md`
- generated XML documentation for `Insert`, `Update`, `Save`, `Delete`, `Mutate`, and `Reset`

The public docs should state:

- mutable objects are reusable working copies, not globally single-use command objects
- the returned immutable instance after an implicit write is the committed authoritative row
- the returned immutable instance after an explicit transaction write is authoritative only inside that transaction until commit
- repeated `Save()` is allowed after successful commit
- no-op `Update()` intentionally does not write
- a mutable written in an active explicit transaction cannot be reused outside that transaction
- mutables touched by rolled-back transactions cannot be reused for writes
- users should create a fresh mutable from committed state after rollback
- ordinary primary-key mutation is unsupported

This must not be left as tribal knowledge. These lifecycle rules are too important for cache correctness and transaction correctness.

## 9. Testing Requirements

Add compliance tests for SQLite, MySQL, and MariaDB covering:

- repeated implicit `Save()` on the same mutable preserves the first saved value and applies the second change
- `Insert()` followed by `Insert()` on the same mutable throws
- `Save(newMutable)` followed by `Save(existingMutable)` updates
- `Update()` with no tracked changes does not issue a write and returns an equivalent row
- repeated `Save()` inside the same explicit transaction works
- mutable bound to one open transaction cannot be saved through another transaction
- mutable bound to an open explicit transaction cannot be saved through an implicit transaction
- mutable touched by a committed transaction can be reused later
- mutable touched by a confirmed rolled-back transaction throws on later write with the `RolledBack` reason
- rollback failure while provider status remains open records `RollbackOutcomeUnknown`, gates managed operations except disposal, and still makes the mutable unusable
- mutable touched by direct disposal of an open transaction throws on later write with the `OpenTransactionDisposed` reason
- provider rollback/disposal failure cannot prevent token/mutable/registry/scoped-cache finalization or replace the exact primary provider exception
- exact transaction notification subscriptions are discarded without invocation while unrelated/committed subscriptions remain
- mutable passed to committed delete rejects later mutation
- mutable passed to rolled-back delete rejects later write or is explicitly invalidated
- ordinary primary-key assignment followed by `Update()` throws
- write APIs reject `TransactionType.ReadOnly`

## 10. Implementation Checklist

- [x] Add the bounded lifecycle/provenance substrate to `Mutable<T>`.
- [x] Track exact provider identity on mutable baselines.
- [x] Track exact transaction-token ownership for transaction-local baselines.
- [x] Track touched lifecycle mutables by reference identity in `Transaction`.
- [x] Promote touched mutable baselines only after confirmed provider commit and local finalization.
- [x] Invalidate touched mutable baselines after managed-wrapper rollback or open-transaction disposal, including deterministic provider cleanup failures.
- [x] Guard supported managed row-mutation routes before executing SQL.
- [x] Reject ordinary primary-key updates.
- [x] Keep failed-write assignments inspectable while preventing an untrusted baseline from becoming writable.
- [x] Recover conservatively from a throwing managed provider `Commit()` with permanent `CommitOutcomeUnknown`, managed-operation rejection, touched/scoped cleanup, provider-wide committed-cache eviction, and exact-cause-preserving recovery-failure context.
- [x] Prove active attached wrapper-only commit promotion/reuse and rollback invalidation across every provider, and reject wrapper commit after external completion into conservative unknown-outcome cache recovery.
- [x] Detect an inactive attached original handle before managed read/write/fallback/dispose, classify it as `ExternalCompletionUnknown`, recover provider-wide caches, block later managed work except disposal, and prove actual external commit/rollback rematerialization across every provider.
- [x] Recover provider-wide caches when wrapper `Rollback()` first observes an externally completed attached handle, while retaining `RollbackOutcomeUnknown` instead of guessing the external result.
- [ ] Complete the remaining provider/terminal matrix. Owned and active-attached wrapper completion plus bounded managed uncertainty recovery are covered; provider-specific database-outcome evidence after a throwing commit, raw low-level escapes, arbitrary local-cleanup fault injection, full provider commit-fault evidence, and full concurrency remain open.
- [ ] Update public shipped docs after the full supported lifecycle/provider boundary, including attached/external policy, is ready to claim.
