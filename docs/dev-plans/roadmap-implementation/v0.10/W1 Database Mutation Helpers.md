> [!WARNING]
> Internal W1 orchestration with controllable adapters, not public/native async mutation support or W1 closeout. Native adoption remains W2; public overloads and packed/generated consumers remain W3.

# W1 Database Mutation Helpers

**Date:** 2026-09-18. Extends the [transaction mutation slice](W1%20Async%20Tracked%20Mutations.md) through the database-owned lifetime required by AAPI-28, AAPI-68 and AAPI-69. The [completion audit](W1%20Completion%20Audit.md) retains the full remaining scope.

## Ownership and capture

Internal `Database<T>` insert/update/save/delete entries construct their own existing lazy transaction, preserving the selected transaction type. There is no database-level batch entry, implicit join of an originating transaction or new transaction factory. An immutable from an unresolved source transaction is still rejected by existing mutation preflight; after source commit, an otherwise valid model can be submitted through the helper's separate transaction.

The helper captures and reserves its input, binds the command and captures hydration reader policy synchronously before its first suspension. The transaction execution path and database helper reuse the same binding and execution routines. Save selects insert/update from the captured input lifecycle. Generated-key assignment and authoritative hydration keep the existing private reservation authority. Unchanged update performs the current cache/source lookup without writing or replacing the unchanged mutable's baseline with a stale shortcut.

The reservation remains held through mutation execution, hydration, commit/finalization, recovery and both transaction/connection cleanup attempts. The result or original failure becomes observable only after cleanup settles and the reservation is released. Setters, reset/deletion and competing submissions reject throughout that lifetime. Release does not repair an invalidated baseline.

The existing [managed callback/completion core](W1%20Managed%20Async%20Completion.md) owns normal commit/recovery. Preparation or callback-capability validation can fail before that runner takes ownership; the database helper handles those failures through the same automatic recovery boundary, preserving the primary exception and ordered secondary failures. Cancellation does not replace invalid-input/capability failures. An unused resource is not opened merely to roll it back, and request cancellation is not passed to cleanup or linked to automatic rollback's independent budget.

Adapters must explicitly support asynchronous completion, mutation commands and hydration as applicable. Creation retains the existing lazy, I/O-free provider contract; missing async completion rejects before input reservation or provider execution. No synchronous I/O/completion fallback or `Task.Run` facade is introduced. This is controllable internal evidence, not native provider support.

## Failure reporting correction

Expanded helper tests exposed a shared recovery bug: when execution and cleanup throw the same exception object, identity deduplication could lose the fact that cleanup failed. Direct cleanup boundaries in managed completion and automatic recovery now use `ExecutionFailures.AddCleanup`. The primary exception remains the original object, distinct secondary failures retain order, and `HasCleanupFailure` remains true even when no distinct secondary exception can be added.

A failed/uncertain commit remains unknown after a later successful rollback. Confirmed commit survives subsequent cache-publication or disposal failure; those failures do not authorize rollback. The helper still reports failure instead of returning a result when cleanup fails.

## Verification

Release / .NET 10 focused coverage passed **22/22 `DatabaseMutation_*` cases** at maximum parallelism 8 (`artifacts/w1-database-mutations-final-focused.json`). It covers all single-model families, reservations across commit and both cleanup suspensions, validation-before-cancellation, unused-resource cleanup, independent rollback tokens, generated keys, late cancellation, unchanged warm/cold lookups, invocation-stable hydration policy, unresolved source ownership, uncertain/confirmed completion, committed publication failure, ordered multiple failures and repeated exception identity.

The initial focused run passed 17/18: its source-ownership case incorrectly expected a row from another unresolved transaction to be accepted. The test now verifies rejection, then acceptance after source commit. The expanded run passed 21/22 and reproduced the cleanup-reporting bug above. Both failed summaries are retained as `w1-database-mutations-focused.json` and `w1-database-mutations-expanded.json`; the final focused and broad runs include the correction.

Broad Release / .NET 10 verification:

- **2,630/2,630 unit cases**, maximum parallelism 16 (`artifacts/w1-database-mutations-unit.json`).
- **210/210 Memory cases**, maximum parallelism 16 (`w1-database-mutations-memory.json`).
- **527/527 compliance cases on each SQLite anchor**, maximum parallelism 8 (`w1-database-mutations-sqlite-file.json`, `w1-database-mutations-sqlite-memory.json`).
- Core .NET 8/9/10 and unit/dependency/compliance/Memory Release builds: zero warnings/errors (`w1-database-mutations-*-build.log`).

All **3,894** broad cases passed without skips, with invocation and artifacts complete. Local `ValidForEvidence` remains false because these bounded local checks do not meet canonical full-provider/clean-runner release requirements. Planning links and whitespace are checked separately; normal docs, website navigation and presentation are unchanged. The PR records exact-head CI and integration evidence.

## Remaining work

This closes the bounded internal database-helper reservation lifetime, not every mutation requirement. The subsequent [mutation contracts](W1%20Mutation%20Contracts.md) cover internal local-edit overloads, custom/generated mutable behavior, key/converter and finalization cases. Complete diagnostics/correlation and comparable coordination/allocation evidence remain open. Public helper declarations and generated/packed compatibility remain W3, and native async capability/resource implementations remain W2.

The wider W1 audit still includes synchronous adapter compatibility, relation owner/waiter coordination and publication, metadata/provisioning, owning-root disposal, complete telemetry, the final I/O map and .NET 10 performance comparisons against 0.9.2. The SQLite W0-F1 limitation is unchanged.
