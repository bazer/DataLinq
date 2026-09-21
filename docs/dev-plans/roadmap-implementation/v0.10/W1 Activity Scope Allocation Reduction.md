> [!WARNING]
> Internal W1 optimization with bounded functional and strict intermediate performance evidence. F19/F21 and final release acceptance remain open.

# W1 Activity Scope Allocation Reduction

**Date:** 2026-09-21. Base: `4dccd64f62ea764db32d5eba77162be6e7f75149`, merged [PR #222](https://github.com/bazer/DataLinq/pull/222). Measured code: `b904a955e75280bf1cb28cd6ca2642300137fddb`. This follows the [allocation-stage review](W1%20Allocation%20Stage%20Review.md), [owned-cleanup correction](W1%20Owned%20Cleanup%20Occurrences.md) and earlier [telemetry allocation reduction](W1%20Telemetry%20Allocation%20Reduction.md).

## Change and preserved behavior

The earlier [coordination measurements](W1%20Coordination%20Measurements.md) established that diagnostic scopes allocate even on successful paths. Inspection found three more activity-only scopes that ran without an activity listener:

- [Synchronous mutation execution](../../../../src/DataLinq/Mutation/Transaction.SyncMutation.cs) now snapshots listener presence once and passes the same decision to [MutationExecutionTelemetry.Start](../../../../src/DataLinq/Execution/MutationExecutionTelemetry.cs). A no-activity start still records its timestamp and starts completion reporting, but skips activity creation and its callback scope. Passing the same decision prevents a listener attached between the check and Start from executing outside that scope. Async mutation callers retain the default activity behavior.
- [Synchronous transaction startup](../../../../src/DataLinq/Database/DatabaseTransaction.SyncCompletion.cs) and [async transaction startup](../../../../src/DataLinq/Database/DatabaseTransaction.cs) now enter their nested activity-creation scope only when a listener exists. Root diagnostic scopes, start flags, timestamps and start/completion counters are unchanged. The context is a captured get-only property and transaction type a nonvirtual property; this callback block contains no provider I/O or custom getter.
- Sampling/start observers still run inside the existing isolation boundary. Their exceptions still prevent undispatched mutation work or terminate failed transaction initialization as appropriate. Completion listeners added later remain observable, and completion is still once-only.

Command telemetry is deliberately unchanged: obtaining a custom command's text can invoke provider/user code even without activity listeners. Dispatch, reader, cleanup, recovery and admission scopes are not removed. There is no pooled diagnostic state, new public API, provider-state sharing or change to workload meaning.

## Functional verification

Six new [late-listener cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.TelemetryAllocation.cs) cover mutation success/throwing counters and synchronous/async transaction startup followed by late completion listeners. `TelemetryAllocation_MutationMeasurementsSurviveSuppressedActivityAndLateListeners` verifies affected-row/duration reporting, aggregate counts, observer-failure classification, unchanged caller activity, once-only completion and a subsequent observed mutation. `TelemetryAllocation_TransactionCompletionSurvivesLateMeterListeners` verifies late commit/duration reports, independent reporting after a counter failure, retained completion facts and once-only aggregate counts. These are telemetry-component controls, not native commit evidence or a claim that TUnit has no activity listeners. Existing sampling/start, reentrancy, poisoning, resource cleanup and caller-restoration tests remain in the broad run.

The focused filter passes **8/8**, including the earlier two query controls. Broad Release / .NET 10 results pass **3,872 unit + 71 generator + 221 Memory + 528 SQLite file + 528 SQLite memory = 5,220 cases**, no failures/skips. Core, Memory, MySQL and SQLite builds cover .NET 8/9/10; these and affected test/dependency builds have zero warnings/errors. Reports have stable dirty-checkout provenance at the stated base, matching runner/DevTools assemblies and complete invocation/artifact records. Their `ValidForEvidence=false` remains explicit. No edits or benchmarks overlapped builds/tests.

| Report under `artifacts/` | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-activity-scope-focused.json` | 8 / 8 | `9dd211dae5ead52f022fb673e5ab2dd100422b834c3e1231380cd7b048deda81` |
| `w1-activity-scope-unit.json` | 3872 / 3872 | `21d05d087584902adb3d15dc3c25082e7a1fac966cac4683951e2314638bd3b1` |
| `w1-activity-scope-generators.json` | 71 / 71 | `d99979b67e15db1b601283579179d16391c32acd4a0a2f57e61ea8d380898a27` |
| `w1-activity-scope-memory.json` | 221 / 221 | `19470f0bd9bcf1b4273cc659b88ea4c32eea5d43e9a8b6fd322a5cb6c4259f46` |
| `w1-activity-scope-sqlite-file.json` | 528 / 528 | `4bcd86036d327ad71bf0e63e1c0d462dfe72335a81220d16b9997ee829600bab` |
| `w1-activity-scope-sqlite-memory.json` | 528 / 528 | `71284c97912054476c6dcb05ac0c033c20ff541a76ed7e53db49f1d0d5e626f8` |

## Measured allocation change

Both clean nine-row `allocation-regression` captures use the frozen W0 comparison, .NET SDK 10.0.401/runtime 10.0.12, BenchmarkDotNet 0.15.8, Windows 10.0.26200 x64, Intel Family 6 Model 140 Stepping 1 and eight logical processors. The heavy profile selects two launches, ten warmups and fifteen measurement iterations. The before code is PR #222 head `849801937189f8265d93edcd206a37f768290a06`, whose tree was verified equal to the merged base; its run takes 276.1263412 seconds. The candidate takes 278.5992368 seconds. Both histories/comparisons are complete, artifact-complete, strict valid evidence and exit zero, with unchanged telemetry and exact nine-row scope. All **30 raw receipts across the two runs** match their lengths/hashes. Each run's live benchmark assembly was verified before later builds, with clean matching runner/target identities and no overlapping edits, builds, tests or other benchmarks.

All rows use SQLite memory. Values below are normalized history values with the frozen lane's KiB display rounding; arithmetic differences are not exact object-size measurements.

| Workload | W0 B/op | Before B/op | Candidate B/op | Allocation vs W0 | Mean vs W0 |
| --- | ---: | ---: | ---: | ---: | ---: |
| CRUD workflow batch | 65884.16 | 79114.24 | 78346.24 | +18.9% | +6.8% |
| CRUD workflow small | 65904.64 | 79339.52 | 78448.64 | +19.0% | -0.9% |
| Cold primary-key fetch | 6256.64 | 7608.32 | 7608.32 | +21.6% | +8.7% |
| Cold relation traversal | 13352.96 | 17162.24 | 17203.20 | +28.8% | -0.4% |
| Provider initialization | 367247.36 | 352389.12 | 352747.52 | -3.9% | -11.3% |
| Startup primary-key fetch | 70133.76 | 57384.96 | 57384.96 | -18.2% | -12.5% |
| Update employees | 15626.24 | 17766.40 | 17571.84 | +12.5% | +6.7% |
| Warm primary-key fetch | 1761.28 | 1812.48 | 1812.48 | +2.9% | -0.4% |
| Warm relation traversal | 0.00 | 0.00 | 0.00 | — | -6.0% |

The affected CRUD rows decrease by **768.00** and **890.88 B/op**, and update by **194.56 B/op**, versus the immediate control. This matches removal of activity-only scopes from mutation/transaction execution; it does not claim those rounded differences are a precise per-scope size. Warm/cold primary-key and startup allocations are unchanged. Smaller movement in provider initialization and cold relation is retained in the table, not credited as part of this optimization.

The candidate has two stable, two improved and **five allocation-warning rows** versus W0, with zero telemetry changes and zero latency warnings. The earlier small-CRUD latency warning (+10.7%) is not reproduced here (-0.9%). That does not prove a fixed timing improvement: reported comparison noise is 1.7–12.6%, and the raw log retains short-iteration warnings for update (92.823 ms) and cold-key fetch (77.58 ms), plus a multimodal warning. `ReviewRequired=true` remains. The full six-lane final capture and disposition of remaining material costs are still required.

| Artifact under `artifacts/benchmarks/history/` | SHA-256 |
| --- | --- |
| `w1-84980193-allocation-regression.json` | `a362e6beec7a2e32de20428953d7cfcacae647aa5d31310e668a291ec4993e69` |
| `w1-84980193-vs-w0-allocation-regression.json` | `d12f5b8a132765330c88074681ac7e36da21066bda54fb2ed102e42f15059d59` |
| `w1-b904a955-allocation-regression.json` | `78b321175e0a801281b7788049e9d4c344e657c3a6c69fad7b4e6ef1b14ef08a` |
| `w1-b904a955-vs-w0-allocation-regression.json` | `d8cd12fa598f4a8cc9e7c395a16dfad6d3d82044157c2ef00be35051d6a4f392` |

Before run ID: `20260921-213438368-77046e31981349778c56d4c360540687`. Candidate: `20260921-214805334-a26801a1061a416ea70c162561961056`. Reproduce through the Benchmark CLI with `run --allocation-regression --profile heavy --release-evidence --history-json <new-path> --baseline artifacts/benchmarks/history/w0-7e36614b-allocation-regression.json --comparison-json <new-path>` after building a clean committed runner. Do not overwrite receipts.

The 48-stage cold typed-ID allocation/timing findings and the five end-to-end allocation warnings are not waived. F19/F21 still own final committed evidence, cost disposition and I/O/integration reconciliation. Published compatibility stays 0.9.2; W0-F1's limited exception and native/public/DI/release gates are unchanged. Planning pages remain excluded from DocFX. Local links/anchors, named tests and original receipt/table values are checked before integration; exact-head CI and guarded merge/tree checks belong in the PR.
