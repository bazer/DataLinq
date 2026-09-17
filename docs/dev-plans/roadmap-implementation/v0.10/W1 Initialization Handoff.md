> [!WARNING]
> Internal 0.10 orchestration with controllable providers. Native initialization and provider binding remain W2; public declarations remain W3. W1 and W0-F1 are still open.

# W1 Initialization Handoff

**Recorded:** 2026-09-17. Follows merged [managed-completion PR #158](https://github.com/bazer/DataLinq/pull/158), under the [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception). The [completion audit](W1%20Completion%20Audit.md) retains the full remaining scope.

## Ownership and Publication

`LazyTransactionResource<T>` now accepts an existing private `TransactionOperationGate.Step` as well as its original lease entry points. Reader acquisition can initialize without trying to acquire a second step inside its own execution slot. Both synchronous and asynchronous paths validate the step's gate and lifetime. A separate resource-call guard rejects overlapping initialization/disposal through the same private step, including while failed-initialization cleanup or terminal disposal is suspended. No lock spans provider work, no ambient ownership is added, and synchronous initialization remains direct.

`InitializingTransactionReaderSource<T>` composes a captured reader source with the lazy bundle used by its provider access. `AsyncReaderEnumerator<T>` supplies its private owner through `IAsyncTransactionReaderSource`. A transaction-owned source rejects missing ownership before cancellation; an ordinary borrowed/root source keeps its existing dispatch. Inner source validation remains I/O-free and cannot depend on unpublished native resources.

The raw internal completion capability also receives the private step. The controllable managed-completion fixture now reads actual lazy state and disposes the retained bundle under helper/recovery ownership. This proves the initialization-to-reader-to-disposal handoff, rather than only setting a synthetic failed-initialization flag.

| Boundary | Executable behavior |
| --- | --- |
| Invalid capability or pre-canceled read | No initialization or command; no managed transaction restriction |
| Cancellation after admission but before initialization starts | Wrapper stays unused and reusable; no command or initialization replay |
| Suspended open, configuration or begin | Resources stay private; the reader holds shared sync/async admission |
| Initialization succeeds, then cancellation is observed before the command | No application command; published state stays ready; later execution is allowed after safe failure assessment |
| Initialization fails or observes cancellation | Terminal failed state; no read, commit, explicit rollback, reconnect or initialization retry; disposal remains available |
| Initialization cleanup fails | Keep the original exception and ordered cleanup details; retain partial resources for a later disposal attempt |
| Callback returns with initialization still running | Close admission, await admitted work and cleanup, report unfinished work, never commit or return the callback result |
| Helper disposes retained resources | Pass the existing private owner, await the attempt, then independently attempt connection cleanup; do not explicitly roll back failed initialization |

Successful initialization is evidence for the no-statement/confirmed-integrity distinction at the pre-command checkpoint. Rollback availability still comes from explicit provider evidence. Once command acquisition begins, its evidence remains authoritative; this wrapper does not infer successful command cancellation from token state.

## Failure Boundaries

The lazy bundle attaches immutable initialization context after partial cleanup settles and before releasing ownership. Matched cancellation is reported as cancellation, other recorded causes are preserved, initialization remains the enclosing stage, and cleanup errors remain ordered secondary failures. The reader now imports existing context at acquisition and cleanup boundaries instead of discarding nested errors. It records managed recovery restrictions before releasing admission; helper draining imports that context and adds its own later disposal failures without mutating earlier snapshots.

Repeated initialization is never recovery. Retrying disposal of a resource retained after failed partial cleanup is a distinct resource-recovery action, and final disposal still reports each retained-resource attempt only once.

## Development Evidence

Twenty new TUnit cases cover three suspended initialization phases, failure/cancellation at each phase, validation/pre-cancellation, cancellation before initialization and after successful publication, awaited and unfinished callback failures, private-step overlap, foreign/expired owners, and missing managed ownership. Existing lazy-resource cases also assert synchronous cause/stage and cleanup-context preservation.

Local Release / .NET 10:

- `artifacts/w1-initialization-transactions.json`: **276/276 passed**, transaction-focused tests at maximum parallelism 16.
- `artifacts/w1-initialization-unit.json`: **2,105/2,105 passed**, full unit suite at CI parallelism 16.
- `artifacts/w1-initialization-sqlite-file.json` and `artifacts/w1-initialization-sqlite-memory.json`: **519/519 passed each**, compliance anchor shards at maximum parallelism 8.
- Unit/dependency, compliance and core .NET 8/9/10 builds: zero warnings/errors, recorded in adjacent `w1-initialization-*-build.log` files.

These are modified-checkout development checks, not frozen release evidence. Exact-head CI is recorded in the PR. Planning pages are excluded from DocFX; relative links and whitespace are checked separately.

## Remaining W1 Work

This slice connects the internal deferred reader and managed completion/helper boundaries. It does not connect initialization to every inventoried dispatch family, implement native opening, or expose public async APIs. Owned/generated commands, actual typed materialization, scalar/non-query orchestration, async mutations and batches, immutable query invocation capture, relation/cache publication races, metadata/root lifetimes, complete operation correlation/telemetry, the I/O-map audit and .NET 10 performance comparison remain on the W1 checklist.

The compatibility baseline stays 0.9.2, benchmarks stay on .NET 10, and the SQLite dependency limitation remains unchanged.
