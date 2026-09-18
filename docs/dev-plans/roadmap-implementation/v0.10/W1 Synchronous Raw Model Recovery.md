> [!WARNING]
> Bounded W1 integration evidence. Built-in public synchronous raw adapter admission and override compatibility remain open. This does not close W1, native async feasibility, public API acceptance or SQLite W0-F1.

# W1 Synchronous Raw Model Recovery

**Date:** 2026-09-18. This extends the [synchronous raw coordinator](W1%20Synchronous%20Raw%20Execution%20Coordinator.md) and [private owned dispatch](W1%20Synchronous%20Owned%20Command%20Dispatch.md). The [completion audit](W1%20Completion%20Audit.md) remains the full W1 checklist.

## Managed model lifetime

`Transaction.GetFromQuery<T>` and `GetFromCommand<T>` now use one raw-model coordinator. Enumeration remains cold: metadata validation and per-enumeration reader binding start on the first move. The existing guarded enumerator retains transaction ownership through reader acquisition, every row, model construction, early disposal, exhaustion and failure reporting. Models retain their transaction source; raw results do not become tracked mutations or cache publications. Column lookup remains by database name.

The default reader binding preserves private owned dispatch and its legacy public virtual override hooks. It treats entry into a legacy override as opaque dispatch. Internal adapters can instead supply a captured `SyncRawCommand`, which distinguishes I/O-free validation/command creation from initialization and actual dispatch. Invocations reserve cleanup ownership before opening a reader; a rejected reuse cannot dispose another caller's command or reader.

## Failure and compatibility rules

Acquisition, row advancement, value decoding and model-constructor failures retain their original exception identity. Cleanup always attempts the reader before the owned command. The coordinator then publishes the immutable failure snapshot to the transaction, exception and helper lifetime before releasing the execution slot. Existing transaction-bound models and subsequent operations observe the same recovery restriction.

Raw SQL can have effects even when it returns rows. Post-dispatch failure therefore cannot gain permission to continue from an ordinary-read or no-statement claim. Explicit provider evidence may permit rollback; unknown integrity permits disposal only. Pre-dispatch validation/construction failure preserves valid pending tracked work. Failed initialization, cleanup failure or failed evidence assessment prevents reuse. The tracked-mutation poisoning flag is distinct from this execution recovery restriction.

Known library-owned reader/command wrappers expose structured internal cleanup so a disposal exception cannot replace a preceding model failure. Reader and command failures remain ordered secondary evidence, including a cleanup-failure flag when the same exception object is reused. Public legacy wrapper disposal retains its aggregate-exception behavior. Likewise, legacy acquisition plus command-disposal failure keeps its public `AggregateException` and inner exceptions, now carrying cleanup evidence into the managed boundary. Arbitrary provider aggregates are not flattened.

Helper integration uses the existing reader registration and drain protocol. A callback that catches a raw-model failure cannot silently commit; an escaped iterator is closed and cleaned before recovery, with no successful callback result. Cleanup evidence constrains helper rollback rather than being lost at the model boundary.

## Verification

Release / .NET 10 passed **30/30 `SyncRawModels_*` cases**, maximum parallelism 8 (`artifacts/w1-sync-raw-models-focused.json`). Cases cover both legacy and explicit binding, owned/borrowed commands, cold repeated enumeration, acquisition/advance/materialization/constructor/early-disposal/exhaustion failures, pre-dispatch validation and pending tracked work, ordered/repeated cleanup exceptions, classifier failure, legacy acquisition aggregates, helper restrictions, failed initialization and rejected invocation reuse.

Broad local results:

- **2,574/2,574 unit cases**, maximum parallelism 16 (`w1-sync-raw-models-unit.json`). This also runs the existing overlap and in-flight helper-drain regressions.
- **210/210 Memory cases**, maximum parallelism 16 (`w1-sync-raw-models-memory.json`).
- **527/527 compliance cases on each SQLite anchor**, maximum parallelism 8 (`w1-sync-raw-models-sqlite-file.json`, `w1-sync-raw-models-sqlite-memory.json`).
- Core .NET 8/9/10, unit/dependencies, compliance and Memory-test Release builds: zero warnings/errors. Build logs use the same artifact prefix.

All **3,838** broad cases pass without skips, with invocation and artifacts complete. Local `ValidForEvidence` remains false because canonical release evidence additionally requires full provider scope and clean runner/checkout identity. The PR records exact-head CI and merge evidence. All 57 relative links in the touched planning pages and whitespace are checked; website navigation/presentation and normal documentation are unchanged.

Initial artifacts are retained: the first build found five test-fixture typing/naming errors; the first focused run passed 25/30. Four failures were incorrect expectations that `GetReadSource()` could bypass active enumeration ownership. The fifth expected the helper to rethrow a deliberately caught, already-settled failure: existing helper semantics instead reject commit using the transaction restriction, retaining original cleanup evidence on the caught exception. Assertions now verify those actual contracts. No product-source change was needed to resolve the initial test failures.

## Remaining work

Built-in SQLite/MySQL public raw entry points still require a compatible admission/binding design. This change preserves their current override path when reached through managed raw-model enumeration; it does not retrofit public adapter admission. Complete telemetry/correlation, async tracked mutation/batch orchestration, async relation coordination/publication, metadata/provisioning, owning-root disposal and final I/O-map/performance evidence remain W1 work.

The .NET 10 benchmark target, published 0.9.2 baseline and accepted limited W1 exception for SQLite are unchanged. Native async provider adoption and public/packed API evidence remain W2/W3 requirements.
