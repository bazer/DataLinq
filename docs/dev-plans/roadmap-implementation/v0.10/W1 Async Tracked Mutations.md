> [!WARNING]
> Internal W1 transaction orchestration, not public/native async mutation support or W1 closeout. Database-level helper reservation lifetimes, remaining custom/generated mutation evidence, complete telemetry/correlation and allocation comparisons remain open.

# W1 Async Tracked Mutations

**Date:** 2026-09-18. Implements a transaction-level portion of AAPI-27–30 and AAPI-71 using the existing mutation snapshots, lifecycle, transaction cache and [managed completion](W1%20Managed%20Async%20Completion.md). See the [completion audit](W1%20Completion%20Audit.md) for the complete remaining scope.

## Capture and exclusive inputs

Internal insert, update, save and delete entries capture model identity/lifecycle and mapped values during the method call, before the first suspension. Save chooses insert/update from that lifecycle. The finite insert entry enumerates once, rejects duplicate object identities/nulls and captures every model before binding commands or writing. Binding renders captured parameterized SQL and validates command capability before cancellation/execution. Arrays follow the existing detached snapshot contract; arbitrary scalar objects are not deep-cloned.

Known DataLinq mutables reserve the existing row-data ownership lock for short local editing/reservation transitions. Setters (including generated setters through the base indexer), both reset forms, explicit deletion and conflicting mutation preflight reject while the input is reserved. Another transaction cannot submit that same object. Generated-key assignment uses the explicit private reservation owner; it does not temporarily enable public setters or grant ambient authority. Reservation ends when the transaction-level operation exits, including validation, binding, cancellation and provider failure. Release does not restore an invalid baseline.

Custom mutables retain object-identity submission exclusion and snapshot validation, without claiming interception of arbitrary custom setters or escaped references. Current focused tests prove the known mutable paths and escaped array detection; broader custom/generated consumer evidence remains open. The short setter/reset locks reuse the existing ownership object but add synchronization cost; final W1 allocation/coordination measurements must include it.

Local editing uses a synchronous action: argument/lifecycle validation, cancellation check, one edit invocation, final capture/validation, then cancellation before execution. Pre-write delegate failure or cancellation retains local edits without mutation poisoning. No asynchronous editing delegate is introduced.

## Execution, hydration and failure

`IAsyncMutationCommandFactory` binds captured SQL to an owned `AsyncEagerCommand`. It supplies explicit asynchronous command cleanup and the existing shared lazy initialization contract. Hydration reader policy is captured before suspension, including when the eventual lookup key comes from an insert result. No synchronous command execution or task-pool facade is used.

One transaction lease and private step cover command execution/cleanup, transaction-local cache effects, generated-value decoding, authoritative async row hydration and local lifecycle/change recording. Hydration may observe cancellation. The final short consistency section does not check a late token; a confirmed delete, for example, can finish successfully when cancellation arrives during successful command cleanup. Raw commands remain separate from this tracked mutation behavior.

An unchanged update performs no write and uses the existing async cache/source lookup, rather than returning a potentially stale immutable baseline. Warm and cold paths still validate cancellation and retain operation/input ownership.

Failures before statement dispatch preserve otherwise valid prior work. Once any command in the operation has dispatched, failure/cancellation poisons the transaction and invalidates its affected known mutables. Cancellation before a later batch command cannot permit committing the completed prefix, but does not invalidate a later input that never dispatched. Cleanup settles before failure publication, original exceptions and ordered secondary evidence are preserved, and the mutation boundary does not allow a hydration read's ordinary-read evidence to authorize reuse after a write. Initialization, cleanup or assessment failures retain conservative recovery. Helper draining observes in-flight mutation failure before the lease is released and cannot commit unfinished work.

## Verification

Release / .NET 10 focused coverage passed **34/34 `AsyncMutation_*` cases**, maximum parallelism 8 (`artifacts/w1-async-mutations-focused.json`). Tests cover insert/update/save/delete, generated keys, setter/reset and cross-transaction conflicts, command cleanup and hydration lifetimes, pre/post-dispatch failures, partial batches, local edits, unchanged warm/cold updates, escaped arrays, late cancellation, repeated exception identity, invalid failure evidence, initialization and actual helper draining.

The initial focused run passed 12/16. The fixture had no generated-key SQL renderer, and three failure checkpoints were already completed rather than paused; those were corrected. Expanded coverage then passed 27/27. Review also added explicit evidence-assessment failure handling and tightened partial-batch invalidation so a later, never-dispatched input retains its baseline. The final 34-case focused run and broad runs below include those changes.

Broad Release / .NET 10 verification:

- **2,608/2,608 unit cases**, maximum parallelism 16 (`artifacts/w1-async-mutations-unit.json`).
- **210/210 Memory cases**, maximum parallelism 16 (`w1-async-mutations-memory.json`).
- **527/527 compliance cases on each SQLite anchor**, maximum parallelism 8 (`w1-async-mutations-sqlite-file.json`, `w1-async-mutations-sqlite-memory.json`).
- Core .NET 8/9/10 and unit/dependencies/compliance/Memory Release builds: zero warnings/errors. Build artifacts share the `w1-async-mutations-` prefix.

All **3,872** broad cases pass without skips, with invocation and artifacts complete. Local `ValidForEvidence` remains false: bounded local regression checks do not satisfy canonical full-provider/clean-runner release evidence requirements. All 52 relative links in the touched planning pages and whitespace checks pass. Normal docs and website navigation/presentation are unchanged. The PR records exact-head CI and merge evidence.

## Remaining integration

This does not complete all mutation requirements: database-level helper reservations must extend through commit/cleanup, remaining local-overload/custom/generated/key/converter and finalization cases need evidence, and complete command/query/transaction diagnostic correlation remains open. Public overload placement and packed consumers belong to W3; native command/resource bindings remain W2. The synchronous adapter compatibility item remains open and was not removed from the audit when moving to this core W1 work.

The rest of W1 still includes relation coordination/publication, metadata/provisioning, shared owning-root disposal, the final I/O map and comparable .NET 10 performance evidence against 0.9.2. SQLite W0-F1 remains subject to the recorded limited exception.
