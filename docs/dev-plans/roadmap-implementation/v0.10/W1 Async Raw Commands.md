> [!WARNING]
> Internal W1 orchestration evidence. Native provider adoption and public async declarations remain W2/W3 gates.

# W1 Async Raw Commands

**Date:** 2026-09-18. This integrates the eager lower-level command families under AAPI-56/58/59 with the existing transaction admission, initialization, cleanup and helper machinery. It extends [owned async commands](W1%20Owned%20Async%20Commands.md) and [managed scalar reads](W1%20Fluent%20Keys%20and%20Scalars.md). The [full W1 audit](W1%20Completion%20Audit.md) remains open.

## Internal execution boundary

`DatabaseAccess` now has internal scalar, typed-scalar and non-query entry points for SQL strings and borrowed `IDbCommand` instances. `IAsyncEagerCommandFactory` binds one invocation without I/O, capturing the selected explicit async access, optional initializer, owned command factory and provider scalar-conversion policy. It does not guess native support from `DbCommand` inheritance or invoke synchronous execution. Strings remain immutable statement text; borrowed commands retain their identity, parameters, timeout and caller ownership rather than being cloned or modified.

Each managed transaction binds its adapter to the same wrapper that owns its execution gate. The eager entry points validate lifecycle, recovery state and capability before pre-cancellation. Admission covers initialization, command execution, owned cleanup, scalar conversion and failure publication. Concurrent managed reads, mutations, completion and other raw eager calls fail before dispatch. Private internal dispatch accepts an explicit step from that same transaction, validates it and does not reacquire admission. Wrong-transaction and retired steps are rejected; callbacks and scalar converters receive no ownership token. Unbound transaction adapters cannot silently run this path without a managed owner.

`AsyncEagerCommand` reuses `OwnedCommandExecution` for created commands. Owned cleanup is asynchronous, uncancelable and must succeed before any normal result; borrowed commands are never disposed by this path. A late cancellation request after successful execution and cleanup does not undo the result. Typed conversion runs locally after cleanup and under the same owner, preserving the bound provider's null/`DBNull` policy rather than introducing generic coercion.

`LazyTransactionResource` supplies the private initialization capability. Initialization is completed before command creation/dispatch, published once on success, and never retried after a started failure. Validation and pre-cancellation do not initialize or create resources. A cancellation observed after successful initialization but before dispatch leaves a ready transaction reusable; interrupted initialization is drained while private and permits only disposal. Existing synchronous initialization remains direct.

## Failure and tracking policy

Dispatch is recorded by the command invocation, not inferred from SQL text or result shape. After raw scalar/non-query dispatch, a failure cannot acquire the ordinary-read exception that permits continued transaction use. Even an `OrdinaryRead` or `NoStatement` effect claim from the provider cannot restore `Continue` at this raw boundary. Confirmed provider recovery evidence may still permit rollback; lost integrity, cleanup failure or failed evidence assessment permits disposal only.

Pre-dispatch rejection preserves otherwise valid prior work. Actual command validation/construction failures clean up any owned resource before recovery is evaluated. Failure snapshots retain the original exception and ordered cleanup/assessment failures, including the safety fact that cleanup failed when the provider throws the same exception twice. The context and helper failure report are published before releasing admission. Helpers await unfinished command execution and cleanup and reject callback success without committing.

Successful raw execution does not create tracked changes, hydrate generated values, finalize mutables or invalidate committed row caches. Applications remain responsible for explicit invalidation after untracked writes. The tests deliberately retain an old committed cache row after a successful raw command and managed commit; this is the accepted tracking boundary, not cache freshness evidence.

## Verification

The final `AsyncRawCommands_*` focused run passed **45/45** on Release / .NET 10 with maximum parallelism 8 (`artifacts/w1-raw-commands-focused.json`). The initial 35-case run also passed; subsequent runs expanded coverage. The initial build corrected three test-only references before execution.

Cases cover owned/borrowed scalar and non-query paths, actual command identity, explicit capability rejection with no framework synchronous fallback, validation before cancellation, pending tracked work preservation, captured conversion/provider selection, single private initialization, each interrupted initialization phase, actual command validation and cleanup, conservative post-dispatch recovery, cleanup-only failure, primary/secondary identity and ordering, overlapping queries/mutations/completion, private/wrong/retired owners, unchanged tracking/cache semantics, standalone null behavior and helper draining.

Broad Release / .NET 10 local results:

- **2,407/2,407 unit tests passed**, maximum parallelism 16, `artifacts/w1-raw-commands-unit.json`.
- **210/210 Memory tests passed**, maximum parallelism 16, `artifacts/w1-raw-commands-memory.json`.
- **527/527 compliance tests passed for each SQLite anchor**, maximum parallelism 8, `artifacts/w1-raw-commands-sqlite-file.json` and `w1-raw-commands-sqlite-memory.json`.
- Core .NET 8/9/10 and unit/dependency, Memory-test and compliance Release builds passed with zero warnings/errors. Logs share the `w1-raw-commands` prefix.

The **3,671** broad cases are complete successful bounded local runs. The stricter `ValidForEvidence` flag remains false because canonical full-matrix evidence also requires complete provider scope and clean runner/checkout identity. Planning-link and whitespace checks precede integration; the PR records exact-head CI and merge evidence. No normal documentation, navigation or website presentation changed. These results do not satisfy public/native/performance release gates.

## Remaining work

This slice does not close AAPI-59. The [raw reader follow-up](W1%20Async%20Raw%20Reader%20Lifetimes.md) now covers caller-owned returned readers, ephemeral `ReadReader` sequences, reader-lifetime ownership and unconditional conservative post-dispatch handling in raw-model streams. The [synchronous owned-dispatch follow-up](W1%20Synchronous%20Owned%20Command%20Dispatch.md) carries private admission through managed synchronous pipelines while preserving default public override dispatch. Synchronous raw adapter entry points still require shared-gate integration and compatibility evidence; escaped native handles remain outside observable execution.

Native providers have not adopted these internal eager bindings, and existing managed query factories still require native integration with explicit private dispatch. Public signatures and packed consumers remain W3. Tracked async mutation/hydration, full telemetry/correlation, relation coordination/publication, metadata/provisioning, owning-root disposal and the final I/O-map/performance audit remain W1 work. The .NET 10 performance target, 0.9.2 compatibility baseline and SQLite W0-F1 limitation are unchanged.
