> [!WARNING]
> Internal W1 transaction reporting and controllable-provider evidence only. Built-in native binding, synchronous observer composition, command telemetry and W1 closeout remain open.

# W1 Async Transaction Telemetry

**Recorded:** 2026-09-20, after [async mutation telemetry](W1%20Async%20Mutation%20Telemetry.md). This extends [managed async completion](W1%20Managed%20Async%20Completion.md) and the existing [initialization handoff](W1%20Initialization%20Handoff.md). The [completion audit](W1%20Completion%20Audit.md) retains the remaining requirements.

## Owned reporting and completion certainty

Previously, the internal async completion path reused the protected synchronous transaction telemetry finalizer. One throwing meter listener could prevent duration reporting and activity stopping, leave reporting repeatable, and lose local-failure classification. Committed local-cache finalization failures and uncertain native completion also bypassed telemetry closure. Eight initial controllable cases failed against that runtime before the production changes.

[DatabaseTransaction](../../../../src/DataLinq/Database/DatabaseTransaction.cs) now supplies a separate internal async completion reporter, sharing the existing transaction lifecycle fields. It claims reporting completion before invoking any observer. The [telemetry overloads](../../../../src/DataLinq/Diagnostics/DataLinqTelemetry.cs) independently attempt the completion counter and duration histogram. [Activity finalization](../../../../src/DataLinq/Execution/ExecutionActivity.cs) independently handles tags, stopping and ambient restoration. Provider status notification and managed status notification have separate diagnostic scopes; a failure in one does not suppress the other or the telemetry attempts.

[Managed completion](../../../../src/DataLinq/Mutation/Transaction.AsyncCompletion.cs) records native confirmation before fallible local work. A confirmed commit or rollback therefore retains its completion and corresponding aggregate count even if cache publication, notification or a telemetry observer fails. Existing `commit`/`rollback` outcome labels describe native completion; an activity can retain that outcome while carrying error status for a known local failure. The original failure remains primary, with later reporting failures retained in encounter order as local-finalization failures. Requested commit/rollback identity and existing cleanup attribution are preserved.

An uncertain native completion closes telemetry once as `failure`. Later recovery or disposal cannot rewrite it as a confirmed success. Disposal without confirmation also closes an already-started lifetime as failure without inventing a rollback. Unused wrappers emit no transaction activity or count simply because they are completed or disposed. The existing cleanup runner still attempts transaction and connection disposal independently; no retry or rollback after confirmed commit is introduced.

Reporting describes facts known when each measurement is emitted. A listener can fail after observing a measurement or after an activity's tags were set; those observations cannot be retracted. BCL delivery to every subscriber is not guaranteed if a subscriber throws. Transaction completion counters reflect native confirmation, not whether every local notification succeeded.

## First use and activity context

The new internal `BeginAsyncTransactionTelemetry` hook performs no I/O and confers no admission authority. A provider must invoke it only after native initialization/begin settles, under its existing initialization owner. It owns an activity before start callbacks and attempts the start counter independently. If sampling, start notification or that counter fails, it closes reporting once and supplies initialization evidence. The existing lazy-resource owner makes initialization terminal, waits for partial-resource cleanup, and does not publish a resource or dispatch a reader.

The start hook can finish telemetry before the enclosing owner finishes failed-initialization cleanup; its measured lifetime does not promise to include that cleanup. The operation itself still waits for cleanup and retains admission until the failure is released. Controllable tests exercise this boundary with paused cleanup. Production native adapters do not yet call this hook; that binding remains W2. Completing a transaction that successfully began through the legacy synchronous telemetry hook is also covered, without changing that protected hook's implementation.

The expanded tests exposed a separate context error: normal stopping of a transaction activity begun under an earlier caller restored that earlier context during subsequent helper cleanup. The [.NET 10 `Activity.Stop` implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/Activity.cs#L775) notifies stop listeners before restoring its saved previous activity. A throwing listener can skip restoration; a successful stop can restore an earlier caller even when another caller is now active. Async context restoration after the helper returns does not prove correctness inside its cleanup.

Transaction finalization now preserves the caller active immediately before stopping when the owned transaction activity was not current. The helper test checks context inside resource disposal, for both successful and throwing stop listeners, while preserving the activity's original parent. Query and mutation behavior through the shared finalizer remains unchanged.

## Controllable verification

[The 29 new TUnit cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.TransactionTelemetry.cs) use real meter/activity listeners and controllable transaction completion and cleanup. Global-listener cases are nonparallel.

| Cases | Covered boundary |
| ---: | --- |
| 6 | Confirmed commit/rollback with provider and managed status failures, completion-counter, duration and stop failures; exact primary/secondary order, classification, counts, admission and recovery |
| 1 | Committed cache-publication failure still closes telemetry with confirmed commit and preserves the existing finalization exception |
| 2 | Uncertain native commit/rollback plus stop failure closes once, retaining the native primary |
| 1 | Interrupted reporting cannot count completion or stop the activity again |
| 3 | Sampling/start/counter failure during first use waits for partial cleanup, dispatches nothing and leaves initialization terminal |
| 4 | Successful commit/rollback with and without observers; one start/outcome and no invented failure |
| 2 | Unused completion emits no native-transaction telemetry |
| 2 | Managed status failure retains confirmed completion and marks the activity failed |
| 2 | Helper cleanup sees the later caller activity after normal or throwing stop notification |
| 2 | Disposal without confirmation, with and without cleanup failure, invents no rollback and reports once |
| 2 | Cancellation during native completion remains primary and uncertain through reporting and disposal |
| 2 | Cancellation during reporting after confirmed completion cannot undo that outcome |

The initial negative capture was **0/8**. After the first reporting changes those eight passed. The expanded capture was **24/25**: the later-caller helper case failed before the context restoration fix. Final focused transaction **29/29**, query regression **39/39** and mutation regression **29/29** pass. Broad local verification passes **3,240 unit + 221 Memory + 528 SQLite file + 528 SQLite memory = 4,517**, with no skips; focused cases are included in the unit total. Release core builds for .NET 8/9/10 and unit/Memory/compliance builds have zero warnings/errors.

All listed reports have complete counts, invocations and artifacts. Final reports exit zero; the two deliberate failure captures exit two. `ValidForEvidence=false` identifies bounded local development checks, not full release qualification. Raw artifacts remain local under `artifacts/`, unmodified and unuploaded. Final-head Latest CI and the expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-transaction-telemetry-negative.json` | 0 / 8 | `98e4215bc5cbd2d215af68551c1f5dc0abbe7deed5cb3975d1104872a3c0f6f1` |
| `w1-transaction-telemetry-expanded.json` | 24 / 25 | `fab500dc9a82a7ae79707f583ff628b2ef004fb84f6680cf4a4d82968f46a988` |
| `w1-transaction-telemetry-final.json` | 29 / 29 | `9468223894072bbc8175037c3cfd54a71e4907a8675a5db51c8f7fa8194a8b6f` |
| `w1-transaction-telemetry-query-regression.json` | 39 / 39 | `79663e933428562c8fc96eea5d10c9a4465f0b434d6a2e788f434323bf8dc181` |
| `w1-transaction-telemetry-mutation-regression.json` | 29 / 29 | `3fbed3e96df4a96f0e12c6f0585e6f26b5327edbc34fb584636a5dadb17d4ae0` |
| `w1-transaction-telemetry-unit.json` | 3,240 / 3,240 | `38e35e4c6e01e9cefe25d6292f65ad37f31bc013b0b93cf7070ec8f08ad18c44` |
| `w1-transaction-telemetry-memory.json` | 221 / 221 | `ddad85ba629c04af28846c88357b3e29379f4e2616927a531506ab97e963b055` |
| `w1-transaction-telemetry-sqlite-file.json` | 528 / 528 | `dbaac79c66e919162492fd01501d2a2a5f8634d2b931d28a2e5f19d68e8b2c83` |
| `w1-transaction-telemetry-sqlite-memory.json` | 528 / 528 | `46da4ebe73c1aff8f939f024a432a3d7987a237f653be06b641497475f4474e9` |

## Remaining work

**2026-09-20 follow-through:** [Command telemetry](W1%20Command%20Telemetry.md) now repairs the existing synchronous provider helper and supplies internal async hooks, independently composing reporting/reader-cleanup failures and retaining command-specific no-dispatch evidence. Native async adoption and the other remaining requirements below stay open.

Command telemetry must retain its single provider boundary. Synchronous query/mutation/command/transaction observer composition, higher local diagnostics, standalone raw-provider attribution, administrative coverage and full cause/stage/correlation mapping remain open. Protected synchronous transaction telemetry and built-in native adapters are unchanged here.

Final I/O-path mapping, internal adapter readiness, attributable coordination costs, remaining allocation review and all six strict W0 performance lanes remain W1 requirements. Earlier performance captures do not qualify this runtime. Native binding stays W2, public/packed APIs stay W3, and the W0-F1 official SQLite package acceptance gate is unchanged. No public API or package publication changes in this slice.
