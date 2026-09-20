> [!WARNING]
> Internal W1 mutation reporting and controllable-provider evidence only. Command/transaction telemetry, native bindings, public APIs and W1 closeout remain open.

# W1 Async Mutation Telemetry

**Recorded:** 2026-09-20, after [async query telemetry](W1%20Async%20Query%20Telemetry.md). This follows the existing [tracked mutation](W1%20Async%20Tracked%20Mutations.md), [database helper](W1%20Database%20Mutation%20Helpers.md) and [requested-operation correlation](W1%20Async%20Mutation%20Correlation.md) contracts. The [completion audit](W1%20Completion%20Audit.md) retains all remaining requirements.

## Reproduced failure and owned reporting

[Transaction.AsyncMutation](../../../../src/DataLinq/Mutation/Transaction.AsyncMutation.cs) previously started its activity before entering the failure collector and reported completion in `finally`. A throwing activity-start listener escaped without a diagnostic snapshot; a throwing completion listener could replace the original execution, hydration or cleanup exception. The first six controllable regression cases all failed against that runtime. The failures were captured before changing production code.

Each admitted input now owns a [mutation telemetry record](../../../../src/DataLinq/Execution/MutationExecutionTelemetry.cs). It begins after per-input cancellation and captured-input validation, owns the activity before notifying start listeners, and completes after command/hydration cleanup and local lifecycle work. Reporting remains under transaction admission and the input reservations. Empty batches, pre-canceled calls and binding-validation failures emit nothing. A later batch input canceled before starting telemetry does not acquire a mutation count.

The [telemetry overload](../../../../src/DataLinq/Diagnostics/DataLinqTelemetry.cs) independently attempts the mutation counter, affected-row counter when positive, and duration histogram. Aggregate provider/table bookkeeping is recorded even if a meter observer throws. [Shared activity finalization](../../../../src/DataLinq/Execution/ExecutionActivity.cs) independently handles outcome/exception tags, stopping and restoration after a throwing stop listener. Query telemetry now uses this same finalizer; its 39 regression cases pass unchanged. Each observer attempt retains a separate diagnostic scope. No activity or ambient state confers transaction admission.

Original execution/cancellation/cleanup failures remain primary. Reporting failures follow in encounter order as local-finalization failures; if execution succeeded, the first reporting failure becomes primary. Existing more-specific attribution remains authoritative: a primary owned cleanup failure is still `Dispose`, while ordinary mutation and reporting failures retain requested `Save` rather than deriving `Update` from the executed statement. A development assertion expecting `Save` for primary cleanup was corrected to preserve this existing contract; no runtime attribution was weakened to satisfy it.

## State and recovery

Reporting failures are collected before provider evidence assessment, failure publication and admission release. The existing write evidence still decides poisoning and recovery:

- A sampling/start failure before dispatch preserves valid prior work, input baselines and reuse when the owned source establishes no-statement evidence. Optional provider assessment remains in place; no classifier bypass is introduced.
- A reporting failure after a dispatched mutation poisons the transaction and invalidates the affected known mutables. Successful local finalization does not let the caller commit a mutation whose operation ultimately failed.
- A later input's start/reporting failure or cancellation cannot permit committing a completed batch prefix. Inputs that never dispatched retain their own baselines, and all reservations are released.
- Unchanged-save execution remains a read. A listener failure alone does not invent a write or poison the transaction; cache-only and cold-read paths retain their evidence-dependent recovery.
- Cleanup failure still removes rollback/continue permission, including when execution, cleanup and observers reuse the same exception object. Identity deduplication does not erase cleanup safety facts.
- Database helpers retain the reporting primary through rollback and both disposal stages, with ordered recovery failures and terminal recovery permissions. Cancellation arriving after a single mutation's local success does not retroactively undo that result; a later batch input still checks its token.

Telemetry uses the selected physical mutation type for its existing insert/update/delete dimensions; failure diagnostics separately retain the requested operation. Affected rows remain the value returned by the command coordinator, or zero if it did not return normally; this does not infer rows affected by an uncertain failed command.

Aggregate counts and meter outcome tags describe the settled mutation result known when reporting starts. A subsequent observer failure can fail the operation and mark its activity failed, but cannot retract an earlier measurement. An activity-stop failure occurs after its outcome tags were set and may already have been observed. BCL listener delivery to every subscriber is not guaranteed when a subscriber throws. These limits do not permit swallowing the exception or weakening recovery.

## Controllable verification

[The 29 new TUnit cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.MutationTelemetry.cs) use real activity/meter listeners and explicit dispatch/cleanup checkpoints, with nonparallel listener tests:

| Cases | Covered boundary |
| ---: | --- |
| 5 | Provider, provider-plus-cleanup, cleanup, hydration and successful execution combined with throwing reporters; original identity, exact secondary order, operation identity, poisoning and independent attempts |
| 2 | Sampling/start rejection before dispatch; activity ownership, local classification, no statement, released reservations and usable transaction |
| 8 | Insert/update/save/delete, generated-key scalar and unchanged save; statement dimensions, parent, one logical mutation and no duplicate query counts, with and without observers |
| 1 | Both command and hydration cleanup must settle before any completion report; admission/reservations remain held |
| 3 | Failed first reporting, failed second activity start and canceled second input; written-prefix invalidation without dispatching/invalidation of the later input |
| 2 | Cache-only/cold unchanged-save listener failure preserves read-only recovery and does not poison |
| 1 | Helper retains the reporting primary through rollback and connection-cleanup failures, without commit |
| 1 | Request cancellation stays primary while cleanup and throwing observers settle |
| 3 | Empty, pre-canceled and binding-invalid inputs emit no mutation telemetry |
| 2 | Cancellation from a successful save/delete stop listener does not undo the completed result |
| 1 | Reused execution/cleanup/listener exception preserves cleanup restrictions without duplicate secondary entries |

Final Release core builds for .NET 8/9/10 and unit/Memory/compliance builds have zero warnings/errors. Focused mutation **29/29** and query regression **39/39** pass. Broad local verification passes **3,211 unit + 221 Memory + 528 SQLite file + 528 SQLite memory = 4,488**, with no skips. The focused cases are included in the unit total.

All listed reports have complete counts, invocations and artifacts. Final reports exit zero; the deliberate negative capture exits two. `ValidForEvidence=false` identifies bounded local development checks, not full release qualification. Raw artifacts remain local under `artifacts/`, unmodified and unuploaded. Final-head Latest CI and the expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-mutation-telemetry-negative.json` | 0 / 6 | `5850141a4c11802a07c7f66f4d23eb893d948698808e1bf704d5476a07cd6f1f` |
| `w1-mutation-telemetry-final.json` | 29 / 29 | `cdfb6665de17afa526f3dbaef5a4d085ae215b32d950d9dff6aaf6d8ea7fdc17` |
| `w1-mutation-telemetry-query-regression.json` | 39 / 39 | `f85bbed09495441e7a8bae1de1b0a48c12656c65e47005ae4cda3a94ecb5e419` |
| `w1-mutation-telemetry-unit.json` | 3,211 / 3,211 | `e68ff2eca33c9b1bef0bf6bbd609f324e068c598b22f101a3703982ff440a011` |
| `w1-mutation-telemetry-memory.json` | 221 / 221 | `d3dc4a0d32bcfe14b07c74088abce41dce66fd3e199cf4dfeab497aadc05f4f0` |
| `w1-mutation-telemetry-sqlite-file.json` | 528 / 528 | `f342da98e7fb0eec0254fed5b6afb90702f93a9d84696aec1dd4fcdb74ee6af2` |
| `w1-mutation-telemetry-sqlite-memory.json` | 528 / 528 | `4ed98bf5f6327d2fb75641a30305433d0135e8b2d27a9116409565b3513842bf` |

## Remaining work

**2026-09-20 follow-through:** [Async transaction telemetry](W1%20Async%20Transaction%20Telemetry.md) now preserves confirmed completion through independent notifications/reporting, closes uncertain/disposed lifetimes once, and supplies a controllable first-use hook. It also preserves the later caller inside helper cleanup. Native start binding and protected synchronous telemetry remain unchanged; the remaining scope below is retained where not covered by that bounded record.

The audit found that command and transaction instrumentation currently lives at the provider boundary. The next integration must preserve that single boundary, isolate observer failures from confirmed native completion, and avoid adding duplicate command/transaction counts in managed callers. Synchronous mutation/command/transaction observer composition, higher local diagnostics, standalone raw-provider attribution, administrative coverage and complete cause/stage mapping remain open.

This slice does not measure or waive async coordination/allocation costs. Final mapping across the W0 I/O inventory, internal adapter readiness, all six strict W0 performance lanes and broad W1 closeout remain required. Native binding stays W2, public/packed declarations stay W3, and the W0-F1 SQLite official-package acceptance gate is unchanged. No public API, synchronous telemetry implementation or package publication is changed here.
