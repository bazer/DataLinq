> [!WARNING]
> This document is roadmap execution evidence for the DataLinq 0.9 development line. It records unsafe or incomplete current behavior and must not be read as shipped product documentation.

# Mutation Lifecycle Expected-Failure And Ownership Matrix

**Status:** W1 audit complete; implementation expectations assigned to W3 workstreams.

**Target release:** 0.9.

**Audited:** 2026-07-10.

## Purpose

This is the explicit owner matrix for the mutation-lifecycle cases that the first W1 characterization slice deliberately did not turn green. It separates three different facts that are easy to blur together:

1. behavior that is already safe and must remain green
2. behavior that happens to succeed today without the lifecycle state needed to make it trustworthy
3. behavior that is observably unsafe and must be represented as a future-behavior test until its owning workstream lands

The matrix covers cross-provider and cross-transaction reuse, post-commit reuse, rollback and disposal, deletion, primary-key mutation, failed writes, read-only transactions, and attached external transactions. Test IDs below are durable proposed IDs for the W3 implementation suite; they are not names of tests that exist today.

## Audit Boundary

The audit inspected the active runtime under `src/DataLinq`, the SQLite and MySQL/MariaDB transaction adapters, and the active TUnit projects under `src/DataLinq.Tests.Unit`, `src/DataLinq.Tests.Compliance`, and `src/DataLinq.Tests.MySql`.

The evidence is intentionally static. This matrix does not add behavioral tests that approve execution of an unsafe command. The parallel TX-0 provider-lifecycle characterization does freeze current commit/rollback/disposal exception ordering under explicitly named `CurrentBehavior` tests; those tests document a gap rather than define the target contract. Mutation command-count and poison-state assertions should land with the owning implementation slice so they require the accepted 0.9 behavior from their first committed form.

Status terms used below:

- **Green control:** the user-visible happy path is already covered, but its lifecycle implementation still needs to move under explicit provenance.
- **Amber:** part of the desired result occurs, but timing, ownership, failure handling, or diagnostics are not trustworthy.
- **Red:** the current path has no required guard or terminal transition and can reach provider SQL or publish invalid state.

## Current-Code Evidence Index

| Evidence | Exact current location | What it proves |
| --- | --- | --- |
| E1 | `src/DataLinq/Instances/Mutable.cs:20-30` | A mutable stores an immutable row, `isNew`, `isDeleted`, and changed row data. There is no provider owner, transaction owner, invalid state, or invalidation reason. |
| E2 | `src/DataLinq/Instances/Mutable.cs:130-163` | Construction and `Reset(T)` copy row data and metadata but discard the immutable's data source. A successful statement therefore resets values without preserving the provider/transaction provenance of the hydrated row. |
| E3 | `src/DataLinq/Instances/Immutable.cs:128-133` | The source immutable does know its `IDataSourceAccess` and can move from a completed transaction to read-only access. The provenance is available before `Mutable(T)` discards it. |
| E4 | `src/DataLinq/Instances/Mutable.cs:37-66`, `src/DataLinq/Instances/Mutable.cs:86-113` | Primary-key setters are accepted and merely invalidate the cached key; the next `PrimaryKeys()` call adopts the assigned key. |
| E5 | `src/DataLinq/Instances/Mutable.cs:143-165` | Public reset changes the row baseline without lifecycle validation, while deletion is one irreversible Boolean set by `SetDeleted()`. |
| E6 | `src/DataLinq/Mutation/Transaction.cs:150-166`, `src/DataLinq/Mutation/Transaction.cs:187-207` | Insert/update validate only new-versus-existing shape, execute immediately, hydrate from the target provider's cache, and call `model.Reset(immutable)` before provider commit. |
| E7 | `src/DataLinq/Mutation/Transaction.cs:230-273`, `src/DataLinq/Database.cs:177-240` | `Save()` selects insert/update from `IsNew()`, and implicit database writes create a fresh transaction. Neither path checks mutable ownership. |
| E8 | `src/DataLinq/Mutation/Transaction.cs:279-290`, `src/DataLinq/Mutation/StateChange.cs:66-73` | Mutable delete sets `isDeleted` immediately after the statement, before commit. A later `StateChange` rejects the Boolean, but there is no transaction-local delete or invalid-delete state. |
| E9 | `src/DataLinq/Mutation/Transaction.cs:324-339` | A candidate is appended to public `Changes` before command execution. There is no catch, removal, or transaction poisoning if execution or transaction-local cache application throws. |
| E10 | `src/DataLinq/Mutation/StateChange.cs:112-164` | Mutation exceptions are recorded in telemetry and rethrown. The transaction and mutable are not transitioned to failed/invalid states. |
| E11 | `src/DataLinq/Mutation/StateChange.cs:207-223` | Update uses the mutable's current primary-key value both in `WHERE` and in the generated `SET` list; delete also uses the current value. It does not use the captured original primary key as the row selector. |
| E12 | `src/DataLinq/Mutation/Transaction.cs:344-352` | Provider commit precedes global cache publication, which is good. Publication and transaction-cache removal are sequential and have no failure partition or mutable promotion/invalidation. |
| E13 | `src/DataLinq/Mutation/Transaction.cs:357-392` | Rollback removes cache state only after provider rollback succeeds; dispose removes cache state before provider disposal. Neither path tracks or invalidates touched mutables. |
| E14 | `src/DataLinq/Mutation/Transaction.cs:373-383` | The only central validity guard explicitly returns for `TransactionType.ReadOnly`; otherwise it checks only committed/rolled-back provider status. |
| E15 | `src/DataLinq/Cache/TableCache.RowLookup.cs:219-229`, `src/DataLinq/Cache/TableCache.Invalidation.cs:25-34` | Read-only transactions do not receive transaction row caches, even though mutation application still treats every non-null transaction as transaction-local. Permitting writes here produces an internally inconsistent cache path. |
| E16 | `src/DataLinq/Mutation/Transaction.cs:134-142`, `src/DataLinq/Mutation/Transaction.cs:344-352` | An attached provider transaction uses the same wrapper commit/publication path as an owned transaction, but the wrapper records no ownership mode or external-completion observation. |
| E17 | `src/DataLinq.SQLite/SQLiteDatabaseTransaction.cs:139-153`, `src/DataLinq.MySql/Shared/SqlDatabaseTransaction.cs:130-144` | Attached adapters skip the underlying commit when its connection is no longer open, then still set DataLinq status to `Committed`. The outer wrapper can consequently publish `Changes` after an external commit or rollback without knowing which occurred. |
| E18 | `src/DataLinq.SQLite/SQLiteDatabaseTransaction.cs:161-203`, `src/DataLinq.MySql/Shared/SqlDatabaseTransaction.cs:152-193` | Provider rollback/disposal closes open attached or owned transactions, but the provider layer has no touched-mutable registry to invalidate. |
| E19 | `src/DataLinq/Mutation/StateChange.cs:52-83` | `StateChange` captures the current key and changes after only view/type/new/deleted checks. There is no provider, transaction, read-only, invalid-baseline, or primary-key-change preflight. |

## Existing Test Evidence And Its Limits

| Current test evidence | Exact location | What is green | What is not proved |
| --- | --- | --- | --- |
| Repeated implicit save and explicit same-transaction/post-commit save | `src/DataLinq.Tests.Compliance/Transactions/EmployeesTransactionLifecycleTests.cs:570-638` | Earlier values survive repeated `Save()` calls; the same mutable can currently be used again after commit against the same database. | Provider ownership, transaction ownership, promotion timing, rollback invalidation, and failure partitions. Current success depends on statement-time `Reset(T)`, not commit-time promotion. |
| Explicit rollback restores committed database values | `src/DataLinq.Tests.Compliance/Transactions/EmployeesTransactionLifecycleTests.cs:293-326` | The provider rollback restores the database row. | Reusing the touched mutable is never attempted, so invalidation is not covered. |
| Rollback cache preservation and transaction-row cleanup | `src/DataLinq.Tests.Compliance/State/EmployeesCacheInvalidationCharacterizationTests.cs:197-240` | Committed row identity remains cached and transaction rows disappear. | The touched mutable is not written again after rollback. |
| Open transaction disposal cleanup | `src/DataLinq.Tests.Compliance/State/EmployeesCacheInvalidationCharacterizationTests.cs:242-284` | Provider disposal rolls back and transaction rows disappear. | The touched mutable is not invalidated or tested after disposal. |
| Committed delete cache invalidation | `src/DataLinq.Tests.Compliance/State/EmployeesCacheInvalidationCharacterizationTests.cs:64-91` | A committed delete removes the cached immutable row. | The test deletes an immutable, not a mutable whose lifecycle can become transaction-local, deleted, or invalid. |
| Attached write followed by external commit and wrapper commit | `src/DataLinq.Tests.Compliance/Transactions/EmployeesTransactionLifecycleTests.cs:61-96` | The current external-first sequence happens to persist and lets the wrapper publish. | This is the sequence 0.9 explicitly makes unsupported after DataLinq touches a mutable. It does not prove wrapper-owned commit, rollback invalidation, or external-close detection. |
| Deterministic provider commit/rollback/disposal exceptions | `src/DataLinq.Tests.Unit/Core/TransactionFaultInjectionCharacterizationTests.cs:15-152` | Exact current exception propagation, status, and transaction-cache ordering are frozen for nine terminal-path partitions. In particular, commit/rollback failures retain cache state while direct wrapper-`Dispose` faults occur after outer cache cleanup. | The fixture deliberately has no touched mutable, mutation statement candidate, poisoned state, or committed-cache publication fault. Tests named `CurrentBehavior` are evidence for `TX-3`/`TX-4`, not accepted 0.9 outcomes. |

Repository search found no active lifecycle tests for cross-provider reuse, cross-transaction rejection, primary-key mutation rejection, mutation-statement poisoning, read-only write rejection, mutable deletion terminal states, attached wrapper rollback, or external rollback/closure detection. Their absence is intentional in W1: a test that approves today's unsafe side effect would become a compatibility burden.

## Expected-Failure And Owner Matrix

### Provenance And Reuse

| Status | Proposed test ID | Entry condition | Required 0.9 exit condition | Current behavior and evidence | Owner | Why this is not green today |
| --- | --- | --- | --- | --- | --- | --- |
| **Red** | `ML2-XP-01` | Materialize an immutable through provider instance `P1`, create and dirty mutable `M`, then call `Update`/`Save` through distinct provider instance `P2` with a compatible model shape. | Throw a DataLinq-owned cross-provider diagnostic before command creation/execution. `P2.Changes` and both provider caches remain unchanged; `M` retains its assignments and its `P1` committed baseline. | `M` discards the immutable data source (E2/E3), and write preflight has no provider comparison (E14/E19). Command generation and cache hydration use the target transaction's provider (E6/E9). Depending on metadata identity and database contents, current execution may mutate `P2`, fail late in cache lookup, or fail in the provider; none is a valid contract. | `ML-1` provenance, `ML-2` guard, provider matrix `TX-6` | There is no owner to compare and no focused test. Most importantly, the current path can reach SQL before discovering any mismatch. |
| **Red** | `ML2-XT-01` | `M` has completed a successful insert/update in still-open transaction `T1`; dirty it again and pass it to distinct explicit transaction `T2` on the same provider. | Reject before command creation. `M` remains transaction-local to `T1`; `T2.Changes` stays empty; neither transaction cache changes. `T1` may still commit or roll back normally. | The first write resets `M` to a transaction row but records no owner (E1/E2/E6). `T2` checks only its own provider status (E14), then can issue SQL and reset `M` again (E6/E9). Provider locks or row visibility may make the observed failure vary. | `ML-1`, `ML-2`, `TX-1`; provider matrix `TX-6` | There is no transaction provenance or touched registry. A provider-specific lock/error is not an acceptable substitute for deterministic preflight rejection. |
| **Red** | `ML2-XT-02` | `M` is transaction-local to open explicit `T1`; call an implicit `Database.Save(M)`/`Update(M)` on the same provider. | Reject before the implicit transaction creates or executes a command. `M` remains owned by `T1`; the short-lived transaction publishes nothing. | Implicit writes always create another transaction (E7), and no ownership check distinguishes that new transaction from `T1` (E14/E19). | `ML-2`, `TX-1`; provider matrix `TX-6` | The convenience API currently provides a direct escape from transaction ownership. No test covers it. |
| **Green control / amber implementation** | `TX2-PC-01` | Successfully write `M` in explicit transaction `T`, commit `T` through DataLinq, then dirty and save `M` through the same provider. | Commit promotes `M` from transaction-local to clean committed state only after provider commit and committed-cache publication. Later same-provider save succeeds from that promoted baseline. | The user-visible path passes in `EmployeesTransactionLifecycleTests.cs:597-637`, but `M` is reset after each statement (E6) and `Commit()` never touches it (E12). Success therefore does not prove promotion or ordering. | `TX-2`, provider regression `TX-6` | Keep the existing test green, but do not mark `TX-2` green until the test can observe transaction-local ownership before commit, committed ownership afterward, and invalidation on the adjacent failure partitions. |

### Rollback, Disposal, And Delete

| Status | Proposed test ID | Entry condition | Required 0.9 exit condition | Current behavior and evidence | Owner | Why this is not green today |
| --- | --- | --- | --- | --- | --- | --- |
| **Red** | `TX3-RB-01-I/U/D` | Successfully insert, update, or mutable-delete through explicit transaction `T`, then call `T.Rollback()`. Attempt a later implicit and explicit write with each touched mutable. | Provider rollback occurs; all transaction rows/notifications are removed; every touched mutable becomes invalid with `RolledBack`; later writes reject before SQL and direct the caller to materialize a fresh committed row. Committed cache state remains intact. | Provider/database and cache rollback are covered, but mutable reuse is not. Runtime rollback only invokes the provider and removes transaction cache state (E13); insert/update mutables retain statement-time transaction values (E6), while delete retains a bare deleted Boolean (E8). | `TX-3`, with provenance from `ML-1` and registry from `TX-1`; provider matrix `TX-6` | Current values can look clean even though they were never committed. Update/insert mutables have no invalid state and may be reused. Delete rejects for the wrong terminal reason and cannot explain rollback. |
| **Red** | `TX3-DISP-01-I/U/D` | Successfully touch mutable(s) in open transaction `T`, then dispose `T` without commit or explicit rollback; repeat with a provider-dispose fault in the core fake lane. | Treat open disposal as rollback: invalidate all touched mutables with `OpenTransactionDisposed`, remove local cache state in a finally-safe path, and retain the provider exception if disposal fails. Later writes reject before SQL. | Provider disposal rolls back an open transaction (E18), and current cache cleanup is covered. The outer wrapper removes cache state before provider disposal but has no mutable registry (E13). | `TX-3`, core fault test and provider matrix `TX-6` | Current tests stop after database/cache assertions. No mutable terminal transition exists, and provider-dispose failure cannot be tied to mutable invalidation. |
| **Amber** | `TX2-DEL-01` | Pass an existing mutable `M` to `Delete()` in transaction `T`, observe it before commit, commit through DataLinq, then attempt `Insert`, `Update`, `Save`, and another `Delete` with `M`. | After the statement, represent a transaction-local delete owned by `T`; after successful commit, promote it to a `Deleted` terminal state owned by the provider. Every later mutation rejects before SQL with an actionable deleted-row diagnostic. | `Delete()` sets one Boolean immediately after statement execution (E8). `StateChange` rejects later operations when it sees that Boolean, so a narrow later-write guard exists, but there is no transaction-local state or commit promotion. Existing delete tests use immutables. | `TX-1` delete state, `TX-2` promotion, `ML-2` guard; provider matrix `TX-6` | The protective Boolean is set at the wrong lifecycle granularity and has no provenance. It cannot distinguish committed deletion from a delete that will roll back or whose commit outcome is unknown. |
| **Amber/red semantics** | `TX3-DEL-02` | Mutable `M` is successfully deleted in open `T`; roll back or dispose `T`; then attempt to reset and write `M`. | `M` is invalid with the rollback/disposal reason, not restored heuristically and not labeled as a committed delete. `Reset()` cannot resurrect it; every later write rejects before SQL. | Current `isDeleted` remains true because neither rollback nor reset clears or reclassifies it (E5/E8/E13). Later `StateChange` is likely rejected, but only as "deleted," which is factually wrong after the database delete rolled back. | `TX-3`, `ML-1`, `ML-2`; provider matrix `TX-6` | Rejection alone is insufficient: recovery guidance and outcome semantics are wrong, and no mutable-delete rollback test exists. |

### Preflight And Write Failures

| Status | Proposed test ID | Entry condition | Required 0.9 exit condition | Current behavior and evidence | Owner | Why this is not green today |
| --- | --- | --- | --- | --- | --- | --- |
| **Red, data-corruption risk** | `ML2-PK-01` | Materialize existing row with primary key `K1`, assign ordinary mutable primary key `K2` (with and without another field change), and call `Update`/`Save`. Include a fixture where `K2` already identifies another row. | Reject before command creation/execution. The mutable remains dirty against its `K1` baseline, transaction `Changes` stays empty, and neither cache nor row changes. | Primary-key assignment is accepted and replaces the current key (E4). Update builds `WHERE pk = K2` and also `SET pk = K2` (E11). If `K2` exists, the command can update the wrong row using values derived from the `K1` mutable; if it does not, the failure is late and provider/cache dependent. | `ML-2`, provider matrix `TX-6` | This is not merely unsupported syntax; it can select the wrong row. No current test or guard prevents provider execution. |
| **Red** | `TX4-FW-01` | In explicit transaction `T`, execute an insert/update/delete that deterministically fails at the provider statement (for example, a unique or foreign-key constraint). | The failed candidate never enters successful `Changes`; its mutable becomes invalid with `MutationFailed`; `T` becomes poisoned; reads, writes, and `Commit()` reject; only rollback/dispose are legal; nothing is globally published. | The candidate enters `Changes` before execution (E9). The provider exception is rethrown without transaction or mutable transition (E10), and current validity checks do not know a poisoned state (E14). | `TX-1` successful-change recording, `TX-4` poisoning, core fault lane plus provider matrix `TX-6` | Today a failed candidate remains eligible for `Commit()` and global cache publication. No active test exercises this path. |
| **Red** | `TX4-FW-02` | Execute one successful write in `T`, then a second write that fails. Attempt `T.Commit()`. | The first mutable and failed mutable are invalidated; commit is rejected before provider commit; rollback/dispose removes all pending state; the earlier successful statement is not committed or published. | Earlier and failed candidates coexist in `Changes` (E9). No poison guard stops `Commit()` from committing provider state and applying the entire list globally (E12/E14). Provider-specific statement-abort behavior can make the result differ, which is precisely why DataLinq must own the rule. | `TX-4`, with `TX-1`; core fault lane and provider matrix `TX-6` | Current behavior can commit the earlier statement and publish cache effects for the failed candidate. A provider happening to abort its whole transaction is not a cross-provider contract. |
| **Red** | `TX4-HYDRATE-01` | Make provider execution succeed, then fault generated-value hydration, row reload, or transaction-local cache application before the statement is finalized. | No publishable successful change; transaction poisoned; touched/current mutable invalid; only rollback/dispose legal; no global cache publication. User assignments remain inspectable. | Candidate recording precedes all execution/cache work (E9). Insert/update reload and reset occur after statement execution (E6), and there is no enclosing lifecycle failure partition. A `ModelLoadFailureException` or cache exception leaves the candidate and transaction reusable. | `TX-1`, `TX-4`; core fault-injection lane `TX-6` | Statement success alone is not a completed DataLinq mutation. The current code has no test seam or terminal state for the later stages. |
| **Red** | `TX4-COMMIT-01` | Successful pending writes exist; provider commit throws or has unknown outcome. Attempt another write or commit, then clean up. | Do not publish globally; invalidate all touched mutables with `CommitOutcomeUnknown`; reject further writes/commit; allow rollback where possible or dispose; require fresh reads. | Global publication correctly follows provider commit and is skipped when that call throws (E12), but mutables were already reset to transaction-local values (E6), no poisoned/uncertain state exists (E14), and cleanup/invalidation is absent (E13). | `TX-4`; core provider-lifecycle fault lane `TX-6` | Provider-first ordering is only half the contract. Current mutables still look reusable after an outcome-uncertain commit. |
| **Red** | `TX2-PUBLISH-01` | Provider commit succeeds, then committed/global cache publication throws. Attempt to read/reuse touched mutable(s). | Report that database commit succeeded; remove transaction-local state; conservatively clear affected committed cache state; invalidate touched mutables; never report rollback; require a fresh committed read. | `Commit()` calls global `ApplyChanges` and only afterward removes transaction rows (E12). If publication throws, removal is skipped until disposal, no conservative committed-cache cleanup is guaranteed, and statement-time-reset mutables remain apparently trustworthy. | `TX-2` publication partition, `TX-4` diagnostics; core fault lane `TX-6` | This is a committed database with failed local publication, not a normal commit and not a rollback. No current state or test distinguishes it. |
| **Red** | `ML2-RO-01-I/U/S/D` | Open `TransactionType.ReadOnly` and call `Insert`, `Update`, `Save`, or `Delete`; also exercise the database convenience overloads with `transactionType: ReadOnly`. | Reject before `StateChange` construction and provider command execution. `Changes` and caches stay unchanged; mutable state is unchanged; the read-only transaction remains usable for reads and normal terminal completion. | The validity method returns immediately for read-only transactions (E14), so all write APIs proceed. The cache layer simultaneously declines to create a transaction row cache for that transaction (E15), making successful mutation hydration/publication internally inconsistent. | `ML-2`, core command-count assertion and provider matrix `TX-6` | The current code explicitly does the opposite of the accepted contract. No active read-only mutation test exists. |

### Attached External Transactions

| Status | Proposed test ID | Entry condition | Required 0.9 exit condition | Current behavior and evidence | Owner | Why this is not green today |
| --- | --- | --- | --- | --- | --- | --- |
| **Amber** | `TX5-ATTACH-COMMIT-01` | Begin provider `IDbTransaction`, attach it, perform DataLinq write(s), and call only the DataLinq wrapper's `Commit()`. Reuse the mutable through the same provider afterward. | Wrapper commits the provider transaction, publishes committed state, removes local rows, and promotes touched mutables exactly like an owned transaction. | The wrapper can call the attached provider transaction's commit (E16/E17), but there is no touched registry or promotion. The current test instead commits the external transaction first. | `TX-5`, using `TX-1`/`TX-2`; provider matrix `TX-6` | The supported completion sequence is not the sequence currently tested, and mutable promotion is unobservable. |
| **Red** | `TX5-ATTACH-RB-01` | Attach an open provider transaction, perform DataLinq writes, and call only wrapper `Rollback()`. | Provider rollback, local-cache cleanup, and invalidation of every touched mutable with `RolledBack`; nothing globally published. | Attached rollback uses the generic rollback path (E13/E18), which has no mutable tracking. There is no focused attached-write rollback test. | `TX-5`, using `TX-3`; provider matrix `TX-6` | Database rollback may work, but the lifecycle contract does not exist. |
| **Red** | `TX5-ATTACH-EXTERNAL-01-C/R` | Attach and perform at least one DataLinq mutation, then commit or roll back the original `IDbTransaction` directly. Invoke the next wrapper read/write/commit operation. | Detect external closure where possible; poison/invalidate rather than infer commit versus rollback; perform no guessed global publication; emit a deterministic unsupported-completion diagnostic. | Provider adapters can see the connection is no longer open, skip the underlying commit, and still mark themselves committed (E17). `Transaction.Commit()` then applies every `Change` globally (E12). After external rollback, this can publish changes that do not exist in the database. The existing external-commit-first test approves only the lucky half of this ambiguity. | `TX-5`, failure handling from `TX-4`; core and provider matrix `TX-6` | Current status transition cannot distinguish external commit from external rollback. Publishing in that state is factually unsafe. |
| **Red** | `TX5-ATTACH-DISP-01` | Attach, perform DataLinq writes, then dispose the wrapper without an observed wrapper commit; include already externally closed and provider-dispose-fault variants. | Conservatively invalidate touched mutables, remove local cache state, and roll back/dispose the still-open provider transaction where possible. Never infer that a mutable became committed. | Disposal removes cache rows and delegates provider rollback/disposal (E13/E18), but no touched mutable is known. If the original transaction was externally committed, the wrapper cannot reconstruct publication or safely promote. | `TX-5`, `TX-3`, `TX-4`; core fault lane and provider matrix `TX-6` | Cache cleanup alone cannot make mutable baselines trustworthy, and external completion remains ambiguous. |

## Test Entry/Exit Invariants

Every implementation test above should make the command and cache boundary observable, not merely assert that some exception was thrown.

### Preflight exits

For `ML2-XP-*`, `ML2-XT-*`, `ML2-PK-*`, and `ML2-RO-*`:

- provider mutation command count remains zero
- transaction `Changes` remains empty
- no transaction row cache or notification is created by the rejected operation
- the mutable's prior provenance and user assignments remain unchanged
- the transaction remains usable when the rejection is a caller preflight error rather than a provider failure

### Successful statement, pre-terminal exits

For same-transaction reuse and attached/owned success:

- the provider statement has succeeded
- the change is present exactly once in the successful collection
- the hydrated mutable is clean and transaction-local to the exact owning wrapper
- only that transaction sees its row/cache/notification effects
- another transaction or implicit write cannot consume the baseline

### Failure exits

For `TX4-*` and provider lifecycle faults:

- a failed candidate is absent from successful `Changes`
- no later `Commit()` can globally publish either the failed candidate or earlier pending successes from the poisoned transaction
- all touched mutable baselines are invalid even if provider rollback/disposal cleanup throws
- diagnostics distinguish statement failure, hydration/local-cache failure, unknown commit outcome, and committed-database/local-publication failure
- user assignments may remain inspectable, but public `Reset()` cannot make the mutable writable again

### Terminal exits

For commit, rollback, open disposal, and mutable delete:

- provider completion occurs before the corresponding mutable promotion or invalidation
- transaction cache entries disappear on every terminal path
- normal commit promotes non-deleted mutables and leaves deleted mutables terminal
- rollback/open disposal invalidates all touched mutables rather than reconstructing a guessed committed baseline
- committed cache and relation state changes only after confirmed provider commit

## Implementation Order Implied By The Matrix

The matrix cannot be implemented safely as a pile of isolated exception checks. The dependency order is substantive:

1. `ML-1` adds provider/transaction provenance and invalid reasons without changing public equality or property APIs.
2. `ML-2` installs command-free preflight for cross-provider, cross-transaction, invalid/deleted, primary-key, and read-only cases.
3. `TX-1` records only successful changes and tracks touched mutables by reference identity.
4. `TX-2` promotes after confirmed commit/publication and handles post-commit publication failure.
5. `TX-3` invalidates in finally-safe rollback/open-disposal cleanup.
6. `TX-4` poisons statement/hydration/commit failures and narrows legal recovery to rollback/dispose.
7. `TX-5` applies those same transitions to attached transactions and rejects ambiguous external completion.
8. `TX-6` runs the accepted cases across SQLite, MySQL, and MariaDB after the deterministic core fault lane is green.

Skipping directly to provider compliance tests would make results depend on locks, connector error policy, and cache accidents. The preflight and fault partitions belong in deterministic core tests first; provider tests then prove that the contract survives real connectors.

## W1 Exit Statement

The expected-failure inventory is complete when this document is linked from the W1 status record. It does **not** make any red or amber row green. W3 owns implementation and executable acceptance.

The only current green control is ordinary repeated same-provider reuse on the successful path. Even that control must be re-grounded on explicit commit promotion before the mutable lifecycle workstream can claim completion.
