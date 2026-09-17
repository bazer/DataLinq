> [!WARNING]
> Internal 0.10 implementation work, not public async support or native-provider acceptance. W0-F1 and the SQLite/release limitations remain open.

# W1 Transaction Ownership And Initialization

**Recorded:** 2026-09-17. W1.2 foundation, following merged [W1.1 PR #147](https://github.com/bazer/DataLinq/pull/147), under the [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception).

The accepted policies are AAPI-21/22 (validation, cancellation and first use) and AAPI-34–37 (transaction admission and resource ownership). This slice supplies internal ownership and initialization building blocks; it does not finish every W1.2 integration or the W1 exit gate.

## Operation Ownership

`TransactionOperationGate` gives one operation an explicit lease. A conflicting admission throws immediately with the transaction ID and attempted/active operation names. It does not queue, cancel or poison the existing owner. Each transaction has its own gate; there is no database-wide execution lock.

Private dispatch takes a lease explicitly. A short internal step protects active work against overlapping private calls, lease release and reader handoff. No monitor is held during provider calls, callbacks or awaits. There is no ambient flag, `AsyncLocal` privilege or thread affinity in the new gate. Possessing an execution context alone cannot bypass ordinary admission.

An idle lease can transfer to a resource owner. Transfer invalidates the previous owner's dispatch authority and makes its later disposal harmless. The new owner retains the slot between reader moves and through cleanup. A stale lease or step cannot release newer work; a lease from another gate is rejected. These are internal primitives: the test explicitly composes them with the controllable reader, not a production async enumerator.

The existing `Transaction` mutation, commit, rollback and disposal scopes now use this gate in place of the integer busy flag. They still execute synchronously and retain their existing lifecycle/finalization ordering. Existing managed read checks consult the same gate. Busy public disposal remains rejected before marking the transaction disposed or touching provider resources. Existing notification/reentrancy coverage remains active, with diagnostics updated to identify the active operation.

At the foundation checkpoint this was not universal read admission: ordinary synchronous queries did not newly acquire a lease across enumeration, and the old synchronous authoritative-hydration thread-ID allowance remained. The subsequent [owned read integration](W1%20Owned%20Read%20Execution.md) removes that allowance and adds scoped ownership to single-row lookup, source-row loading, scalar execution and raw reader enumeration. Whole LINQ/relation invocation ownership remains open before provider wiring.

## First-Use Resource Publication

`ITransactionResource` describes an internal resource bundle with I/O-free construction and separate direct synchronous/asynchronous initialization and disposal. Production SQLite/MySQL adapters do **not** implement it yet. The bundle owns partial connection/configuration/transaction resources; successful initialization means all required stages have completed.

`LazyTransactionResource<T>` initializes under an existing lease and active step. It publishes an immutable state snapshot, so partially initialized resources are not exposed as ready.

| Boundary | Implemented behavior |
| --- | --- |
| Construction or disposal before use | No resource factory call and no initialization |
| Pre-cancellation on an otherwise usable owner | No factory or provider call; unused state stays reusable |
| Already ready, canceled caller | Cancellation still wins over the cached resource |
| Open/configure/begin suspended | State is initializing; no published resource; owner cannot be released or concurrently disposed |
| Initialization fails or observes cancellation | Failed state, independent cleanup, original exception preserved, no implicit retry or replay |
| Initialization succeeds with cancellation merely requested | Publish ready state; the application-command boundary must still check cancellation before dispatch |
| Initialization cleanup fails | Keep both failures in an internal immutable record and retain the partial resource for explicit final disposal |
| Explicit disposal fails | Report that attempt once, remain disposed, reject reinitialization; repeated sequential disposal does not replay the failure |

The sync path calls only synchronous initializer/cleanup methods. The async path awaits only the explicit async methods, with `ConfigureAwait(false)` and token-free cleanup. Sync-first and async-first execution share the same successfully published resource. These helpers neither issue application commands nor classify interrupted statements, completion certainty or connection trust.

## Evidence And Limits

The new coverage contains six gate cases, eighteen initialization cases and two cases using the existing managed transaction fixture. It pauses open/configuration/begin and cleanup explicitly, resumes an owner on a separate thread, checks stale/foreign owners, reader handoff, rejected busy disposal and preserved original/cleanup exception identity. It also verifies that synchronous completion owns the gate through callbacks and releases it after a callback failure.

- Release / .NET 10 transaction-focused unit run: **101/101 passed**, `artifacts/w1-ownership-focused.json`.
- Full Release / .NET 10 unit run: **1,890/1,890 passed**, `artifacts/w1-ownership-unit.json`.
- Release / .NET 10 SQLite-file and SQLite-memory anchor shards, maximum parallelism 8: **516/516 passed each**, `artifacts/w1-ownership-sqlite-file.json` and `artifacts/w1-ownership-sqlite-memory.json`.
- Release unit project build: zero warnings/errors, `artifacts/w1-ownership-build.log`.
- Core Release build on .NET 8/9/10: zero warnings/errors, `artifacts/w1-ownership-core-build.log`.
- Release compliance project build: zero warnings/errors, `artifacts/w1-ownership-compliance-build.log`.
- Diff whitespace and all local links in the changed planning pages checked; these pages are excluded from DocFX.

These modified-checkout checks are development evidence. The Testing CLI summaries retain actual runner/configuration and raw report paths. No strict release capture or performance acceptance is claimed. Replacing the busy integer adds a per-transaction gate and per-operation lease allocation plus short transition locks; quantify that cost against W0 at the coordination/performance checkpoint before accepting the full gate.

The [owned read follow-through](W1%20Owned%20Read%20Execution.md) records completed private hydration and bounded synchronous source/reader ownership. Remaining integration includes complete outer query/relation invocation ownership, async command/materializer/reader handoff, and helper-owned callback admission/draining. W1.3 must compose recovery, bounded rollback policy and public primary/secondary failure information. W1.4 must connect invocation/source capture and enumeration cleanup. W2 binds native provider initialization, replacing their existing early-open publication only after provider evidence.

No public async declarations, package dependencies, SQLite pooling configuration, retries or package publication changed. The 0.9.2 compatibility baseline and frozen W0 evidence remain unchanged. Passing these tests does not close W0-F1 or prove native cancellation.
