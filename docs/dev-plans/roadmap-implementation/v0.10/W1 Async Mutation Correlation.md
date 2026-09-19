> [!WARNING]
> Internal W1 mutation diagnostics with controllable adapters. This does not complete W1, expose public diagnostics or prove native provider behavior. W2/W3 and SQLite W0-F1 remain open.

# W1 Async Mutation Correlation

**Date:** 2026-09-19. Continues [failure correlation](W1%20Failure%20Correlation%20and%20Admission%20Diagnostics.md) and [async read correlation](W1%20Async%20Read%20Correlation.md) under AAPI-91–95. The [completion audit](W1%20Completion%20Audit.md) retains the remaining obligations.

## Requested operation versus executed statement

[Transaction.AsyncMutation](../../../../src/DataLinq/Mutation/Transaction.AsyncMutation.cs) carries an explicit diagnostic kind separately from `TransactionChangeType`. Insert, Update, Save, Delete, collection Insert and explicit state-change execution supply their kinds before dispatch. Save remains Save whether lifecycle selection chooses insert or update, including the local-edit overloads and an unchanged existing model that only requires a read. Selecting the physical statement and capturing mutation inputs retain their existing behavior.

Admission and private steps carry the requested kind. The hydration read inherits the owner identity through the read-correlation integration; a Save failure while hydrating is not mislabeled KeyLookup. Mutation snapshots supply the gate's captured provider telemetry-instance identifier and current transaction ID. The recovery decision still uses actual dispatch and provider evidence: unchanged-update read failures do not claim a write, while a failed hydration after a dispatched mutation retains poisoning/rollback restrictions.

[MutationPreflight](../../../../src/DataLinq/Mutation/MutationPreflight.cs) accepts the optional diagnostic kind without changing its validation order. It preserves Save through early async overlap rejection; existing callers default to their known statement type. This does not bind all synchronous mutation owners or complete synchronous failure reporting. The rejected call cannot overwrite admitted operation state.

## Database helper and failure precedence

[Transaction.AsyncMutationHelper](../../../../src/DataLinq/Mutation/Transaction.AsyncMutationHelper.cs) preserves mutation identity through preparation cleanup and automatic recovery. The internal callback runner accepts a separate fallback kind for its generated mutation body; cancellation before invoking that body retains Save/Insert/Update/Delete rather than inventing an application TransactionCallback operation. Ordinary application transaction callbacks keep their established classification.

More specific reported failures retain precedence. A failing commit remains Commit; a primary owned cleanup failure remains Dispose. Secondary rollback/disposal entries keep their own identity, order and original exception objects. An owned helper publishes terminal recovery `None` only after its lifetime is finished; previous completion certainty is preserved. No automatic retry or native interruption claim is added.

Actual local lifecycle finalization failures now use `LocalFinalizationError`, separately from materialization and provider execution. The mutation is still poisoned after its statement has dispatched. Local `Action` edits remain before capture/admission as previously designed; their full callback/preparation diagnostic mapping is part of the remaining classification audit, not a claim of this slice.

## Verification

All **32** focused cases in [TransactionMutationFailureTests.MutationCorrelation](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.MutationCorrelation.cs) pass. They cover insert/update/save/delete/collection dispatch with command cleanup, generated-key scalar execution, local-edit overload admission, private hydration, unchanged-update read recovery, helper preparation/cancellation/commit/disposal precedence, historical snapshot isolation, ordered recovery failures and legacy delete lifecycle finalization.

After the final source change, core builds on .NET 8/9/10 and unit/dependency, compliance and Memory Release builds succeeded with zero warnings/errors. Broad local runs passed **3,069 unit + 210 Memory + 528 SQLite-file + 528 SQLite-memory = 4,335** cases, with no failures or skips. Reports are `artifacts/w1-mutation-correlation-{focused,unit,memory,sqlite-file,sqlite-memory}.json`, with complete invocation/artifact status; these are bounded local checks, not release evidence. All **116** relative links/anchors across this record, the completion audit and integration plan resolve; `git diff --check` passes. Planning-only changes do not change published DocFX navigation or layout.

## Remaining work

Memory, administrative/root and remaining standalone raw-provider correlation; complete cause/stage/secondary classification including local-edit and preparation failures; synchronous owner/failure publication; telemetry; internal adapter compatibility readiness; comparable .NET 10 coordination/allocation evidence; and final requirement/code/test/I/O mapping remain W1 work. The frozen 0.9.2 compatibility baseline is unchanged. No native/public async release or package publication is included.
