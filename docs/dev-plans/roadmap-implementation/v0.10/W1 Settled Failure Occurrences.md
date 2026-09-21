> [!WARNING]
> Internal 0.10 failure-reporting evidence. W1 is not complete. Native provider behavior, public diagnostics and performance acceptance remain separate gates.

# W1 Settled Failure Occurrences

**Recorded:** 2026-09-21, following [no-dispatch recovery restrictions](W1%20No-Dispatch%20Recovery%20Restrictions.md). This supplies bounded occurrence and recovery evidence for the [completion audit](W1%20Completion%20Audit.md); it does not close the full inventory.

## Separate later work from earlier exception reports

An exception object can be thrown more than once within one operation. A report attached during earlier work must not classify a later conversion, cleanup, evidence assessment or recovery-policy failure. Conversely, a report produced inside the current boundary must survive. Each affected call now observes its catch inside a child diagnostic scope, while the enclosing failure collector remains anchored to the original operation so later sibling reports remain visible.

[Async scalar reads](../../../../src/DataLinq/Execution/AsyncScalarRead.cs), [buffered reads](../../../../src/DataLinq/Execution/AsyncBufferedRead.cs), [synchronous raw execution](../../../../src/DataLinq/Execution/SyncRawExecution.cs) and [async raw eager execution](../../../../src/DataLinq/Database/DatabaseAccess.AsyncCommands.cs) isolate local conversion after owned resources have settled. Buffered reads also isolate actual reader disposal, including borrowed-command readers. Borrowed commands remain undisposed by the coordinator.

Optional failure evidence assessment is isolated in scalar/buffered reads, [tracked mutations](../../../../src/DataLinq/Mutation/Transaction.AsyncMutation.cs) and [relation completion](../../../../src/DataLinq/Cache/TableCache.AsyncRelations.cs). If that assessment itself throws, its secondary cause/stage remain Unknown/Recovery; a current nested attempted-operation identity can survive. Assessment failure cannot authorize Continue or erase a written prefix. Earlier cleanup attachments cannot invent a cleanup failure in the current assessment.

Recovery-policy getters are different from optional classifiers: their current nested diagnostic cause/stage remain meaningful. [Managed completion/disposal](../../../../src/DataLinq/Mutation/Transaction.AsyncCompletion.cs), the [callback runner](../../../../src/DataLinq/Execution/TransactionCallbackRunner.cs) and [mutation-helper failure cleanup](../../../../src/DataLinq/Mutation/Transaction.AsyncMutationHelper.cs) now scope each getter evaluation and retain the requested operation as its fallback. Automatic rollback and owned cleanup policy are unchanged.

## Capture initialization failure before cleanup

[Lazy transaction initialization](../../../../src/DataLinq/Execution/LazyTransactionResource.cs) captures the primary failure and its exception dispatch information before attempting cleanup. It then records cleanup in an independent scope and publishes a snapshot with actual owner transaction/provider identity and Initialization stage. This preserves the initial cause and stack even when cleanup throws the very same exception object. Identity deduplication does not remove the cleanup restriction or current nested cleanup details.

Started initialization failure remains terminal and unpublished. Existing resource policy is retained: successful failure cleanup clears ownership; failed cleanup retains the resource for a later disposal attempt. That later disposal is attempted once, rather than silently leaking the retained resource or retrying initialization.

## Verification

Three TUnit files add 40 cases:

| Cases | Boundary |
| ---: | --- |
| 8 | [Post-cleanup conversion](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.SettledOccurrences.cs): sync/async raw scalar, fluent scalar and buffered completion; stale versus current reports |
| 10 | Scalar, buffered, Save, Delete and unchanged-Save evidence assessment; original primary, attempted operation, write-dependent poisoning and rejection after failed assessment |
| 2 | Actual captured single-reference relation completion after a successful buffered read, reaching the outer relation assessment directly |
| 2 | Buffered borrowed-command reader cleanup; one reader disposal, no command disposal |
| 10 | [Initialization](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.InitializationOccurrences.cs): sync/async, distinct/reused exception, stale/current cleanup reports, original stack, actual owner identity, terminal failure and later retained-resource disposal |
| 8 | [Recovery-policy getters](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.RecoveryPolicyOccurrences.cs): commit, callback, failed helper binding and explicit disposal; stale/current reports, no unintended rollback and once-only owned cleanup |

The staged negative controls are retained, not presented as one untouched-baseline run. Initial assessment/cleanup controls were **6/14**, expanded conversion controls **10/22**. After their fixes, initialization controls produced **26/32**, then recovery-getter controls **36/40**. A final stack assertion exposed **four** same-instance initialization regressions (**36/40**) even after metadata assertions passed: bare rethrow propagated the cleanup stack. Rethrowing through the already captured dispatch information fixes those cases without changing exception identity.

Final focused results are **40/40**. Final broad results are **3,707/3,707 unit**, **221/221 Memory**, **528/528 SQLite file** and **528/528 SQLite memory**: **4,984 passes**, no skips. Core, SQLite and MySQL Release builds cover .NET 8/9/10; unit, Memory and compliance builds also have zero warnings/errors.

Reports remain local, unmodified and unuploaded. ValidForEvidence=false denotes bounded local verification, not release qualification. Failing reports exit two; passing reports exit zero. The initial test draft had two compile errors, corrected before its first negative test report; it is not counted as behavioral evidence. No source edits or benchmarks overlapped local builds/tests. These planning docs are excluded from DocFX; local links, anchors, report counts and hashes are checked instead. Exact-head Latest CI and expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-settled-occurrences-expanded-negative.json` | 10 / 22 | `b9c2a6740ca18c427549755352c21879b9abf2b0f086f26f5c55e223653032fa` |
| `w1-settled-occurrences-final-focused.json` | 40 / 40 | `7ffec3ea9340239d9aea642179bea2d47ac369e85372db9fb56816eb527b196d` |
| `w1-settled-occurrences-final-unit.json` | 3707 / 3707 | `76693cf3da47da47f6d74af45ac91fc82a6ae91f65b3ee52d5d9d80c5efb8597` |
| `w1-settled-occurrences-first.json` | 22 / 22 | `1f611534fbb71a275b57a5d27caef6dec5052192ab9de1b46227b3c597e5738a` |
| `w1-settled-occurrences-focused.json` | 40 / 40 | `1045cd3073f6e47ae95e1d6b86f914b2b4f877da6bdd84cc5448f931bf6af1ea` |
| `w1-settled-occurrences-initialization-first.json` | 32 / 32 | `5a5105673c65dd158056c42dfdf1daff1da1e3d78dd2ff5652cc4f4ef25dee0e` |
| `w1-settled-occurrences-initialization-negative.json` | 26 / 32 | `09c563b02f3888d6bce4194cf0682691ebbb609f56a7164f353f4a4bdb01c7ec` |
| `w1-settled-occurrences-memory.json` | 221 / 221 | `7563ee24b8e0855ab69a34a7a68ed158164a5bfd7b929bdf33efd02713deca9b` |
| `w1-settled-occurrences-negative.json` | 6 / 14 | `47327ad4dcfee6cd3e585b416f51e5db100afca73c8402a2cfc3e7f1dc6b0533` |
| `w1-settled-occurrences-recovery-negative.json` | 36 / 40 | `aebb3b9a69fc24c26ff382450709d5220abe658e849ff15a5f7cf11df508e94d` |
| `w1-settled-occurrences-sqlite-file.json` | 528 / 528 | `f672d0d7685984118a3601d1fab2aaccff6089135cc2a2a8a72a2b9408d65c53` |
| `w1-settled-occurrences-sqlite-memory.json` | 528 / 528 | `65fe227e9d974fe898f73e0f1ee2d4e588f1d9d9f1dcd8be203b04a4b5e649fe` |
| `w1-settled-occurrences-stack.json` | 36 / 40 | `4367f00f7c0c5d45be94b5311fcc27e1c8e1d06ef8fd3adbff005cfff9b37561` |
| `w1-settled-occurrences-unit.json` | 3707 / 3707 | `889cc626e600e6e532d947b906401704e7c5dea0ca067fe7151f6259934bc0a1` |

## Cost and remaining work

The subsequent [materialization occurrence slice](W1%20Materialization%20Failure%20Occurrences.md) addresses row advancement/conversion, nested transforms and post-reader relation completion using report checkpoints, and retains captured continuation recovery through late observers. Its 30 cases and 5,014 broad local passes extend this checkpoint without closing the full inventory or performance gates.

This change adds successful-path scopes around conversion and actual buffered cleanup, and around recovery-policy evaluation where reached. Async raw conversion also retains a nullable scope through its catch/finally. The resulting allocation and async-state costs are **unmeasured**; this is not a failure-only or zero-cost change. Failure-only assessment/initialization capture does not establish a cost waiver for the successful paths.

The complete local/preflight cause/stage/correlation inventory, remaining per-row and relation completion boundaries, higher synchronous orchestration, cross-family I/O mapping, internal adapter readiness and final requirement-to-code/test/merged-PR audit remain open. All six strict .NET 10 W0 lanes and measured coordination/scope costs remain mandatory.

Native provider evidence/binding remains W2, public declarations and packed consumers remain W3, and release acceptance is separate. W0-F1's limited internal exception and the published 0.9.2 compatibility baseline remain unchanged. No packages are published.
