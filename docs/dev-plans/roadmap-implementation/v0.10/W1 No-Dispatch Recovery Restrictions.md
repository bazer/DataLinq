> [!WARNING]
> Internal 0.10 recovery evidence. W1 is not complete. Native provider behavior, public declarations and performance acceptance remain separate gates.

# W1 No-Dispatch Recovery Restrictions

**Recorded:** 2026-09-21, following [raw execution diagnostics](W1%20Raw%20Execution%20Diagnostics.md). This closes the specific async normalization gap identified there and extends internal [AAPI-94](Async%20Public%20API%20Decisions.md#aapi-94-recovery-flags-describe-actions-valid-at-reporting-time) evidence. It does not narrow the [completion audit](W1%20Completion%20Audit.md).

## Preserve stronger provider restrictions

Proving that the current command never ran cannot establish that the transaction is healthy when provider assessment says otherwise. Five async adapters previously replaced both effects and integrity with NoStatement/Confirmed on their no-dispatch paths, erasing an Initialization effect or Lost integrity and allowing Continue.

The failure-only [ReadFailureEvidence.WithNoDispatch](../../../../src/DataLinq/Execution/ExecutionFailure.cs) helper now preserves Initialization effects and their integrity, preserves Lost integrity for other effects, and retains rollback availability and cause. Only the established local cancellation path explicitly supplies Cancellation as the cause. Ordinary no-dispatch evidence still permits Continue when cleanup/assessment succeed and no stronger restriction exists.

The helper is used by [async eager commands](../../../../src/DataLinq/Execution/AsyncEagerCommand.cs), [owned commands](../../../../src/DataLinq/Execution/OwnedCommandExecution.cs), [borrowed readers](../../../../src/DataLinq/Execution/IAsyncReaderSource.cs), and the initializing [reader](../../../../src/DataLinq/Execution/InitializingTransactionReaderSource.cs) and [scalar](../../../../src/DataLinq/Execution/InitializingTransactionScalarSource.cs) adapters. The previously corrected [synchronous raw consumer](../../../../src/DataLinq/Execution/SyncRawCommand.cs) uses the same rule. The separate synchronous mutation policy already guards Initialization/Lost and is unchanged.

Optional assessment still runs. No dispatch fact is inferred from SQL text or an open connection; the existing captured command-specific evidence and local command-start state remain the prerequisites. Failed cleanup/assessment, known failed lazy initialization, conservative post-dispatch raw recovery and written-prefix restrictions are unchanged.

A published lazy resource may be Ready while command/provider assessment reports stricter failure evidence. Ready does not override that assessment. Cancellation before the command handoff remains the original OperationCanceledException with its matching token, while recovery can correctly be Dispose-only.

## Verification

[No-dispatch restriction tests](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.NoDispatchRestrictions.cs) add 30 TUnit cases:

| Cases | Boundary |
| ---: | --- |
| 24 | Async raw non-query, scalar, returned reader and row sequence; owned/borrowed commands; unrestricted, Initialization and Lost evidence; telemetry startup fails before native dispatch |
| 6 | Reader/scalar cancellation after successful lazy initialization; unrestricted, Initialization and Lost evidence; a direct classifier prevents lower command adapters from masking an incorrect initialization-wrapper policy |

The unmodified control is **10/30**. All 20 restricted cases failed the exact recovery assertion because the old implementation returned Continue|Rollback|Dispose instead of Dispose. The ten unrestricted controls already passed. The fixed implementation passes **30/30**, preserves pending raw-call work, performs no native dispatch or unacquired-reader cleanup, assesses once, settles owned commands once and leaves borrowed commands alone. Restricted transactions reject subsequent Query; unrestricted ones admit it.

Final broad local results are **3,667/3,667 unit**, **221/221 Memory**, **528/528 SQLite file** and **528/528 SQLite memory**: **4,944 passes**, no skips. The unit run includes the 116 preceding raw-diagnostic cases and the existing command/transaction/mutation/initialization regression suites. Core, SQLite and MySQL Release builds cover .NET 8/9/10; unit, Memory and compliance builds also have zero warnings/errors.

Reports remain local, unmodified and unuploaded, with full invocation/artifact metadata. ValidForEvidence=false denotes bounded local verification, not release qualification. The negative control exits two; passing reports exit zero. No source edits or benchmarks overlapped local builds/tests. These planning docs are excluded from DocFX; links, anchors and report counts/hashes are verified. Exact-head Latest CI and expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-no-dispatch-restrictions-first.json` | 30 / 30 | `16ed213b1381c08e7fe275cac0c0bb83792b3b559ceba4b6b87c48b0334ac295` |
| `w1-no-dispatch-restrictions-memory.json` | 221 / 221 | `2b4b779ce7668f8fe5883e00996e92eaa553b256b623dd52621237f897d09e36` |
| `w1-no-dispatch-restrictions-negative.json` | 10 / 30 | `a56f8d81e7ead7a3f1d77f95527fcc5f3f99f3e55e03ae32d144463bfb41e7cc` |
| `w1-no-dispatch-restrictions-sqlite-file.json` | 528 / 528 | `401dec47dd51ed82e62c751ae3cee632de3e423c45ab2bcae423393c9abf2a81` |
| `w1-no-dispatch-restrictions-sqlite-memory.json` | 528 / 528 | `7ef4d7f30566e7d8ccfcc127030bdaf4571a67a89389b09eec417e02d29dfab6` |
| `w1-no-dispatch-restrictions-unit.json` | 3667 / 3667 | `327c5783e2f62a85aad2e553f537bf64eeb3f8bea75ab346d149b60038556c3c` |

## Remaining W1 gates

The helper replaces existing failure-path record copies; it adds no successful-path call, scope or resource capture. This is not a new performance measurement. The complete occurrence/cause/stage/preflight inventory, eager read and relation assessment boundaries, final cross-family I/O mapping, internal adapter readiness and measured coordination costs remain open. All six strict .NET 10 W0 performance lanes and final requirement-to-code/test/merged-PR audit remain mandatory.

This does not establish native provider classification/fidelity (W2), public declarations/consumers (W3), or release acceptance. W0-F1's limited internal exception and the published 0.9.2 compatibility baseline remain unchanged; no packages are published.
