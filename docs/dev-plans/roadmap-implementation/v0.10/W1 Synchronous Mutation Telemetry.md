> [!WARNING]
> Internal 0.10 synchronous mutation integration. W1 is not complete. Native provider adoption, public async APIs, synchronous transaction observer composition and performance acceptance remain separate gates.

# W1 Synchronous Mutation Telemetry

**Recorded:** 2026-09-21, after [synchronous query telemetry](W1%20Synchronous%20Query%20Telemetry.md). This extends the [command reporting boundary](W1%20Command%20Telemetry.md), existing tracked synchronous mutations and the [async mutation reporting contract](W1%20Async%20Mutation%20Telemetry.md). The [completion audit](W1%20Completion%20Audit.md) retains unfinished requirements.

## Failure ownership and measurement

The initial seven regression cases all failed before the runtime changes. Synchronous mutation reporting could replace execution errors with cleanup/listener errors, report success before authoritative hydration and mutable finalization, lose the requested Save identity, and poison an untouched input when an activity failed before command creation.

The [synchronous coordinator](../../../../src/DataLinq/Mutation/Transaction.SyncMutation.cs) now owns the logical mutation from activity start through statement execution, command cleanup, transaction-local cache effects, authoritative hydration, relation keys, mutable lifecycle, successful-change recording and independent reporting. Admission remains held until recovery publication and reporting finish. Statement and hydration retain distinct explicit private steps; callbacks gain no permission to use the transaction. Public signatures, legacy provider dispatch overrides, snapshots and mutable drift checks are preserved.

[StateChange](../../../../src/DataLinq/Mutation/StateChange.cs) preserves the statement/generated-value failure before disposing its command. The first failure remains primary; independent cleanup and counter/affected-row/duration/stop/caller-restoration failures remain ordered secondary facts. Generated-value materialization keeps its stage, and primary cleanup keeps Dispose attribution. Exception objects are not wrapped. The internal statement-only verification seam still omits tracked cache/lifecycle effects.

One admitted mutation records one aggregate count, including sampling/start failure and unchanged Update/Save. Unchanged inputs perform the owned lookup, report zero affected rows and do not invent a statement, successful change or mutable baseline advancement. Physical insert/update/delete stays a telemetry dimension; private statement/hydration and reporting retain the requested Save kind. Empty batches and rejected preflight/admission emit no mutation telemetry.

Counts and meter outcome labels describe the execution/cleanup/local-finalization result known when reporting starts. A later observer error fails the call and can poison a completed write, but cannot retract a delivered measurement. Affected rows are reported only after the statement boundary returns normally; this is not an estimate of effects after an uncertain provider failure. Independent attempts do not promise delivery to every BCL subscriber when an earlier subscriber throws.

The [shared activity helper](../../../../src/DataLinq/Execution/ExecutionActivity.cs) restores the caller, including after throwing CurrentChanged/stop callbacks, and never reinstates a stopped caller. Synchronous queries now delegate to that same helper. The mutation metric collector retains the enclosing diagnostic scope, with separate scopes for fallible callbacks. Reused exceptions cannot import old invocation completion or cleanup facts.

## Recovery and compatibility boundaries

Matching command-specific no-dispatch evidence is consumed immediately at the dispatch catch, before disposal can replace the direct exception lookup. Mutation activity sampling/start failure likewise establishes that this input never reached command creation. After successful cleanup and assessment, an untouched input keeps its baseline and separate earlier successful operations remain usable.

Within the existing synchronous Insert sequence, a later item's mutation/command-start failure must still poison an already written prefix. Only affected mutables are invalidated; the undispatched later input remains new. This is a narrow reporting-safety correction, not conversion of synchronous IEnumerable consumption into the async finite-batch capture/reservation contract. Existing synchronous enumeration, local-edit and preflight exception semantics remain unchanged.

Legacy custom command preparation has no I/O-free capability contract, so construction/drift errors remain conservatively poisonous absent matching no-dispatch evidence. The legacy synchronous contract still permits a rollback attempt after an unclassified write failure; it does not establish safe continuation or native transaction integrity. Optional provider assessment occurs after cleanup. Failed assessment, cleanup, reported integrity loss, or failed/disposed initialization restrict recovery to disposal. Initializer state inspection errors are secondary recovery failures. Unchanged-input hydration cannot upgrade a stricter child read assessment.

The first broad run exposed two diagnostic-precedence regressions: commit was blocked but returned the generic recovery error instead of the established TransactionPoisonedException. Poison rejection now precedes the generic recovery check, consistent with read/mutation guards, while rollback retains its recovery checks.

Callback helpers preserve an escaping mutation failure through independent rollback and cleanup. A callback that catches a safely undispatched, settled failure may still commit under the existing continuation policy. Catching a post-write failure does not make a poisoned transaction committable. No helper rule is widened to treat every caught, safely recoverable failure as fatal.

## Verification

[58 new nonparallel TUnit cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.SyncMutationTelemetry.cs) use actual activity/meter listeners and controlled synchronous resources:

| Cases | Boundary |
| ---: | --- |
| 7 | Original failure/cleanup/reporting order, sampling/start, finalized success, Save identity and unchanged counts |
| 18 | Insert/update/new Save/existing Save/delete/generated keys, with absent, normal and throwing observers |
| 4 | Exact command-start rejection for generated/non-generated mutations, with successful or failed command cleanup |
| 2 | Later undispatched batch input invalidates the written prefix only |
| 2 | Unchanged hydration/reporting failure without inventing a write |
| 5 | Reused failure/cleanup restriction, stale reporter facts, primary cleanup, stopped caller and empty/rejected work |
| 10 | Trusted/lost/initialization/null/throwing provider assessment with and without dispatch |
| 4 | Caught helper failure policy and escaping failure through rollback/cleanup |
| 2 | Activity.CurrentChanged failure during start or restoration |
| 1 | Earlier separate successful operation remains committable after a settled undispatched failure |
| 3 | Failed/disposed initialization and throwing state inspection cannot advertise rollback |

The initial captures are **0/7**, then **7/7**, with the first broad run **3,347/3,349** exposing the two precedence regressions. The expanded **38/38** capture ran in Debug; it is not described as Release evidence. The **51/53** boundary run had two incorrect helper expectations: already-settled caught failures are not replayed as helper secondary failures, and trusted no-dispatch recovery permits a successful callback to commit. Those assertions were corrected to the existing policy; no runtime relaxation was made for them. Subsequent **55/55**, **254/254** reporting and **3,397/3,397** unit captures passed.

Final review reproduced the initialization recovery error in **0/3** additional cases. After the fix, **58/58** focused, **257/257** combined reporting and **3,400/3,400** unit tests passed. A final visibility cleanup kept the shared mutation-kind helper private; the complete captures repeat all **257** reporting cases and the full **3,400** unit cases on that source. Memory passes **221/221** and SQLite file/memory **528/528** each: **4,677** broad passes, no skips. Focused cases are included in the unit total. Core Release builds for .NET 8/9/10 and unit/Memory/compliance builds have zero warnings and errors.

Every listed JSON has complete counts, invocation and artifacts. Passing captures exit zero; failed captures exit two. ValidForEvidence=false denotes bounded local verification, not release qualification. Reports remain local and unmodified under artifacts, unuploaded. Exact final-head Latest CI and the expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-sync-mutation-telemetry-negative.json` | 0 / 7 | `3906e099d3b16fd4dc3ec0d27909dca3b8fde4c313b0e82bec2800347889eddb` |
| `w1-sync-mutation-telemetry-first.json` | 7 / 7 | `5016f512793e3ebf86f8534ac2b4dfecff7d5d6a2d2e17734216b5bb7b64728f` |
| `w1-sync-mutation-telemetry-unit-first.json` | 3347 / 3349 | `ff7b276aa42788ba237e03cc0cf10105a006185f5782e2e45233a25372cdf20b` |
| `w1-sync-mutation-telemetry-expanded.json` | 38 / 38 | `d1831424ee8450792d3160f7f79a7b51bbbfe64a81b04f4c49505fd12c346f52` |
| `w1-sync-mutation-telemetry-boundaries.json` | 51 / 53 | `97989d3599cf4ef7c1cbd18ceea7dffa3bddd654282aeb623a3285b747cd2a55` |
| `w1-sync-mutation-telemetry-final.json` | 55 / 55 | `b1e76b13e54d943da2ee838c2c4d2bae88cbe0fc909a6c01782f610d9f8198a0` |
| `w1-sync-mutation-telemetry-all-reporting.json` | 254 / 254 | `796573b33a7ef5652b116efc07d5ce669f33b4815fa2268e1c1de29ad21dc426` |
| `w1-sync-mutation-telemetry-unit.json` | 3397 / 3397 | `998839bce3997f7c826ff107f1669b12817d1bc260beeeef7df385712b9dcbcc` |
| `w1-sync-mutation-telemetry-initialization-negative.json` | 0 / 3 | `e94a1e4ab95e6a95cda519bcf91ee7959cefb5da9f303b3b47a360a705429de8` |
| `w1-sync-mutation-telemetry-verified.json` | 58 / 58 | `94d05089f284df300a5211792ddc079497cad520c8b3fb5acc31b78d6e28e828` |
| `w1-sync-mutation-telemetry-all-reporting-verified.json` | 257 / 257 | `130855de1b81b5768b7324976242914e8e3c9b82064db48d17e1070cb0422f8d` |
| `w1-sync-mutation-telemetry-unit-verified.json` | 3400 / 3400 | `cbad7cfeb0382bd7a7619a6273720b409102a1ae501cf3d02e4d3658dcfb0337` |
| `w1-sync-mutation-telemetry-all-reporting-complete.json` | 257 / 257 | `e615b82fb1980b1e15a87efe94367ed841a0f01fcc276570948e1465484af1d5` |
| `w1-sync-mutation-telemetry-unit-complete.json` | 3400 / 3400 | `0c3719930f1f0bbc04004c578e152cd46ccdf868108b9d6f21cb138fbfabd328` |
| `w1-sync-mutation-telemetry-memory.json` | 221 / 221 | `ed4695748337933c424ef2709f5673f52c47ed3587ef60485c03b724ad0b6b8a` |
| `w1-sync-mutation-telemetry-sqlite-file.json` | 528 / 528 | `8c0447fed8487aa07a2548492e403b850c5df7fe711dd73da4fb5acdce7ebc49` |
| `w1-sync-mutation-telemetry-sqlite-memory.json` | 528 / 528 | `2c3f088c617493a28f73d78d2a01de58674dec98edc1ddff1612ffdeaac9c732` |

## Remaining work and cost

Synchronous transaction notification/reporting composition and completion kinds are next. Higher local/preflight failures, complete cause/stage/recovery correlation, administrative telemetry, standalone raw-provider attribution, internal adapter readiness and the final cross-family I/O audit remain W1 work. Native provider binding remains W2; public/generated/packed consumers remain W3.

This change adds logical-mutation coordination and diagnostic scopes, including on the successful path. Failure collectors remain lazy, but that is not an allocation measurement. The earlier [allocation checkpoint](W1%20Mutation%20Preflight%20Allocation%20Reduction.md) predates this runtime. Attributable cost analysis, reductions or explicit explanations and all six strict .NET 10 W0 lanes are still required for the final integrated candidate. No performance increase, W0-F1 limitation or 0.9.2 compatibility requirement is waived. No packages are published.
