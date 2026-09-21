> [!WARNING]
> Internal 0.10 raw-execution diagnostics. W1 is not complete. Native provider evidence, public declarations and performance acceptance remain separate gates.

# W1 Raw Execution Diagnostics

**Recorded:** 2026-09-21, after [administrative session diagnostics](W1%20Administrative%20Session%20Diagnostics.md). This supplies bounded internal [AAPI-91 through AAPI-95](Async%20Public%20API%20Decisions.md#aapi-91-failure-context-is-an-immutable-diagnostic-snapshot) evidence for the [completion audit](W1%20Completion%20Audit.md).

## Captured standalone identity

The internal sync/async raw coordinators now carry the provider instance ID already captured by [DatabaseAccess](../../../../src/DataLinq/Database/DatabaseAccess.cs). A managed transaction remains authoritative. Standalone execution retains its actual provider ID, or explicitly establishes that it has none; it cannot inherit another provider's ID, operation or active-operation marker from a nested report.

This applies to eager non-query, scalar and typed scalar calls, returned readers and deferred row sequences. Returned readers retain the captured string through later row and cleanup failures. No new live provider reference is retained. [ReadExecutionIdentity](../../../../src/DataLinq/Execution/ReadExecutionIdentity.cs) derives authority from its existing operation/provider values without adding a struct field: an explicit operation may establish absent provider identity, while a default identity leaves attribution to a more specific child. The lower [command telemetry boundary](../../../../src/DataLinq/Database/DatabaseAccess.CommandTelemetry.cs) also establishes actual or absent identity without guessing a raw operation for query/mutation callers.

Original exceptions and prior snapshots remain unchanged. Nontransactional completion stays NotApplicable, recovery stays None, and no transaction ID is invented.

## Synchronous dispatch evidence and recovery

[SyncRawCommand](../../../../src/DataLinq/Execution/SyncRawCommand.cs) now observes current command-specific no-dispatch evidence when the native dispatch wrapper throws, before owned cleanup drops the command. Telemetry startup failure can therefore preserve pending tracked work without falsely claiming that the raw statement ran. Reader, scalar and non-query dispatch cover owned and borrowed commands, returned readers, row sequences and model sequences.

An old report for the same command, or a current report for another command, cannot establish no dispatch. After actual raw dispatch, recovery remains conservative regardless of a provider's OrdinaryRead claim. Raw commands still do not perform tracked mutation bookkeeping.

No dispatch does not bypass optional failure assessment, initialization restrictions, lost transaction integrity or failed cleanup. Provider-supplied rollback availability is retained. Two existing raw-model expectations were adjusted from Continue|Dispose to Continue|Rollback|Dispose for reserved pre-dispatch failures with explicit provider evidence; rejected capture/admission still performs no assessment. Those tests now assert assessment counts as well as successful later commit and unchanged pending work.

## Independent cleanup and assessment occurrences

Actual owned command/reader cleanup and failure assessment now have separate diagnostic scopes in the [sync raw publisher](../../../../src/DataLinq/Execution/SyncRawExecution.cs), [sync returned reader](../../../../src/DataLinq/Execution/SyncRawDataReader.cs), [async eager publisher](../../../../src/DataLinq/Database/DatabaseAccess.AsyncCommands.cs), [async returned reader](../../../../src/DataLinq/Execution/AsyncRawDataReader.cs) and [async row enumerator](../../../../src/DataLinq/Execution/AsyncReaderEnumerable.cs). The shared [sync reader cleanup](../../../../src/DataLinq/Execution/SyncRawModelEnumerable.cs) and managed [legacy owned-reader cleanup](../../../../src/DataLinq/Database/OwnedCommandDataReader.cs) keep reader and command occurrences separate.

A reused exception cannot import an earlier sibling's timeout, attempted operation or unrelated secondary failures. Reports actually produced by the current cleanup/assessment still retain their operation. Cleanup preserves the original work failure, remains ordered and sets the independent cleanup restriction. Failed assessment stays Unknown/Recovery, uses the current requested operation as fallback and restricts recovery to Dispose. No nonexistent reader/command receives a cleanup scope; resources are settled once, borrowed commands remain borrowed, and admission is released after reporting.

## Verification

Four new TUnit files cover [standalone identity](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.RawDiagnostics.cs), [synchronous telemetry dispatch](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.RawDispatchDiagnostics.cs), [failure occurrences](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.RawOccurrenceDiagnostics.cs) and [dispatch/late-reader controls](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.RawDiagnosticControls.cs).

| Cases | Boundary |
| ---: | --- |
| 44 | Standalone owned/borrowed eager and reader acquisition identity, plus lower sync/async telemetry with actual or absent provider |
| 24 | Sync telemetry before/after dispatch across six raw shapes and both command ownership modes |
| 4 | No-dispatch recovery restrictions from cleanup, assessment, initialization and lost integrity |
| 24 | Stale/current cleanup and assessment reports through sync/async scalar, returned-reader and row-sequence paths |
| 2 | Independent reader/command cleanup in the managed legacy owned-reader helper |
| 4 | Stale or wrong-command no-dispatch evidence must not permit reuse |
| 14 | Late row/disposal failures with actual/absent provider identity, including mixed sync disposal of an async reader |

The retained controls show **2/44** before standalone identity fixes, **60/72** before synchronous dispatch evidence, **85/96** before occurrence isolation, and **115/116** before isolating the legacy owned command's cleanup. The first expanded full unit run was **3,635/3,637**: both failures were the rollback-availability expectations described above. Its report is retained separately from the corrected final run; no reports were overwritten.

The final implementation passes **116/116** focused cases and **3,637/3,637 unit**, **221/221 Memory**, **528/528 SQLite file** and **528/528 SQLite memory**: **4,914 broad local passes**, no skips. Core, SQLite and MySQL Release builds cover .NET 8/9/10; unit, Memory and compliance builds also have zero warnings/errors.

Reports retain complete invocation/count/artifact information under local artifacts, unmodified and unuploaded. Passing reports exit zero; control reports exit two. ValidForEvidence=false denotes bounded local checks, not release qualification. No source edits or benchmarks overlapped local builds/tests. Planning docs are excluded from DocFX; local links, anchors and report hashes are verified. Exact-head Latest CI and the expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-raw-diagnostics-dispatch-negative.json` | 60 / 72 | `bafddea039486f1f0f4c5f4fae3e61b040109b1eb310dab96800658c3fae09ee` |
| `w1-raw-diagnostics-dispatch.json` | 72 / 72 | `b73c1499b9b2d0f10f065be0c2429170283bdbc33b935841f086295192ff5c3f` |
| `w1-raw-diagnostics-expanded.json` | 115 / 116 | `19000fd7434ed8161f33ed16c599ff66ed9ef63d8bc0ee7f02a6c4991e6be751` |
| `w1-raw-diagnostics-final-unit.json` | 3637 / 3637 | `eb82bd95896cad3140638af93652604bef14cf7e2fbb6c3e4f1ca7089227e9a5` |
| `w1-raw-diagnostics-focused.json` | 116 / 116 | `318491a556c70b6e6329fa90eae1c5e59ea198c4ae4b35cbbe7db2a213d687b1` |
| `w1-raw-diagnostics-identity-unit.json` | 3565 / 3565 | `3f84a665914bed836cbf6c51cc77a4cb71b9da5ef0e868f68a5e4bba948b7e19` |
| `w1-raw-diagnostics-identity.json` | 44 / 44 | `61103aa356dcbc826ddd0e3b23734ddd944f621e31defb7a5945ba1aeec644ed` |
| `w1-raw-diagnostics-memory.json` | 221 / 221 | `8edc0093493efee419bd0daa0b254756efdef4a94bbbae84b3da74ea617b0b2e` |
| `w1-raw-diagnostics-negative.json` | 2 / 44 | `6da9ef17bbcd05e01d43e29dd14960a2d59056d169a153c45d8ff367dad781fb` |
| `w1-raw-diagnostics-occurrence-negative.json` | 85 / 96 | `bed988fde5a19b199f79da75ece078d4b92780e28dc43b3232d2cca6fd702cb9` |
| `w1-raw-diagnostics-sqlite-file.json` | 528 / 528 | `9d0ec7e7523ac46c9a4686bf2bba8cf1d48fe03dd55e9c8d89155604deaffc6e` |
| `w1-raw-diagnostics-sqlite-memory.json` | 528 / 528 | `e9e507cbfab5ae46d694e1be719ca0d772184da960c6daf6b0e5163f6d90fd53` |
| `w1-raw-diagnostics-unit.json` | 3635 / 3637 | `455c17df8bbbd78b51910f6b4d0a2ceaf404e114045e37715de1a4879bb56045` |

## Remaining audit and costs

The async evidence adapters still need a separate check that no-dispatch normalization preserves an underlying Initialization effect or Lost integrity. This slice's four explicit restriction controls exercise the synchronous consumer; they are not proof for all async adapters. The complete cause/stage/preflight inventory, higher local boundaries, final cross-family I/O map and internal adapter readiness also remain open.

Each actual cleanup now establishes an occurrence scope, including successful cleanup; classifiers establish scopes on failure paths. Shared reader helpers affect more than raw calls. Returned readers retain one extra string reference, and synchronous dispatch adds failure-only evidence observation. These costs are unmeasured here. Attributable coordination-cost work and all six strict .NET 10 W0 comparison lanes remain mandatory. Native classification and adapter fidelity remain W2, public declarations/consumers remain W3. W0-F1 and the 0.9.2 compatibility baseline are unchanged; no packages are published.
