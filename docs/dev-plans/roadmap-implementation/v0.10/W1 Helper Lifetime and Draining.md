> [!WARNING]
> Internal 0.10 helper orchestration. No public async callback API or native helper adapter is exposed. This is part of W1, not a claim that its full exit gate or W0-F1 is closed.

# W1 Helper Lifetime And Draining

**Recorded:** 2026-09-17. Follows merged [automatic recovery PR #155](https://github.com/bazer/DataLinq/pull/155), under the accepted [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception). The [completion audit](W1%20Completion%20Audit.md) tracks the remaining W1 requirements.

## Private Completion And Atomic Closure

`TransactionOperationGate.BeginHelperLifetime()` creates private lifetime ownership without taking the ordinary execution slot. Callback code can run sequential managed operations and pass the borrowed transaction to nested services. Managed synchronous commit, rollback and disposal now distinguish completion admission: when a helper owns the lifetime, these caller operations fail before provider completion or lifecycle mutation. No ambient flag or inherited thread privilege grants helper authority.

`TransactionCallbackRunner` validates the internal adapter, observes pre-cancellation, invokes the callback once and awaits its returned task. The callback receives the supplied token; inner calls still use the tokens explicitly passed to them. Null tasks fail rather than becoming success. The internal adapter separates confirmed provider commit from subsequent short local finalization.

After the callback completes, one gate transition closes admission and snapshots whether any operation/reader is still active. New operations are rejected permanently through that helper's transaction gate. Existing admitted work keeps its private owner. Active work prevents commit even if it eventually succeeds; a successful callback receives `InvalidOperationException`, while a thrown callback stays primary and the unfinished-work error is secondary. The helper does not claim to detect an unawaited task whose execution already completed, or arbitrary background code that has not yet touched the transaction.

## Draining Commands And Readers

The helper awaits gate changes without a caller token or drain deadline. Pending command owners can record their failures before releasing admission. A reader registered after closure is immediately stopped and still drained; transferring an owner preserves tracking. Signals and locks protect transitions only. No lock is held across provider calls, application code or awaits.

Both the internal async enumerator and the synchronous outer transaction query enumerator register private reader controls. Closure blocks new iterator calls, waits for an existing move/disposal call, closes the reader and releases its lease. A reader paused between rows is actively closed instead of leaving the helper waiting indefinitely for caller disposal. Cleanup failures still release reader ownership; later caller disposal does not repeat them. Caller completion remains rejected while the helper drains.

The synchronous outer read boundary records read and cleanup failures before releasing its lease, preserving a read failure if iterator disposal also fails. The async boundary imports its existing typed context. Mutation execution reports its failure to an active helper. Re-observing the same exception through a pending operation and its drain does not duplicate it in the final secondary list. The subsequent [eager read slice](W1%20Eager%20Read%20Failure%20Reporting.md) covers point/scalar/source/relation failure hooks and independent owned command/reader cleanup. Complete lifecycle and telemetry boundary coverage remains open.

Only after draining does the helper obtain a private completion lease. With no failure it validates commit, checks cancellation, awaits confirmed commit and runs local finalization independently of late token cancellation. Results are delivered only after resource cleanup succeeds. Commit failure remains uncertain after recovery; confirmed commit survives finalization/disposal errors. Any failure skips ordinary success and uses [automatic recovery](W1%20Automatic%20Recovery%20Rollback.md), whose independent timer begins after draining. Recovery permissions come from the lifecycle adapter rather than guessed connection state.

## Development Evidence

Twenty-four new TUnit cases exercise callback invocation/result timing, nested sequential work, borrowed completion rejection, pre-callback/pre-commit/late cancellation, poisoned validation, commit/finalization/cleanup failures, completed-but-unawaited detection limits, null tasks, late reader registration, pending command failures, escaped synchronous LINQ/fluent/raw readers, in-flight synchronous reads, pending async moves/disposal and ordered failure import without duplicates.

Local Release / .NET 10 results:

- `artifacts/w1-helper-transactions.json`: **209/209 passed**, transaction-focused tests.
- `artifacts/w1-helper-unit.json`: **2,038/2,038 passed**, full unit suite.
- `artifacts/w1-helper-build.log`: unit/dependency build, zero warnings/errors.
- `artifacts/w1-helper-sqlite-file.json` and `artifacts/w1-helper-sqlite-memory.json`: **519/519 passed each**, compliance anchor shards at maximum parallelism 8.
- `artifacts/w1-helper-compliance-build.log` and `artifacts/w1-helper-core-build.log`: zero-warning/error builds; core targets .NET 8/9/10.
- All **91 local links** in the six changed planning pages resolve; `git diff --check` is clean. Planning pages are excluded from DocFX.

CI evidence is recorded separately with the PR head. These are development checks, not frozen release evidence. New helper cases use controllable lifecycle adapters and scripted transaction fixtures. The synchronous in-flight test uses a test-only worker to pause synchronous provider code; production has no `Task.Run` async facade.

## Remaining Work

After the [eager read reporting follow-through](W1%20Eager%20Read%20Failure%20Reporting.md), complete completion/initialization handoff, then continue the [W1 checklist](W1%20Completion%20Audit.md): async mutation/hydration and immutable invocation capture, owned generated commands, relation publication/coordination, metadata/root orchestration contracts and coordination/allocation evidence. The later [managed async completion slice](W1%20Managed%20Async%20Completion.md) integrates actual cache/mutable finalization and helper disposal. Complete initialization handoff remains open; neither slice claims native provider or public callback wiring.

W2/W3 still own native classification/confirmation, driver cancellation/disposal feasibility, public callback overloads, provider options and consumer compatibility. The 0.9.2 compatibility baseline, .NET 10 benchmark target and official SQLite dependency are unchanged. W0-F1 and the blocked native/public/release gates remain open.
