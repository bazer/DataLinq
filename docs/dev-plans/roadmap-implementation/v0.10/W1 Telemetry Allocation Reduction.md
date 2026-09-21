> [!WARNING]
> This is a measured cost reduction, not W1 completion or performance acceptance. Five allocation warnings and smaller increases remain; F19 and F21 stay open.

# W1 Telemetry Allocation Reduction

**Date:** 2026-09-21. Follows the [cross-family diagnostic review](W1%20Functional%20and%20IO%20Audit.md#cross-family-diagnostic-review), integrated at `7352e50a80b11af080f66d17ccd86dcfeb0fe289`. The measured runtime change is `60f79f6c3d2eb721fa3a035e7fdb1c226c792071`; this document's PR must record its final source head, CI and merge result. The frozen [W0 performance baseline](W0%20Baseline%20Evidence.md) remains `7e36614b5f26f1dd199ff93bc60620f4b0714d5f`. Published compatibility remains 0.9.2.

## Problem and bounded change

The complete nine-row allocation-regression run at `7352e50a` has six allocation warnings: CRUD batch/small +21.9%, cold primary key +26.7%, cold relation +31.9%, update +16.3% and warm primary key +15.1% versus W0. Its recorded workload telemetry is unchanged. Earlier performance checkpoints therefore do not describe the current integrated runtime.

Diagnostic EventPipe allocation sampling of the same four primary-key/relation/update workloads identified diagnostic scope/execution-context objects and metrics factory delegates as contributors. The probe used one unmeasured invocation followed by 5 warm-key, 20 cold-key, 20 cold-relation or 10 update invocations, excluded setup/cleanup from its workload-thread allocation counter and matched work-phase trace markers. Before/after workload checksums agree. These captures live under `artifacts/w1-allocation-probe/`: they are diagnostic sampling, not timing evidence or strict release receipts. Sampled type attribution can cross setup boundaries; thread-local byte counts are not process-wide measurements. They do not assign an exact byte cost to every remaining feature.

Two avoidable costs are removed:

- [DataLinqMetrics](../../../../src/DataLinq/Diagnostics/DataLinqMetrics.cs) uses static stateful `ConcurrentDictionary.GetOrAdd` factories for both provider forms. Existing entries no longer allocate a capturing delegate/closure on every lookup. Key selection, state construction on misses and aggregate counts are unchanged.
- [SyncQueryExecution](../../../../src/DataLinq/Execution/SyncQueryExecution.cs) captures whether an activity listener exists once. If absent, it skips both activity creation and the associated callback scope. [QueryExecutionTelemetry](../../../../src/DataLinq/Execution/QueryExecutionTelemetry.cs) still starts timing and aggregate query counts; measurement listeners enabled later can observe completion. Active activity listeners retain their diagnostic scope, including sampling/start failures. The decision is not cached across queries. Async callers keep their existing default activity behavior.

Admission, provider dispatch, reader cleanup, occurrence checkpoints, report identity and recovery are unchanged. Command telemetry still protects command-property access and callbacks. No diagnostic scope is removed around actual application/provider work. The relevant F11/F20 behavior was reconsidered against the current code and regression results below; this change does not waive the other family contracts.

## Strict end-to-end comparison

All values below are the history's reported B/op. Before and candidate use the same complete canonical nine-workload SQLite-memory lane and their own clean, committed runtime builds.

| Workload | W0 B/op | Before B/op | Candidate B/op | Candidate allocation vs W0 | Candidate time vs W0 |
| --- | ---: | ---: | ---: | ---: | ---: |
| CRUD batch | 65,884.16 | 80,312.32 | 79,206.40 | +20.2% | +8.3% |
| CRUD small | 65,904.64 | 80,332.80 | 79,226.88 | +20.2% | +4.9% |
| Cold primary key | 6,256.64 | 7,925.76 | 7,546.88 | +20.6% | -1.5% |
| Cold relation | 13,352.96 | 17,612.80 | 17,203.20 | +28.8% | -1.0% |
| Provider initialization | 367,247.36 | 353,576.96 | 352,368.64 | -4.1% | -8.2% |
| Startup primary key | 70,133.76 | 58,490.88 | 57,384.96 | -18.2% | -11.5% |
| Update | 15,626.24 | 18,176.00 | 17,827.84 | +14.1% | +4.8% |
| Warm primary key | 1,761.28 | 2,027.52 | 1,812.48 | +2.9% | +0.9% |
| Warm relation | 0 | 0 | 0 | zero bytes | -4.2% |

Warm primary-key allocation falls by 215.04 reported B/op and leaves the warning band, but its remaining +2.9% is not waived. Cold primary-key and relation allocations fall by 378.88 and 409.60 B/op; update falls by 348.16 B/op. The candidate has **five allocation warnings, no latency warnings and no recorded telemetry changes**, with three stable rows and one improved row. Both comparisons remain **ReviewRequired**. Initialization/startup improvements do not offset the other regressions.

Both captures retain BenchmarkDotNet warnings for short update/cold-primary-key iterations and multimodal distributions. Candidate minimum iteration times are 94.603 ms and 60.717 ms respectively; comparison noise ranges from 2.7% to 15.9%. These timing point estimates are not fixed latency guarantees. No environmental explanation for all remaining variation is established.

## Validation and evidence receipts

Release builds for core, SQLite, MySQL and Memory cover .NET 8/9/10 with zero warnings/errors. Unit/dependency, generator, Memory and compliance builds also have zero warnings/errors. Broad results are **3,853 unit + 71 generator + 221 Memory + 528 SQLite file + 528 SQLite memory = 5,201 passes**, no failures/skips.

The [two new controls](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.TelemetryAllocation.cs), `TelemetryAllocation_QueryMeasurementsSurviveSuppressedActivityAndLateListeners`, exercise completion measurements after an explicit no-activity start, a throwing late counter, independent duration reporting, once-only completion, aggregate counts and activities on the next query. The test host has activity listeners, so these are component controls; they do not claim an observer-free TUnit process. The initial four-case test attempt incorrectly asserted that no listener existed and failed that assertion in every case. That test setup failure is retained below, not presented as a product defect or a failing-before/fixed-after runtime proof. The canonical benchmark exercises the real no-observer workload separately.

The final unit TRX also contains all 58 `SyncQueryTelemetry_`, 39 `AsyncQueryTelemetry_`, 18 `DiagnosticCloseout_` and 27 `Correlation_` cases passing. These selected counts overlap the broad total. Existing sampling/start/stop/measurement failures, exception identity and cleanup tests remain in the full suite.

Functional reports under `artifacts/` have complete invocation/artifact evidence, unchanged dirty-checkout provenance at `7352e50a` and matching runner/DevTools assemblies. `ValidForEvidence=false` correctly remains explicit: these are bounded functional checks, not strict release or full-provider qualification.

| Functional report | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-telemetry-allocation-focused.json` (invalid no-listener assumption) | 0 / 4 | `7d555e5afe884b94d020d823b35fa96a1f78bea48c274c7863053e31b46d1596` |
| `w1-telemetry-allocation-focused-2.json` | 2 / 2 | `44b0b8b93715896e303f6408652c981e6409011274ad7714f102f5c141713665` |
| `w1-telemetry-allocation-final-unit.json` | 3853 / 3853 | `018755cf0d1ee78894ebb1a2fb6f5d52caf8705721e026aa3634aebff3ba1b39` |
| `w1-telemetry-allocation-generators.json` | 71 / 71 | `617c38467a538fd7fda863dd6f9415ed3b653afc998b702e3ddbfa0f1639ee20` |
| `w1-telemetry-allocation-memory.json` | 221 / 221 | `cb9f4628c5ceab5a20f46d2794cb91aeeaebd71671eac39605cbfba0b58bf41b` |
| `w1-telemetry-allocation-sqlite-file.json` | 528 / 528 | `5c9edb57ff9bf81e1108707aa14564981fd27dd9881ea6e741132d5a13bd23d9` |
| `w1-telemetry-allocation-sqlite-memory.json` | 528 / 528 | `7e2cd78cb239849ebfc13d556af02e6872543e75a6f51f8ba539ac008d504d6b` |

Both benchmark captures use `--allocation-regression --profile heavy --release-evidence`, no filters or `--no-build`, fresh harness builds, the same W0 baseline and separate history/comparison paths. No edits, other benchmarks, builds or tests overlap either measured run. Each history/comparison is valid, complete and artifact-complete with exit zero; comparisons are comparable. Runner, DevTools and benchmark assembly identities match their clean, unchanged checkouts. All **30 raw artifact receipts** were independently checked for byte length and SHA-256. Recorded environment remains .NET 10.0.12, BenchmarkDotNet 0.15.8, Windows 10.0.26200 x64, eight logical processors, Intel Family 6 Model 140 Stepping 1.

| Capture | Run ID |
| --- | --- |
| Before (`7352e50a`) | `20260921-183216769-763cfeaa4ab14cca8aab2a2464cb8637` |
| Candidate (`60f79f6c`) | `20260921-185706517-ff53cd5fb5d54c6e86cbe3cbc4e9feb4` |

| Receipt under `artifacts/benchmarks/history/` | SHA-256 |
| --- | --- |
| `w1-7352e50a-allocation-regression.json` | `9e7877b644a180b6b0be13fdc304bd81dbf6f312eb050ff218f024217247bc3e` |
| `w1-7352e50a-vs-w0-allocation-regression.json` | `ddd900ffd8bda6cfeb8402bc1eab25a9a003fe200d5c5b29b2db6950e9937606` |
| `w1-60f79f6c-allocation-regression.json` | `4793dd1aeb380bdaca63ed4ee9540e248cee13ba7dac4f93caab2c14cbb89971` |
| `w1-60f79f6c-vs-w0-allocation-regression.json` | `930b4205dd76df4c911901a9e8ec23686c495462fdc2e38596e447fa7dcc88bb` |

Planning pages remain excluded from DocFX; local links/anchors, named tests and receipt values are checked before integration. Final-head CI and expected-head merge/tree verification belong in the PR.

## Remaining work

F19 still needs bounded internal async coordination measurements, explanation/reduction and disposition of the remaining costs, and all six strict W0 lanes on the final committed integration. These two nine-row checkpoints are not that final capture. F21 still requires the complete I/O/integration reconciliation and final evidence review. No sub-threshold increase, timing warning or unrelated improvement silently closes a requirement. The [limited W0-F1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception) is unchanged; native/public/DI/release acceptance belongs to later gates.
