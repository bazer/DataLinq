> [!WARNING]
> Internal W1 orchestration evidence. This is a prerequisite for synchronous raw adapter gating, not a claim that native raw entry points now enforce it.

# W1 Synchronous Owned Command Dispatch

**Date:** 2026-09-18. This extends [owned reads](W1%20Owned%20Read%20Execution.md), [query ownership](W1%20Query%20and%20Relation%20Ownership.md) and [raw execution](W1%20Async%20Raw%20Reader%20Lifetimes.md). The [full W1 audit](W1%20Completion%20Audit.md) remains open.

## Private dispatch and compatibility

Managed synchronous reads already hold a transaction step, but previously discarded that proof when calling the public adapter command methods. A raw-entry gate added at those methods would reject the managed operation that already owns admission.

`SyncCommandDispatch` now carries the explicit step through reader, typed/untyped scalar and mutation non-query dispatch. Nonvirtual internal `DatabaseAccess` entry points validate arguments, the adapter's managed transaction binding, transaction lifecycle and the exact current step before invoking internal provider hooks. A foreign or retired step, an unbound adapter, or a completed transaction cannot dispatch. No permission is stored on a command or inferred from thread/ambient state.

The default hooks call the existing public synchronous virtual methods. This preserves custom overrides, native string-to-command override interception, borrowed-command identity and ownership, and provider-specific scalar/null conversion policies. Gate-aware internal adapters can override the private hooks and dispatch directly, keeping their public raw admission separate. The controllable test adapter exercises both modes; the default implementation does **not** retrofit a raw gate into arbitrary custom overrides.

Database-root operations still take their existing public path. The dispatcher does not acquire or release ownership and does not dispose borrowed commands. Its reader sequence owns only the returned reader. Existing managed scopes retain admission through row materialization and command/reader cleanup. All synchronous execution remains direct; no async blocking, thread-pool facade, public declaration or native capability is introduced.

## Integrated paths

- `Select` reader/first-row and typed/untyped scalar paths, including fluent and expression-query consumers.
- Canonical single-row, primary-key batch and index batch loaders, including transaction cache lookups and private authoritative hydration.
- Scalar cache-row queries and the scalar relation-cache fallback.
- Transaction raw-model string and borrowed-command sequences, retaining their existing model source and cleanup ownership.
- Mutation scalar/non-query statements. The existing outer mutation lease now supplies a statement step, released before local cache application and the separate authoritative hydration step. Existing reservations, poisoning, finalization and rollback semantics remain in place. The internal preflighted test entry point also acquires an explicit step.

The dispatch inventory in `src/DataLinq` has no remaining direct `DatabaseAccess.Execute*`/`ReadReader` calls in these managed pipelines. This bounded inventory is not the final W0 I/O-map audit: provider internals, metadata/provisioning, root maintenance and remaining async integrations still need their own evidence.

## Verification

Release / .NET 10 passed **36/36 `SyncOwnedCommands_*` cases**, maximum parallelism 8 (`artifacts/w1-sync-owned-focused.json`). The initial run passed 32/32 before terminal-state and argument coverage was added (`w1-sync-owned-focused-initial.json`).

Focused cases exercise actual fluent/expression reads, canonical loaders, scalar cache lookup, model lookup, raw-model string/borrowed sequences, generated-ID insertion, update/delete and authoritative hydration. Provider/read/cleanup callbacks attempt public raw and managed reentry while private dispatch succeeds. Cases also cover foreign/retired/unbound ownership, completed transactions, null arguments, public override compatibility, borrowed identity/lifetime, unchanged scalar conversion, command/reader failures, mutation poisoning and rollback. The scalar relation-cache fallback is included in the source routing and broad compliance regression; it is not a separate controllable focused case in this slice.

Broad local results:

- **2,500/2,500 unit tests**, maximum parallelism 16 (`w1-sync-owned-unit.json`).
- **210/210 Memory tests**, maximum parallelism 16 (`w1-sync-owned-memory.json`).
- **527/527 compliance tests on each SQLite anchor**, maximum parallelism 8 (`w1-sync-owned-sqlite-file.json`, `w1-sync-owned-sqlite-memory.json`).
- Core .NET 8/9/10, unit/dependencies, compliance and Memory-test Release builds: zero warnings/errors. Logs use the `artifacts/w1-sync-owned-` prefix.

All **3,764** broad cases are complete, passing, with no skips and complete artifacts. Local `ValidForEvidence` remains false because canonical release evidence additionally requires complete provider scope and clean runner/checkout identity. The PR records exact-head CI and merge evidence. Planning links and whitespace are verified; normal docs and website navigation/presentation are unchanged.

## Remaining work

The [synchronous raw coordinator](W1%20Synchronous%20Raw%20Execution%20Coordinator.md) now supplies internal eager/reader admission, cleanup, recovery and helper integration with public controllable-adapter evidence. Built-in public raw adapter entry points still need binding and compatibility evidence. Native string overloads call public virtual command overrides, and MySQL lazy initialization executes an additional command; adoption must preserve the former and prevent recursive admission in the latter. These internal slices do not change those adapters' current public behavior. Synchronous raw-model failure reporting also remains open.

Complete telemetry/correlation, async tracked mutations and batches, relation coordination/publication, metadata/provisioning, owning-root disposal and final I/O-map/performance evidence remain W1 work. Native async adoption and public/packed signatures remain W2/W3. The .NET 10 performance target, 0.9.2 baseline and SQLite W0-F1 limitation are unchanged.
