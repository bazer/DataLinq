> [!WARNING]
> Internal 0.10 session diagnostics. W1 is not complete. Native provider evidence, public declarations and performance acceptance remain separate gates.

# W1 Administrative Session Diagnostics

**Recorded:** 2026-09-21, after [administrative failure classification](W1%20Administrative%20Failure%20Classification.md). This extends internal [AAPI-92 through AAPI-95](Async%20Public%20API%20Decisions.md#aapi-92-failure-classification-uses-independent-public-enums) evidence without narrowing the [completion audit](W1%20Completion%20Audit.md).

## Independent failure occurrences

The [probe](../../../../src/DataLinq/Execution/AsyncExistenceProbes.cs), [metadata](../../../../src/DataLinq/Execution/AsyncMetadataRead.cs), [journal-mode](../../../../src/DataLinq/Execution/AsyncJournalMode.cs) and [provisioning](../../../../src/DataLinq/Execution/AsyncProvisioning.cs) coordinators previously disposed their sessions under the outer diagnostic scope. A nested command could catch an exception, retain its diagnostic report and finish successfully; if the session later threw that same object, cleanup could inherit the earlier timeout cause, raw-command operation and unrelated secondary failures.

Each actual session disposal now has its own diagnostic scope. A later cleanup occurrence ignores reports from settled sibling work while preserving a current report produced inside disposal, including across suspension. Original primary exceptions, independent cleanup safety facts, ordered current secondaries, immutable earlier snapshots and actual runtime or explicitly absent factory identity remain intact. Session cleanup is still awaited once with no request token; provisioning cleanup does not remove a created destination.

The captured availability classifier also has an independent scope. A classifier failure cannot borrow an earlier command's diagnostics. It remains secondary to the original availability failure and cannot turn the operation into false. Current nested reports remain available. The existing eligibility rules and successful availability-only mapping are unchanged.

## Known missing-session guards

All four coordinators now identify their own missing-session guard as InvalidOperation/Validation. This uses a local classification set only when the captured plan actually returns null. An arbitrary InvalidOperationException thrown by provider construction remains the original exception with Unknown cause; exception type alone is not evidence.

No resource ownership transfers when session creation fails or returns null, so the coordinator does not open or dispose a nonexistent session. The creator still owns cleanup of partially constructed resources it cannot return. Existing capture, validation-before-pre-cancellation and no-synchronous-fallback rules are unchanged. This does not promise structured context for every ordinary argument check.

## Verification

[Administrative occurrence tests](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.AdministrativeOccurrences.cs) and the [provisioning extension](../../../../src/DataLinq.Tests.Unit/Core/AsyncProvisioningTests.AdministrativeOccurrences.cs) add 32 TUnit cases:

| Cases | Boundary |
| ---: | --- |
| 20 | Probe, runtime metadata, import metadata, journal mode and provisioning cleanup; successful/failed work; stale/current nested reports |
| 2 | Availability classification with stale versus current nested reports |
| 10 | Missing session versus an arbitrary provider construction error of the same exception type, across the five entry shapes |

The first 16/32 capture also revealed a test-fixture issue: the probe fixture silently replaced a deliberately returned null session with its default. Correcting that fixture produced a new **16/32 control** in which all 16 failures are diagnostic assertions. Those unmodified reports are retained separately. The final tests additionally suspend cleanup after failed work, verify the operation remains pending and its command is already disposed, then release the cleanup failure.

The fixed implementation passes **32/32** focused cases and **3,521/3,521 unit**, **221/221 Memory**, **528/528 SQLite file** and **528/528 SQLite memory**: **4,798 broad local passes**, no skips. Core, SQLite and MySQL Release builds cover .NET 8/9/10; unit, Memory and compliance builds also have zero warnings/errors.

Reports retain complete invocation/count/artifact information under local artifacts, unmodified and unuploaded. Passing reports exit zero; control reports exit two. ValidForEvidence=false denotes bounded local checks, not release qualification. No source edits or benchmarks overlapped local builds/tests. Planning docs are excluded from DocFX; local links, anchors and report hashes are verified. Exact-head Latest CI and the expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-administrative-occurrences-control.json` | 16 / 32 | `1f108f9a3b5f37a12c15084afe36d17f8c971c853a5ad352ef60fb78d6d43662` |
| `w1-administrative-occurrences-first.json` | 32 / 32 | `8391fdfacdcba688c082b97e428cab6a7f86ed88f9b8a38d45153f2ca1a1399b` |
| `w1-administrative-occurrences-memory.json` | 221 / 221 | `8d75038594cae53bbb204c3ff95245a0a9291232b5d4dae29c06ad3936412e73` |
| `w1-administrative-occurrences-negative.json` | 16 / 32 | `6bd72c22b07750a895afba763e15b580a1d5564b34f21ffccfc86f3fc61af5d8` |
| `w1-administrative-occurrences-sqlite-file.json` | 528 / 528 | `f4660e53a669f8cc9a8da3c01f870e8e12267c9a25ddc34bfa3ed214ede9cb4b` |
| `w1-administrative-occurrences-sqlite-memory.json` | 528 / 528 | `e3cf7a2e7c37db0c731051eb057a8bea0a11a22259b468a279b7da5712414240` |
| `w1-administrative-occurrences-unit.json` | 3521 / 3521 | `aa9a104ae1f36095f60f755703b53decd1ef65d050518dc84e0ae36a98fcaaa0` |

## Remaining audit and costs

This closes the tested administrative session-cleanup, availability-classifier and missing-session gaps. It does not close the complete preflight/cause/stage inventory, standalone provider/raw attribution, remaining dispatch-evidence consumers, internal adapter readiness or the final cross-family I/O mapping. Native provider fidelity/classification remains W2; public declarations and consumers remain W3.

Each owned session disposal now creates a diagnostic scope, even when cleanup succeeds; an eligible classifier invocation also creates one. No disposal scope is created without a session. These costs are unmeasured here. Attributable coordination-cost work and all six strict .NET 10 W0 comparison lanes remain mandatory. W0-F1 and the 0.9.2 compatibility baseline are unchanged; no packages are published.
