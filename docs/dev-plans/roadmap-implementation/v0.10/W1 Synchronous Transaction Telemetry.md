> [!WARNING]
> Internal 0.10 synchronous transaction integration. W1 is not complete. Native async adoption, public async APIs and performance acceptance remain separate gates.

# W1 Synchronous Transaction Telemetry

**Recorded:** 2026-09-21, after [synchronous mutation telemetry](W1%20Synchronous%20Mutation%20Telemetry.md). This extends the [async transaction reporting contract](W1%20Async%20Transaction%20Telemetry.md) to existing synchronous completion. The [completion audit](W1%20Completion%20Audit.md) retains unfinished requirements.

## Completion and failure ownership

The initial seven regression cases all failed. The protected synchronous transaction hooks could replace native completion errors with listener errors, stop reporting incompletely, and obscure a confirmed database outcome when a completion listener failed.

The [internal synchronous coordinator](../../../../src/DataLinq/Database/DatabaseTransaction.SyncCompletion.cs) now records native confirmation before fallible status observers. SQLite and MySQL/MariaDB use direct synchronous commit/rollback and independently attempt connection close, connection disposal and transaction disposal. A confirmed outcome survives observer or cleanup failure. Uncertain commit remains unknown after later rollback, and disposal does not explicitly retry an already attempted rollback. The native provider may perform its own cleanup internally; this is not a promise about every action inside its Dispose implementation.

[Managed completion](../../../../src/DataLinq/Mutation/Transaction.cs) holds admission and an explicit private step through cache/mutable finalization and reporting. A validated private scope defers lower reporting until the managed boundary finishes; its flag schedules reporting and grants no I/O authority. Confirmed native commit continues local finalization even if a lower observer or resource cleanup failed. A failed cache publication retains TransactionCommitFinalizationException, its original inner exception and recovery cleanup failures, invalidates affected mutable state and does not publish a successful managed status.

The first failure remains primary, with independent later failures recorded in encounter order. Provider reports are captured immutably before callbacks can reuse exception objects. Cleanup remains a safety fact even when the same exception is also thrown by a listener. Requested Commit/Rollback/Dispose identity is preserved; a primary resource cleanup failure is specifically attributed to Dispose. Later recovery updates the transaction ledger without rewriting an earlier exception snapshot.

Native transaction counts describe confirmed database completion independently of local or observer success. Activity/meter callbacks are attempted independently, exactly once per started lifetime. Reporting follows owned native cleanup and, for managed completion, local finalization. Uncertain native commit/rollback failure closes reporting once; later disposal does not reopen that lifetime. Measurements already delivered cannot be retracted, and a throwing BCL subscriber can prevent delivery to subsequent subscribers.

## Startup and compatibility boundaries

The protected synchronous start hook owns its activity before Start callbacks, attempts the start counter independently and makes failed telemetry startup terminal. Built-in owned resources are all cleaned before failed-start completion reporting. Managed reads and completion cannot reuse that failed initialization. Caller activity is restored after sampling/start/stop/CurrentChanged failures, including completion under a later caller; stopped callers are not reinstated.

This specifically repairs telemetry startup after native begin. It does not claim to redesign general synchronous connection/begin failure publication. The async first-use hook retains its separately documented cleanup timing; production native async binding remains W2.

Public and protected method signatures remain unchanged. Built-in adapters use an internal resource interface, with friend access from core to SQLite and MySQL. Their duplicate linked copies of internal GuidCodec, EffectiveColumnTypeResolver, StringExtensions and LinqExtensions are removed; they now consume the core definitions. This adds no public helper types or members. All three library projects build for .NET 8/9/10, but final packed-consumer/ApiCompat acceptance remains W3.

Legacy custom providers retain their protected telemetry hooks and override dispatch. They do not acquire the built-in native-certainty contract merely by setting Status before throwing; ambiguous custom commit failures remain conservative. The raw DatabaseAccess/IDbTransaction escape hatch still bypasses managed ownership and mutable/cache finalization. This slice is not full raw-handle admission adoption.

## Verification

[41 new nonparallel TUnit cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.SyncTransactionTelemetry.cs) use actual activity/meter listeners, controlled native resources and real SQLite:

| Cases | Boundary |
| ---: | --- |
| 7 | Original native failure, independent reporters, failed start and real SQLite confirmed completion |
| 10 | Provider/managed status failures, mutable finalization, native failure/recovery, independent cleanup and terminal real SQLite startup |
| 2 | Reporting follows managed finalization and resource cleanup |
| 11 | Later/stopped caller, Dispose identity, immutable earlier snapshots, stale reporters and real SQLite tracked commit/rollback |
| 6 | Failed-start cleanup failures, committed cache/recovery failure and CurrentChanged callbacks |
| 5 | Standalone cleanup timing, implicit-rollback failure identity and deduplicated cleanup/reporting exceptions |

The initial **0/7** became **7/7**. The first broad **3,406/3,407** capture exposed an incorrectly advertised rollback after a legacy custom commit-status failure. The **12/17** expansion exposed missing rollback observer composition, incomplete recovery publication and reusable failed startup. Those failures were corrected before **17/17** and **3,417/3,417** passed.

The **0/2** finalization reproduction proved reporting ran too early; the private managed completion scope fixes this, with **19/19**, **276/276** combined reporting and **3,419/3,419** unit passes. The **28/30** disposal capture exposed incorrect Dispose attribution and failure to advance a prior NotAttempted ledger after confirmed implicit rollback. Subsequent captures pass **30/30** and **36/36**. The **37/41** standalone expansion exposed three early-reporting cases and implicit-rollback failure attribution; both are fixed in the final source.

Final verification passes all **298/298** combined reporting cases (including all 41 new cases), **3,441/3,441** unit, **221/221** Memory and **528/528** SQLite file and memory each: **4,718** broad passes, no skips. Focused reporting is included in the unit total. Core, SQLite and MySQL Release builds cover .NET 8/9/10; unit, Memory and compliance builds also finish with zero warnings/errors. Local builds/tests did not overlap benchmarks or source edits. Server-backed provider verification belongs to the exact-head Latest CI recorded in the PR, not these local SQLite captures.

All reports below retain complete invocation/count/artifact information. Passing reports exit zero; failed reports exit two. ValidForEvidence=false means bounded local verification, not release qualification. Reports remain unmodified under local artifacts, unuploaded. The exact final-head CI and expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-sync-transaction-telemetry-negative.json` | 0 / 7 | `55718d6ca46c81f35fe4a27b04d035931b3b3725b56b58a545ed5061420b9d15` |
| `w1-sync-transaction-telemetry-first.json` | 7 / 7 | `bf85ed24f48c71623d56bebc227a0ec0733f13ffaa2ba529954ef3596af968df` |
| `w1-sync-transaction-telemetry-unit-first.json` | 3406 / 3407 | `1145d3d535ae1176d9dec7e6c61b6385620b7f318b4e6dcd76b21d9e41a1dc1e` |
| `w1-sync-transaction-telemetry-expanded.json` | 12 / 17 | `7e525102ec9bd85481ec584a64c6757249f77dfced0219359dfb57e84b83ad2e` |
| `w1-sync-transaction-telemetry-integrated.json` | 17 / 17 | `06f3ba2f7c8bdacafb03c11bbf3a5cd685dbb1b54c035d70ca866ff5915701e7` |
| `w1-sync-transaction-telemetry-unit-integrated.json` | 3417 / 3417 | `784a7185e4d6f63f55c56470026a65b3d16cc3ccbfbcd2f22a555dcf3748a36e` |
| `w1-sync-transaction-telemetry-finalization-negative.json` | 0 / 2 | `bf729587a551ed8b907219bd91254b65bae2206f705786cfe878773667c1d4bc` |
| `w1-sync-transaction-telemetry-finalization.json` | 19 / 19 | `24cbce2e8193d50fbbca231d672adaa50346e7ae44efcd24f06a51ccc16320b0` |
| `w1-sync-transaction-telemetry-all-reporting-first.json` | 276 / 276 | `62609c19ff67a4ccafd33dca9532b7c44d8c0e93563170a35292086eafa8f5b6` |
| `w1-sync-transaction-telemetry-unit-finalization.json` | 3419 / 3419 | `cb04853ce34d6ea165dc849c689aa4479569faa3c8cf93d6e9e0d6e61b21c26e` |
| `w1-sync-transaction-telemetry-disposal.json` | 28 / 30 | `c39c28c437f92c1dce4727c78ccf79f9e2b1a08e0549c34c8d355374f8a399bc` |
| `w1-sync-transaction-telemetry-disposal-fixed.json` | 30 / 30 | `9c81ba3e681dfd9b5f61d7842a9cbae0736b45b2232d5f067a8f33b67d78d6d6` |
| `w1-sync-transaction-telemetry-resources.json` | 36 / 36 | `da79d260518ccf651b498ddac10de47ad938d14f9c121820c1bec2d6efcafc13` |
| `w1-sync-transaction-telemetry-standalone.json` | 37 / 41 | `8bd1e61f4d3fde233733f1bbf9e7be5a1b85c3c282b8e7334eb4e4db946874be` |
| `w1-sync-transaction-telemetry-all-reporting-verified.json` | 298 / 298 | `4518b6779906cb70931417380c157b18a11fb0be8f88236633d3f8ea41fd5245` |
| `w1-sync-transaction-telemetry-unit-verified.json` | 3441 / 3441 | `378bf03c63bd192bf94e703d7a672c72dd2f3c9bbedfbbaf2d8f5e0b94a22cd0` |
| `w1-sync-transaction-telemetry-memory.json` | 221 / 221 | `f786b7a6efa2461252d589b62c12a29f24af18ac5758339021d919a895babf3a` |
| `w1-sync-transaction-telemetry-sqlite-file.json` | 528 / 528 | `60b28500b09d48377880daeda817f878925fe03d227b0b63a9b9a6bff039132f` |
| `w1-sync-transaction-telemetry-sqlite-memory.json` | 528 / 528 | `ae42edfdecde6e3ca8a35ab96d9acce6c413b05085943873e4d2221f58d680de` |

## Remaining work and cost

Higher synchronous local/preflight diagnostics, complete cause/stage/recovery correlation, administrative telemetry, standalone raw-provider attribution, internal adapter readiness and the final cross-family I/O audit remain W1 work. This closes the bounded synchronous transaction observer-composition work above; it does not close those wider requirements.

Completion now allocates diagnostic scopes, explicit private steps and failure collectors on successful paths as well. These costs have not been measured or waived. The earlier [allocation checkpoint](W1%20Mutation%20Preflight%20Allocation%20Reduction.md) predates this runtime. Attributable cost analysis, reductions or explicit explanations and all six strict .NET 10 W0 lanes remain required for the final integrated candidate. W0-F1, the 0.9.2 compatibility baseline and W2/W3 acceptance gates remain unchanged. No packages are published.
