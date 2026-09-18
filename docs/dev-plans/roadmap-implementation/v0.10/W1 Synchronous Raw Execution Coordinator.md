> [!WARNING]
> Internal W1 orchestration evidence. Built-in public synchronous adapter binding and override compatibility remain open. Raw-model failure reporting is covered by the subsequent recovery slice linked below. This is not a claim that native public raw entry points now share managed admission.

# W1 Synchronous Raw Execution Coordinator

**Date:** 2026-09-18. This follows [private synchronous dispatch](W1%20Synchronous%20Owned%20Command%20Dispatch.md) and the [async raw command](W1%20Async%20Raw%20Commands.md)/[reader](W1%20Async%20Raw%20Reader%20Lifetimes.md) slices. The [full W1 audit](W1%20Completion%20Audit.md) remains open.

## Execution contract

Internal `DatabaseAccess` synchronous raw cores now bind string or borrowed commands through `ISyncRawCommandFactory`. Binding, command construction and capability validation must be I/O-free; first-use provider work belongs to the initialization contract. The factory supplies direct synchronous dispatch separately from the public adapter entry point; recursively calling that entry point would attempt new admission. Scalar conversion remains an explicit adapter policy.

`SyncRawCommand` atomically reserves a single captured invocation before entering cleanup-owning execution. A second or concurrent attempt fails before it can dispose the first attempt's command. Managed transaction admission covers private initialization, owned command creation, validation, execution, cleanup and typed conversion. Created commands are disposed before a normal eager result or conversion is returned. Borrowed commands retain their identity, timeout and caller ownership. Standalone operations have independent lifetimes.

`LazyTransactionResource` implements the direct synchronous initialization contract using its existing state machine. Sequential sync/async execution shares first-use publication and terminal failed initialization; it does not reopen or retry failed resources. The new synchronous dispatch and cleanup paths contain no async waits, blocking on tasks or thread-pool facade.

Raw success does not add tracked changes, finalize mutables or publish/invalidate model caches. The same managed transaction gate rejects raw/managed/completion overlap. The controllable adapter's public synchronous methods invoke the new cores, while the private hooks from the previous slice dispatch under their supplied step without recursively acquiring admission. Tests exercise both these public test-adapter calls and actual managed mutation/hydration/raw-model success paths.

## Readers and failures

`SyncRawDataReader` retains admission after acquisition, between rows and after EOF until disposal. It guards overlapping advancement, getters and disposal; invalid caller-side getter/position use does not become a managed model failure. It preserves the optional owned-binary-buffer SPI only when the underlying reader supplies it. Reader cleanup precedes owned-command cleanup and lease release. Repeated disposal is harmless.

Internal deferred sequences capture each invocation independently and yield an ephemeral current-position reader. Enumeration owns advancement and cleanup on exhaustion, failure or early exit. A helper waits for active synchronous acquisition/advancement, closes admission, drains an escaped reader using its actual synchronous cleanup, and cannot commit an unfinished callback. Eager failures arising during helper draining are reported before their lease is released. Tests use dedicated threads to drive genuinely blocking synchronous work; production does not offload it.

Failures retain original exception identity, ordered secondary failures and the cleanup-failure flag even when execution and cleanup throw the same exception object. Recovery is published only after cleanup settles and before releasing admission. Provider evidence can establish valid rollback, but a post-dispatch raw failure cannot acquire the ordinary-read/no-statement reuse exception. Stronger initialization-failure evidence is retained. Validation or command construction before dispatch preserves otherwise valid prior work; cleanup or evidence-assessment failure still prevents reuse. The transaction rejection text now says execution failed without incorrectly describing synchronous failures as asynchronous.

## Verification

Release / .NET 10 passed **44/44 `SyncRawCommands_*` cases**, maximum parallelism 8 (`artifacts/w1-sync-raw-focused.json`). Earlier coverage stages passed 27/27 and 42/42 (`w1-sync-raw-focused-initial.json`, `w1-sync-raw-focused-expanded.json`). The final cases include concurrent reuse of one captured invocation without premature winning-command disposal.

Coverage includes owned/borrowed eager commands and returned readers, typed conversion and cleanup admission, EOF/position/overlap behavior, deferred/repeated sequences, raw-effects claims, pre/post-dispatch failures, ordered reader/command/assessment failures, terminal/null/capability validation, pending tracked work, private mutation/hydration and raw-model success, mixed sync/async admission and initialization, helper draining, standalone lifetimes and optional binary ownership.

Broad local results:

- **2,544/2,544 unit tests**, maximum parallelism 16 (`w1-sync-raw-unit.json`).
- **210/210 Memory tests**, maximum parallelism 16 (`w1-sync-raw-memory.json`).
- **527/527 compliance tests on each SQLite anchor**, maximum parallelism 8 (`w1-sync-raw-sqlite-file.json`, `w1-sync-raw-sqlite-memory.json`).
- Core .NET 8/9/10, unit/dependencies, compliance and Memory-test Release builds: zero warnings/errors. Build logs use the `artifacts/w1-sync-raw-` prefix.

All **3,808** broad cases are complete, passing, with no skips and complete artifacts. Local `ValidForEvidence` remains false: canonical release evidence additionally requires complete provider scope and clean runner/checkout identity. The PR records exact-head CI and merge evidence. Planning links and whitespace are checked; normal docs and website presentation/navigation are unchanged.

## Remaining integration

These cores are wired to public methods on controllable adapters, not yet to the built-in SQLite/MySQL public methods. Those methods currently call existing virtual command overrides, including string-to-command forwarding; MySQL initialization also issues a `USE` command. Binding must preserve supported overrides and command lifetimes without nested admission, ambient privilege or command-tagged bypass authority. Default custom-provider behavior remains unchanged. Green legacy compliance tests prove regression compatibility of this slice, not adoption of raw admission by those adapters.

Synchronous raw-model streams prove successful private dispatch here. The subsequent [raw-model recovery slice](W1%20Synchronous%20Raw%20Model%20Recovery.md) integrates conservative failure publication through materialization and cleanup. Complete telemetry/correlation and built-in public adapter adoption remain open. Native async feasibility and public/packed declarations remain W2/W3 gates; this coordinator does not supply that evidence or close AAPI-59.

Async tracked mutations/batches, relation coordination/publication, metadata/provisioning, owning-root disposal and final I/O-map/performance evidence remain W1 work. The .NET 10 performance target, 0.9.2 baseline and SQLite W0-F1 limitation are unchanged.
