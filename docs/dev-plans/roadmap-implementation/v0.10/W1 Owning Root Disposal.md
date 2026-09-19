> [!WARNING]
> Internal W1 contracts and controllable-provider evidence. Native provider adoption remains W2, public async declarations remain W3 and DI ownership remains W4. W1 and SQLite W0-F1 are not closed.

# W1 Owning Root Disposal

**Date:** 2026-09-19. Implements the bounded owning-root contract in [AAPI-61](Async%20Public%20API%20Decisions.md#aapi-61-async-disposal-on-existing-owning-roots). The [completion audit](W1%20Completion%20Audit.md) retains the remaining W1 requirements.

## Shared owning lifetime

The internal `OwnedRootDisposal` coordinator captures an owned-resource cleanup list, validates explicit async capability and lifecycle restrictions before closing admission, and makes one cleanup attempt shared by its synchronous and asynchronous entry points. Synchronous resources execute directly; async resources use their explicit async delegate. Purely local cleanup is identified separately. There is no provider-I/O fallback, offloaded synchronous disposal or caller cancellation token.

Cleanup steps run in order. Independent steps still run after failure. Original exception identity and stack are preserved, with ordered, identity-deduplicated secondary failures in the internal immutable failure context. Cleanup records `NotApplicable` completion, no transaction identity and no transaction recovery actions. Completed cleanup, including failed cleanup, is not retried. An overlapping or reentrant root disposal rejects immediately while the original attempt remains responsible for its resources; this internal choice does not freeze a new public concurrency promise.

`Database<T>.DisposeAsyncCore` delegates only to providers opting into the internal capability. Multiple database wrappers and direct provider calls therefore use one provider-owned lifetime in the controllable fixture. An old synchronous provider is rejected before its synchronous disposal executes. Built-in SQLite/MySQL/MariaDB provider resource wiring and public `IAsyncDisposable` declarations are deliberately unchanged.

`State` delegates to its cache's shared lifetime. Nested owning implementations must propagate their child's I/O-free disposal validation before claiming the outer lifetime; the fixture and State/cache validation path prove this for maintenance self-disposal. This is not a new registry of dependent application work. Root cleanup neither completes, cancels nor disposes dependent transactions/readers. Applications must finish those lifetimes first, and this slice does not promise that dependent work remains usable after root disposal.

## Cache maintenance shutdown correction

The old scheduler cleared its worker handle before waiting up to five seconds. A slow maintenance callback could outlive that wait while the root cleared its caches and the scheduler reported no running worker. Already completed worker faults could also escape observation. The previous task-ID self-wait check did not identify async continuations reliably.

The scheduler now retains its worker and cancellation source until shutdown settles. Synchronous Stop joins only this owned local maintenance worker; internal async disposal awaits it. Neither abandons cleanup on a timer. Cache clearing follows worker completion even when shutdown was started by another caller. Concurrent scheduler stop calls share the shutdown outcome, and root disposal upgrades a temporary stop to permanent shutdown. `Start` rejects during stopping or after permanent disposal. Temporary Stop/Start/Restart remain supported. A completed failed worker must be stopped/observed before restart; it is no longer silently replaced.

Synchronous maintenance callbacks, including callbacks reached after worker awaits and cancellation callbacks during stop, cannot wait for their own scheduler or close their owning root. A thread-local marker detects only self-join and is restored before each await; it provides no transaction execution privilege. Cancellation failures still join the worker and attempt subsequent cleanup. Only cancellation carrying the scheduler's own requested token is ignored; unrelated cancellation remains a cleanup failure.

After shutdown, cache disposal attempts telemetry unregistering, row clearing, index clearing and invalidation notification independently for every captured table. An observer failure cannot skip a later table or the provider fixture's independent resource cleanup. The cache's internal constructor accepts a scheduler factory so deterministic clocks do not require changing process-global factories. A null scheduler models the local no-worker path.

These changes affect existing synchronous cache shutdown too: slow maintenance now delays Dispose until it actually exits, worker failures are reported, and a permanently disposed scheduler cannot restart. They do not turn any built-in provider into an async-capable provider. A maintenance callback that never returns can therefore prevent shutdown; there is no cancellation/abandonment guarantee for arbitrary user callbacks.

## Verification

`TransactionMutationFailureTests.RootDisposal.cs` adds **28 cases** covering mixed disposal modes, shared database/provider ownership, unsupported legacy providers, suspended cleanup, immutable captured cleanup lists, repeated/overlapping attempts, ordered nested failures, dependent transaction non-interference, no-worker cleanup, slow/faulted maintenance, temporary stop versus permanent disposal, foreign cancellation, cancellation callback failure and self-disposal before/after a maintenance await.

Focused Release/.NET 10 verification passed **28/28**, maximum parallelism 8 (`artifacts/w1-root-disposal-focused.json`). Broad verification passed **2,785/2,785 unit**, **210/210 Memory**, and **527/527 compliance on each SQLite anchor**: **4,049 cases**, zero failures/skips. Reports are `artifacts/w1-root-disposal-unit.json`, `w1-root-disposal-memory.json`, `w1-root-disposal-sqlite-file.json` and `w1-root-disposal-sqlite-memory.json`. Unit/Memory maximum parallelism was 16; compliance was 8. Invocation and artifacts are complete. Local `ValidForEvidence` is false because these bounded development checks are not canonical full-provider/clean-runner release acceptance.

Core .NET 8/9/10 and unit/dependency/compliance/Memory Release builds passed with zero warnings/errors (`artifacts/w1-root-disposal-*-build.log`). The integration PR records exact-head CI and merge evidence separately. Whitespace and 100 relative planning links passed validation. Planning docs are excluded from the published DocFX site; site navigation and presentation did not change.

## Scope reconciliation and remaining work

The [accepted limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception) explicitly keeps internal contracts separate from production provider wiring. Earlier completion text placed built-in synchronous raw adapter binding alongside remaining W1 work without making that separation clear. The internal compatibility contract/controllable evidence remains a W1 requirement; actual built-in synchronous and asynchronous adapter adoption belongs to W2. AAPI-59 is not removed or declared complete.

In particular, built-in raw string methods currently dispatch through public virtual borrowed-command overloads, and lazy MySQL initialization reenters those methods for setup. Native adoption must preserve external override dispatch and ownership without ambient bypasses, self-rejection or silently changing public adapter identity. Existing controllable/legacy tests do not prove that adoption.

Metadata/probe/provisioning multi-command orchestration, complete diagnostics/correlation, comparable .NET 10 allocation/coordination measurements and the final W0 I/O-map audit remain W1 work. Native root resource cleanup, SQLite driver limitations, public interface/consumer compatibility and container-created versus externally supplied DI ownership retain their W2/W3/W4 gates.
