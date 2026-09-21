> [!WARNING]
> W1 provider-boundary reporting and controllable async evidence only. Native async adoption, synchronous transaction observer composition and W1 closeout remain open. Synchronous SQL logical queries and tracked mutations are covered by the subsequent [query](W1%20Synchronous%20Query%20Telemetry.md) and [mutation](W1%20Synchronous%20Mutation%20Telemetry.md) slices.

# W1 Command Telemetry

**Recorded:** 2026-09-20, after [async transaction telemetry](W1%20Async%20Transaction%20Telemetry.md). This follows the [owned command](W1%20Owned%20Async%20Commands.md), [raw eager command](W1%20Async%20Raw%20Commands.md), [raw reader](W1%20Async%20Raw%20Reader%20Lifetimes.md) and [scoped attribution](W1%20Scoped%20Failure%20Attribution.md) contracts. The [completion audit](W1%20Completion%20Audit.md) remains authoritative for unfinished W1 requirements.

## Reproduced failures and reporting ownership

The protected synchronous `DatabaseAccess.ExecuteCommandWithTelemetry` previously reported success inside its execution `try`. A success-listener failure entered the execution-error handler and attempted another command count. Error reporting or activity disposal could replace the original provider exception. If execution had returned a reader, a subsequent reporter failure prevented ownership from reaching the caller without disposing that reader. Start-listener failures could escape without local classification or activity closure. All six initial regression cases failed before the production changes.

The [existing protected entry point](../../../../src/DataLinq/Database/DatabaseAccess.cs) keeps its signature and direct synchronous execution. Its [owned implementation](../../../../src/DataLinq/Database/DatabaseAccess.CommandTelemetry.cs) separates activity creation/start, provider execution, reporting and unreturned-reader cleanup. The new internal async scalar/non-query and reader hooks use the same [command telemetry record](../../../../src/DataLinq/Execution/CommandExecutionTelemetry.cs), await explicit async execution and independently await cleanup. They do not acquire admission, open connections, initialize transactions, retry, or provide a synchronous fallback.

The [telemetry recorder](../../../../src/DataLinq/Diagnostics/DataLinqTelemetry.cs) updates aggregate execution counts once, then independently attempts the command counter and duration histogram. [Activity finalization](../../../../src/DataLinq/Execution/ExecutionActivity.cs) independently handles tags, stopping and restoration after a throwing stop listener. Original execution/cancellation failures stay primary. Later reporting and cleanup failures retain encounter order and identity; reporting failures are local-finalization failures, while cleanup keeps its distinct stage and safety facts even if it reuses the primary exception object.

Failure collectors retain the enclosing reporting scope while individual observers and cleanup calls have separate scopes. A regression reproduced loss of a fresh reader-cleanup classification when the collector instead retained an earlier sibling observer scope. The shared activity finalizer also now creates its failure collector with the enclosing scope.

The physical SQL verb remains a telemetry dimension. It does not turn a requested `Save` into `Update`, grant ownership or establish transaction completion. Lower command diagnostics leave the requested operation unspecified when they do not own it; the enclosing explicit owner supplies its actual operation. Provider and attached transaction IDs are retained without adding SQL, parameters or resource objects to diagnostic snapshots.

## Measurement and resource boundaries

One command report describes one provider-dispatch attempt, including an attempt that fails. Sampling/start failure and cancellation before dispatch emit no command count. Timing begins immediately before invoking the provider execution delegate and ends after execution or reader acquisition settles. It does not expand to connection opening, transaction initialization, later row reads, reader lifetime or owned cleanup. Providers must place the async hook at that same boundary. Logical query and mutation counts remain separate.

Counts and meter outcome labels describe the provider result known when reporting begins. A subsequent observer failure can fail the operation and mark its activity failed, but cannot retract an earlier measurement. A stop listener can fail after tags were observed. BCL listener delivery to every subscriber is not guaranteed after a subscriber throws.

After successful acquisition, a reader belongs to the command boundary until reporting succeeds and it can be returned. Reporting failure disposes that unreturned reader once; async disposal receives no canceled request token and must settle before the enclosing owner disposes its command or releases admission. Cleanup errors remain secondary to the reporting primary. Successfully returned readers retain their normal lifetime, and neither the borrowed command nor arbitrary scalar results become owned by telemetry.

Existing synchronous native adapters call the unchanged protected entry point and therefore use its corrected implementation. Their surrounding native connection/setup/completion code is unchanged. The async hooks are exercised by an opt-in controllable adapter; production async binding and native resource/cancellation/override fidelity remain W2 requirements.

## No-dispatch evidence and recovery

The first expanded run passed 25/28 cases. Its three failures showed that sampling/start rejection or cancellation during start prevented native execution, but the outer mutation coordinator still treated entering the adapter as dispatch and poisoned valid prior work.

[Command dispatch evidence](../../../../src/DataLinq/Execution/CommandDispatchEvidence.cs) now records whether this hook invoked its provider delegate. This is an immutable, failure-only fact carried through [diagnostic snapshots](../../../../src/DataLinq/Execution/ExecutionFailure.cs). An opaque weak-key identity matches the exact borrowed command without retaining it. Evidence from another command, an earlier invocation, or an unscoped lookup cannot prove no dispatch. No successful-command identity allocation, SQL-text inference, mutable global dispatch flag or admission privilege is introduced.

[Owned commands](../../../../src/DataLinq/Execution/OwnedCommandExecution.cs), [borrowed eager commands](../../../../src/DataLinq/Execution/AsyncEagerCommand.cs) and [borrowed reader sources](../../../../src/DataLinq/Execution/IAsyncReaderSource.cs) consume matching current evidence before cleanup/recovery. An undispatched mutation retains its baseline and does not become a write. Raw sources can retain pre-dispatch reuse. A previously written batch prefix still poisons the transaction if a later command cannot start; the undispatched input retains its own baseline. A dispatched mutation whose reporting fails remains subject to the existing poisoning rules.

Optional provider assessment still runs. Assessment failure or cleanup failure still restricts recovery to disposal; no-dispatch evidence does not bypass either. Cancellation arriving after native success does not retroactively undo that result. A further boundary regression proved that cancellation already present on entry to the hook also needs scoped no-dispatch evidence, without creating an activity.

The [shared async adapter validation](../../../../src/DataLinq/Execution/AsyncDatabaseAccess.cs) also supplies this evidence if its I/O-free checks reject execution or observe cancellation before the provider core. Two further regression cases reproduced lost no-dispatch knowledge at this boundary. Ordinary validation still precedes cancellation; null-command rejection and explicit provider capability requirements are unchanged.

## Verification

[The 44 new nonparallel TUnit cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.CommandTelemetry.cs) use actual activity/meter listeners and explicit provider/cleanup checkpoints:

| Cases | Covered boundary |
| ---: | --- |
| 7 | Synchronous reader/scalar/non-query primary preservation, duplicate-report prevention, start failure, unreturned reader cleanup and cleanup-secondary attribution |
| 6 | Async reader/scalar/non-query success with and without observers; dimensions, privacy, original command identity and ownership |
| 3 | Async execution failure plus counter/duration/stop failures retains the primary and exact secondary order |
| 2 | Paused or failed unreturned-reader cleanup settles before command disposal/admission release; fresh cleanup classification survives |
| 3 | Sampling/start/cancellation before mutation dispatch preserves its baseline and reuse |
| 3 | Cancellation during provider execution stays primary through reporting |
| 3 | Validation precedes pre-cancellation; neither emits command telemetry |
| 1 | Reporting failure after dispatched Save retains requested identity and poisoning |
| 3 | Cancellation after confirmed command success preserves scalar/non-query results and reader handoff |
| 3 | Borrowed/owned raw readers and borrowed eager commands retain pre-dispatch reuse |
| 3 | Dispatch evidence rejects stale, different-command and unscoped observations |
| 1 | Reused reporter/cleanup exception retains cleanup safety without duplicate secondary entries |
| 1 | Cancellation at async-hook entry supplies no-dispatch evidence without an activity |
| 1 | Later command-start failure cannot commit an already-written batch prefix |
| 2 | No-dispatch evidence does not bypass failed assessment or owned-command cleanup |
| 2 | Adapter revalidation or cancellation before entering the telemetry hook does not poison undispatched mutation work |

The initial negative capture is **0/6**. The expanded capture is **25/28** before dispatch-evidence integration. The boundary capture is **36/38** before the collector-scope and hook pre-cancellation fixes. The adapter-validation capture is **42/44** before extending no-dispatch evidence through the shared adapter checks. Final command cases pass **44/44**. The combined query/mutation/transaction/command telemetry run passes **141/141**. Broad final verification passes **3,284 unit + 221 Memory + 528 SQLite file + 528 SQLite memory = 4,561**, no skips; focused cases are included in the unit total. Release core builds for .NET 8/9/10 and unit/Memory/compliance builds have zero warnings/errors.

All listed reports have complete counts, invocations and artifacts. Final runs exit zero; deliberate failure captures exit two. `ValidForEvidence=false` identifies bounded local development checks, not release qualification. Raw reports remain local under `artifacts/`, unmodified and unuploaded. Final-head Latest CI and the expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-command-telemetry-negative.json` | 0 / 6 | `fe992cde10348c5f8b52ab143ce3c9655effb26deb162f204d4fb1afb6f6ad24` |
| `w1-command-telemetry-expanded.json` | 25 / 28 | `e95ccb3eb7f7b3eb82840b3774f286e1c9bea8704782c40075e7fc83e8c5bacb` |
| `w1-command-telemetry-boundaries.json` | 36 / 38 | `e8b64e8b3f3d124162a1891abde5d3fa3c31207920c7f300159b7e38e6d7851e` |
| `w1-command-telemetry-validation-race.json` | 42 / 44 | `dd387600c9dc72d5bd05086c291264ba3bdecbffdef5b17d874c7e146d73dd45` |
| `w1-command-telemetry-final-44.json` | 44 / 44 | `e374377673bb27a7fb48cc72b902f9dbaa43f7c79eb889cbc5404268a7b3d09f` |
| `w1-command-telemetry-all-reporting-verified.json` | 141 / 141 | `76dc2ca54c35af7a3282b078d07c56f26034554fed65c6dcc8b7476f4772208d` |
| `w1-command-telemetry-unit-verified.json` | 3284 / 3284 | `c4cefa8fe5ee0b109f0d0f9ff307a889825965951ba2a2f071b0275ad07a038c` |
| `w1-command-telemetry-memory-verified.json` | 221 / 221 | `dda2f3cb547ca72ae46af6fae030c1a09b859a9ff5c482c98f8793910ae4753c` |
| `w1-command-telemetry-sqlite-file-verified.json` | 528 / 528 | `2125ab6cd7f6d7caad16cd947e76de62f09d9072974496ec38b94f00b4256e4d` |
| `w1-command-telemetry-sqlite-memory-verified.json` | 528 / 528 | `50728d212204bf706e5a9aae034b08aa709924b25cc7913d8c0fd7c6c78bdec0` |

## Remaining work

[Synchronous mutation reporting](W1%20Synchronous%20Mutation%20Telemetry.md) subsequently integrates exact command no-dispatch evidence, tracked finalization and requested Save attribution, including written-prefix and failed-initialization restrictions. [Synchronous transaction reporting](W1%20Synchronous%20Transaction%20Telemetry.md) subsequently composes native certainty, managed finalization, startup failure and independent cleanup/reporters. Remaining consumers' use of dispatch evidence, higher local diagnostics, full requested-operation attribution, administrative boundaries and complete cause/stage mapping remain open. This shared command helper does not claim to repair every outer synchronous provider cleanup path. Native async adoption remains W2; public and packed declarations remain W3.

The diagnostic scopes and async coordination require attributable cost review. No new performance capture or waiver is claimed here; earlier benchmarks do not qualify this runtime. Remaining allocation work, all six strict W0 lanes, final I/O-path mapping, internal adapter readiness and broad W1 closeout remain required. The W0-F1 official SQLite dependency gate and 0.9.2 compatibility baseline are unchanged. No packages are published.
