> [!WARNING]
> Internal 0.10 administrative diagnostics. W1 is not complete. Native provider evidence, public declarations and performance acceptance remain separate gates.

# W1 Administrative Failure Classification

**Recorded:** 2026-09-21, after [completion failure classification](W1%20Completion%20Failure%20Classification.md). This supplies further internal evidence for [AAPI-92 through AAPI-95](Async%20Public%20API%20Decisions.md#aapi-92-failure-classification-uses-independent-public-enums); the [completion audit](W1%20Completion%20Audit.md) retains the wider requirements.

## Classify at the failing boundary

The internal [existence-probe coordinator](../../../../src/DataLinq/Execution/AsyncExistenceProbes.cs) now classifies failed scalar interpretation as MaterializationError/Materialization. The [metadata coordinator](../../../../src/DataLinq/Execution/AsyncMetadataRead.cs) uses the same fallback for local parser failures. A current, more specific command report retains its cause and stage. Native failures without sufficient evidence remain Unknown.

These are boundary-based classifications, not exception-type guesses: a local TimeoutException does not prove a database timeout. Matching, requested cancellation remains Cancellation; a foreign cancellation exception from a local interpreter/parser remains the original exception with local materialization attribution. Existing native foreign-cancellation tests still require Unknown. Availability-only failure mapping is unchanged: interpretation, cancellation and cleanup errors cannot become false.

DataLinq's own incomplete-result guard now reports InvalidOperation/Materialization. The [metadata context](../../../../src/DataLinq/Execution/MetadataReadContext.cs) distinguishes overlapping commands as InvalidOperation/Validation and an abandoned active command as InvalidOperation/Materialization. These guards retain their original exceptions, drain active work, preserve later command failures and clean owned resources before returning failure. They do not publish a partial definition or permit retries.

## Scoped logger notifications

[Captured metadata settings](../../../../src/DataLinq/Execution/MetadataReadContracts.cs) retain the original logger before suspension and wrap each invocation in a fresh diagnostic scope. Plain logger failure uses LocalFinalizationError/Notification, consistently with the existing observer helper. A current nested report remains more specific; an earlier invocation's report on a reused exception cannot classify a later throw.

The wrapper rethrows the original exception. Deliberately caught logger failures do not become retained command failures or change successful parser behavior. The outer metadata owner supplies the actual runtime provider identity or the explicitly absent import identity, NotApplicable completion and None recovery. Ordered cleanup failures and immutable earlier snapshots remain intact. No activities, meters, command counts, native adapters or public signatures are added.

## Verification

[26 new TUnit cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.AdministrativeDiagnostics.cs) cover:

| Cases | Boundary |
| ---: | --- |
| 6 | Scalar interpretation for availability/database/table probes, ordinary and timeout exceptions, with independent cleanup failure |
| 4 | Default Option result versus the Option library rejecting null inside the parser, import/runtime |
| 2 | Logger failure with independent session cleanup, import/runtime |
| 4 | Local parser failure, ordinary/timeout and import/runtime |
| 4 | Request/foreign cancellation during local interpretation/parsing |
| 4 | Current versus stale nested logger diagnostics, import/runtime |
| 2 | Reused logger exception across successive notifications, propagated versus deliberately caught |

Six existing [metadata cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.Metadata.cs) now also assert exact causes/stages for overlapping and abandoned reader/scalar commands.

The first 0/12 capture and the subsequent 21/26 capture exposed both implementation gaps and five incorrect test expectations: the shared enumeration helper deliberately excludes TimeoutException, and the Option library rejects a null value at construction rather than returning a null success. These reports are retained, but are not claimed as twelve independent product regressions. Corrected tests pass 26/26. Running those same corrected tests against the previous implementation reproduces 4/26, with 22 diagnostic assertion failures.

After restoring the fix, unit/Memory/SQLite file/SQLite memory pass 3,489/221/528/528. Final review moved logger-wrapper creation behind the null check; the final runtime passes the same **4,766 broad local cases**, no skips. Core, SQLite and MySQL Release builds cover .NET 8/9/10; unit, Memory and compliance builds also have zero warnings/errors.

All reports retain complete invocation/count/artifact information and remain unmodified under local artifacts, unuploaded. Passing reports exit zero; failed reports exit two. ValidForEvidence=false denotes bounded local checks, not release qualification. No source edits or benchmarks overlapped local builds/tests. Planning docs are excluded from DocFX; local links, anchors and report hashes are verified instead. Exact-head Latest CI and the expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-administrative-diagnostics-control.json` | 4 / 26 | `2e2d836b7e7a713a1441299a5c617353de42b5c395a8f8be09836815726517d3` |
| `w1-administrative-diagnostics-corrected.json` | 26 / 26 | `fbcd7a16da19d65126c4c123207953bd408800ff3611f851c818887a1c123469` |
| `w1-administrative-diagnostics-final-memory.json` | 221 / 221 | `9c1cb6826a619f50e291db8e79b3aed014ca9d09f0f45a1aa2e736a3f258d5f9` |
| `w1-administrative-diagnostics-final-sqlite-file.json` | 528 / 528 | `40599209d9bcb46f1b9db98cb30f62ad4852e490ed4e54131dcf9be10cf7428e` |
| `w1-administrative-diagnostics-final-sqlite-memory.json` | 528 / 528 | `345ae739cac035035d7ea1a6992efbc531b33cc46ced6e121226d713dbe73b98` |
| `w1-administrative-diagnostics-final-unit.json` | 3489 / 3489 | `1e4d4584868487ef95be870587af3939c13dd960461e3f6a3cb9f1dbefb213b5` |
| `w1-administrative-diagnostics-first.json` | 21 / 26 | `7267d1fe9620dc5e94e2dcb6196e592636a7c27103b5acebcb3aea01daa8753b` |
| `w1-administrative-diagnostics-memory.json` | 221 / 221 | `194ab8fd967daba1e74b40dc2825a91565f6b4951f4a879d82e3dd1ef1940f69` |
| `w1-administrative-diagnostics-negative.json` | 0 / 12 | `7d229996b1065280a9a61ad8df8a781abd87589f64ba2c4fa6a0eaee292fe0ca` |
| `w1-administrative-diagnostics-sqlite-file.json` | 528 / 528 | `c9fdda7f742327be92cc882cf3f2f62e6fe39bb4c3b51aadc99b2f43ffafa5e3` |
| `w1-administrative-diagnostics-sqlite-memory.json` | 528 / 528 | `c7225d2e7a152d000af8e64e52f961ff947ab9bd52314262f050a29e5c4a3670` |
| `w1-administrative-diagnostics-unit.json` | 3489 / 3489 | `24057488eb9e15775dcf51aa4a5cc904eb06ec5299ecca0ee83d51ce8c136b2f` |

## Remaining requirements and cost

The later [session diagnostics slice](W1%20Administrative%20Session%20Diagnostics.md) closes the tested session-cleanup, availability-classifier and missing-session gaps across probes, metadata, provisioning and journal mode. Its 32 new cases pass within 4,798 broad local passes; the complete classification/I/O inventory and performance gates remain open.

This slice closes the tested local interpretation, complete-result and logger classification gaps only. Administrative session/preflight attribution, standalone provider/raw paths, remaining dispatch-evidence consumers, complete cause/stage mapping and internal adapter readiness still need the final cross-family audit. Native fidelity/classification remains W2; public declarations and consumers remain W3.

A logger wrapper is created only when a logger is configured. Each logger invocation introduces a diagnostic scope; exception snapshots are created on failure. These costs are unmeasured here. Attributable coordination-cost work and all six strict .NET 10 W0 comparison lanes remain required. W0-F1 and the 0.9.2 compatibility baseline are unchanged; no packages are published.
