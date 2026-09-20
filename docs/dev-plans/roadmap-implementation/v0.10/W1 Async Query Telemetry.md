> [!WARNING]
> Internal W1 integration and controllable-provider evidence only. Native adapters, public async APIs, complete telemetry parity and W1 closeout remain open.

# W1 Async Query Telemetry

**Recorded:** 2026-09-20, following the [mutation preflight checkpoint](W1%20Mutation%20Preflight%20Allocation%20Reduction.md). This closes its identified logical SQL entity/scalar query telemetry gap without changing synchronous telemetry or adding public APIs. The [completion audit](W1%20Completion%20Audit.md) remains authoritative for outstanding W1 work.

## Logical query boundary

[Captured model queries](../../../../src/DataLinq/Query/Select.AsyncModels.cs), [fluent scalar execution](../../../../src/DataLinq/Query/Select.AsyncScalar.cs) and the [SQL async query-plan backend](../../../../src/DataLinq/Linq/Planning/Sql/SqlQueryPlanBackend.Async.cs) now carry immutable provider/table/kind/transactional dimensions into a per-execution [query telemetry owner](../../../../src/DataLinq/Execution/QueryExecutionTelemetry.cs). The existing aggregate entity/scalar counts, `datalinq.query` activity, `datalinq.queries` counter and `datalinq.query.duration` histogram retain their names and dimensions.

The boundary is one logical query, including owned resource cleanup, private model hydration and local terminal/conversion work. Private command and row loads do not increment another logical query. A cache-only exact-key query still counts once. Raw readers, key/relation loads, metadata and direct SQL projection routes without the corresponding synchronous logical-query boundary do not acquire an entity-query count merely because they share a reader coordinator. This is bounded parity with the existing instrumentation, not a new claim that every supported expression emits a query activity.

Capture/binding is local and precedes execution. [Scalar execution](../../../../src/DataLinq/Execution/AsyncScalarRead.cs) and [enumeration](../../../../src/DataLinq/Execution/AsyncReaderEnumerable.cs) start telemetry only after source validation, pre-cancellation checks and successful admission. Unused and pre-canceled enumerations emit no query count or activity. Deferred enumeration keeps its activity across calls, resumes it inside the async call and leaves the caller's activity context intact on return. [Reader transforms](../../../../src/DataLinq/Execution/AsyncReaderTransform.cs) retain the original telemetry descriptor, so a failing `Single` or conversion is not reported as a successful query.

Completion is attempted once after owned cleanup and before releasing transaction admission. Natural exhaustion reports success only when execution, composition and cleanup succeeded. Early disposal and helper draining report an incomplete query as failure, without manufacturing an exception just to record that outcome. Repeated disposal does not report again. Confirmed scalar execution/cleanup retains its existing late-cancellation semantics.

## Listener failures and ownership

Counter, duration and activity completion are independent reporting attempts. A throwing callback cannot prevent the other reports or owned cleanup. Existing execution/cancellation/cleanup failures stay primary; reporting exceptions are retained in order as local-finalization failures. If execution succeeded, the first reporting failure becomes primary. Each callback attempt has its own diagnostic scope, preserving the [scoped attribution contract](W1%20Scoped%20Failure%20Attribution.md). Reporting does not confer transaction admission on callbacks: attempts to reenter the transaction still fail.

The activity is owned before `Start()` invokes listeners, allowing an `ActivityStarted` failure to be finalized rather than losing the already-current activity. A sampling failure produces no activity to dispose. The owned command source supplies no-statement evidence for these pre-dispatch failures; the provider's optional I/O-free failure assessment remains part of the existing contract. No blanket recovery permission or classifier bypass is introduced.

A second deterministic regression exposed a stopped query activity remaining current during failure assessment when `ActivityStopped` threw. The [.NET 10 Activity implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/Activity.cs) notifies stop listeners before restoring its previous current activity. Query completion now independently restores a live parent, or null, if its completed activity is still current. A callback-selected different activity is not overwritten; a restoration failure is retained separately. The caller's async execution context remains unchanged.

Measurements already delivered cannot be withdrawn when a later listener throws. Counter/duration outcome tags describe the execution/cleanup result known when measurement reporting starts; the activity includes reporting failures known before it stops. A failure in one BCL listener can also prevent delivery to later listeners on that same instrument. The guarantee is independent reporting attempts and preserved failure evidence, not guaranteed delivery to every subscriber or retroactive rewriting of measurements.

## Controllable evidence

[The new TUnit cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.QueryTelemetry.cs) use real `ActivityListener` and `MeterListener` callbacks and controlled execution/cleanup checkpoints. They are nonparallel because the listeners observe global instrumentation.

| Cases | Evidence |
| ---: | --- |
| 6 | Object, typed and prepared scalar execution on root/transaction sources; one query, parent/tags, cleanup before completion and admission during cleanup |
| 8 | Fluent batch/exact-key and prepared sequence/exact-terminal entities on both source kinds; private hydration without duplicate query counts |
| 4 | Scalar/entity execution success/failure combined with throwing counter, duration and activity-stop listeners; original/secondary order, all reporting attempts, cleanup and rejected callback reentry |
| 4 | Sampling/start failures on scalar/entity paths; no provider command, failed report, activity ownership, released admission and subsequent transaction query usability |
| 6 | Scalar/entity cancellation, provider failure and cleanup failure; reporting waits for cleanup and records one failed query |
| 4 | Unused, pre-canceled, early-disposed and exhausted enumerators; no duplicate disposal report |
| 2 | Cache-only exact-key execution with and without observers; aggregate count without provider dispatch |
| 1 | Enumeration resumes its original activity under a different later consumer activity without leaking it into the caller |
| 2 | Helper drains unfinished work through child cleanup, never commits and retains a listener failure without duplicate reporting |
| 2 | Scalar conversion and missing-single terminal errors retain materialization classification and failed-query dimensions |

The initial 14 baseline cases failed before telemetry integration. During development, an over-specific full `ParentId` assertion also failed because of trace-flag representation; it was corrected to check the parent object and span identity, not treated as a product regression. The later stopped-activity assessment probe reproduced two failures among 37 cases before the restoration fix. All 39 final cases pass, including the subsequent transaction-usability assertion. Retained negative reports are not overwritten.

## Validation and provenance

Release core builds for .NET 8, 9 and 10, plus unit/Memory/compliance builds, finish with zero warnings/errors. Final focused verification passes 39/39. Broad verification passes **3,182 unit + 221 Memory + 528 SQLite file + 528 SQLite memory = 4,459**, none skipped. Focused cases are included in the unit total, not added twice. The final test-only recovery and exact secondary-failure ordering assertions were rebuilt and rerun in both the focused and complete unit suites; Memory/compliance evidence uses the same unchanged runtime source.

The retained JSON reports have complete counts/invocations/artifacts. Final reports exit zero; the two deliberate negative captures exit two. `ValidForEvidence=false` correctly marks these bounded local functional runs as development verification rather than release qualification. Logs and reports remain local under `artifacts/`; no raw artifacts were uploaded. Final-head Latest CI is required before merge and is recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-query-telemetry-negative.json` | 0 / 14 | `907a293fc4c600d0db664dba71357891a4b4c8f889d3a40271e4a4f0abfad49f` |
| `w1-query-telemetry-assessment-negative.json` | 35 / 37 | `56b0780559144de89f365b4316c34f7fa76d13c8119036a8bd9125aefc0e6956` |
| `w1-query-telemetry-focused-ordered.json` | 39 / 39 | `142689cd0e0ca615260165c1b7e714a7b0fe9eeef2f032d56bc2efd76fc9b54c` |
| `w1-query-telemetry-unit-ordered.json` | 3,182 / 3,182 | `81ce52d329f3251f4b4841cc712dd3c35ecf3eeeb2264b5c2f6d68f5422b0c07` |
| `w1-query-telemetry-memory.json` | 221 / 221 | `47b842b2b0f247dfb73d8ce76d68fcba95bd99badc15c51b0b0d594d41070b6f` |
| `w1-query-telemetry-sqlite-file.json` | 528 / 528 | `80c168a19759eb3abc87462786878d63526ca56db60260d52d37abf9d9273d42` |
| `w1-query-telemetry-sqlite-memory.json` | 528 / 528 | `2bcfe96eb2347bf453167a16a87b43186e6d06f62894e71d59cee660a10621d1` |

## Remaining work

This change adds internal async bookkeeping; its allocation/coordination cost has not been measured or waived. The prior synchronous preflight improvement does not offset that cost. The final six-lane W0 comparison, remaining allocation attribution, full I/O-path mapping, internal adapter readiness and broad W1 closeout are still required.

Continue with command and transaction telemetry, preserving confirmed completion independently from later listener failures. The remaining synchronous/local diagnostic paths, standalone raw-provider attribution, mutation/administrative telemetry and complete cause/stage audit also stay open. W2 native capability/cancellation evidence, W3 public/packed compatibility, W0-F1 official SQLite adoption and release approval remain separate gates.

**Follow-through, 2026-09-20:** [Async mutation telemetry](W1%20Async%20Mutation%20Telemetry.md) brings mutation observers into their owned failure boundary and extracts the shared activity finalizer. All 39 query-telemetry cases pass after that extraction. Command/transaction telemetry and the other listed gates remain open.
