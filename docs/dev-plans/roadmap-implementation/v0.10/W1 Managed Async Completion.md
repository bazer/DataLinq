> [!WARNING]
> Internal 0.10 orchestration, exercised with controllable providers. Production providers do not implement the new completion capability, and no public async completion/callback API is exposed. W1 and W0-F1 remain open.

# W1 Managed Async Completion

**Recorded:** 2026-09-17. Follows merged [eager-read PR #157](https://github.com/bazer/DataLinq/pull/157), under the [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception). The [completion audit](W1%20Completion%20Audit.md) retains the full remaining scope.

## Actual Managed Finalization

`Transaction` now has internal commit, rollback, disposal and callback entry points backed by the explicit `IAsyncTransactionCompletion` capability. The provider contract separates database confirmation from status observers, telemetry, local finalization and disposal. Unsupported providers fail explicitly, with no synchronous fallback. Capability/lifecycle validation precedes request cancellation; pre-cancellation does not dispatch completion or invalidate pending work.

The managed adapter validates the explicit execution step supplied by the callback/recovery runner. It reuses the existing cache publication/removal, mutable promotion/invalidation and conservative recovery machinery. The synchronous commit path shares the extracted local finalization method and remains direct.

| Boundary | Implemented internal behavior |
| --- | --- |
| Unused commit/rollback | Complete the wrapper without opening or dispatching provider work |
| Commit confirmed | Record committed status before fallible observers; finish local state without late cancellation checkpoints |
| Committed cache finalization fails | Preserve `TransactionCommitFinalizationException`, invalidate unsafe mutable baselines and conservatively recover caches; never report rollback |
| Provider/managed status observer fails | Preserve confirmed outcome and finalized state; attempt independent notification/telemetry boundaries and collect ordered errors |
| Commit throws before confirmation | Preserve the original error, invalidate transaction-derived state, clear affected caches, reject business work/recommit and use only evidence-permitted recovery |
| Later rollback after uncertain commit | Keep the earlier unknown completion; later rollback cannot prove commit never occurred |
| Rollback fails | Invalidate local state, reject business work and another rollback, permit disposal |
| Failed initialization | Reject sync/async business work and completion; dispose without retrying initialization or explicit rollback |
| Disposal | Use independent automatic recovery when allowed; attempt transaction and connection cleanup independently, retain ownership through both, report each attempt only once |

The callback runner now reaches real managed finalization rather than only a standalone controlled lifecycle. A swallowed mutation failure still prevents commit. Callback results wait for finalization and both cleanup steps; a known-committed cleanup failure is an error, not an ordinary result. The recovery collector imports structured secondary errors and preserves a confirmed rollback when a later managed finalization/observer fails. Confirmation imported from an exception must belong to this transaction; a reused exception from another transaction cannot prove rollback.

Both sync and async disposal reject a second call while cleanup is still active, even after the wrapper is marked disposed. Sequential disposal after ownership has been released remains harmless. Callback completion admission remains private; no ambient privilege is introduced.

## Development Evidence

Twenty-one new TUnit cases verify suspended commit ownership, finalized cache/mutable state observed by notifications, pre-cancellation and validation precedence, unused completion, late cancellation, commit/rollback observer failures, committed cache failure, unknown commit followed by rollback, failed rollback restrictions, independent cleanup, helper result timing, callback/rollback error precedence, swallowed mutation failures, unsupported capabilities, failed initialization restrictions, recovery-inspection failure and foreign-transaction confirmation rejection.

Local Release / .NET 10:

- `artifacts/w1-managed-completion-transactions.json`: **256/256 passed**, transaction-focused tests.
- `artifacts/w1-managed-completion-unit.json`: **2,085/2,085 passed**, full unit suite.
- `artifacts/w1-managed-completion-sqlite-file.json` and `artifacts/w1-managed-completion-sqlite-memory.json`: **519/519 passed each**, compliance anchor shards with maximum parallelism 8.
- Unit/dependency, compliance and core .NET 8/9/10 builds: zero warnings/errors, in the adjacent `w1-managed-completion-*-build.log` files.

These are modified-checkout development checks, not frozen release evidence. Exact-head CI is recorded in the PR. Planning pages are excluded from DocFX; relative links and whitespace are verified separately.

## CI Follow-Through: Blocking Test Workers

[PR #158](https://github.com/bazer/DataLinq/pull/158)'s first [CI run](https://github.com/bazer/DataLinq/actions/runs/35270867522) failed 13 existing eager-read/relation cases while waiting for their provider-entry checkpoints. All 21 new completion cases passed. The original [unit artifact](https://github.com/bazer/DataLinq/actions/runs/35270867522/artifacts/10518725425) is retained as `artifacts/w1-managed-completion-ci-unit-failure.zip`, SHA-256 `630d164b21e2a9cb3988c7547abd89ba2b3dbfe2c76ed6b2057502a5e2289274`.

The eager-read tests deliberately block synchronous provider calls. Their shared-pool workers competed with the continuations that release those calls; fixture disposal also synchronously waits for the cache-maintenance worker. A constrained local control used two reported CPUs, 16 parallel tests and four pool workers (`DOTNET_ThreadPool_ForceMinWorkerThreads=4`, `DOTNET_ThreadPool_ForceMaxWorkerThreads=4`, per the [.NET threading configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/threading)). The ordinary two-CPU run passed, but this constrained control failed **12/14** in **75.9 seconds**, reproducing checkpoint delays and reads reaching their safety timeout before test release.

The fix uses dedicated test threads, stops unrelated cache maintenance on those threads before the read, and releases/joins pending work in `finally`, including assertion failure. Production code and ownership assertions are unchanged. Dedicated read threads alone still left two constrained cleanup timeouts; the fixture shutdown correction was also necessary. Final evidence:

- `artifacts/w1-eager-pool-starvation-red.json`: **2 passed / 12 failed**, original harness under the four-worker control.
- `artifacts/w1-eager-pool-starvation-green.json`: **12 passed / 2 failed**, intermediate dedicated-worker-only correction; not passing evidence.
- `artifacts/w1-eager-pool-starvation-final-green.json`: **14/14 passed in 1.2 seconds**, complete correction under the same control.
- `artifacts/w1-managed-completion-unit-final.json`: **2,085/2,085 passed**, full Release / .NET 10 suite at CI parallelism 16.

The failed CI run remains a failure. A subsequent green run must identify its own PR head; neither this harness correction nor the new managed completion closes W0-F1.

## Remaining Integration

The managed wrapper consumes initialization state, but the controllable lazy initializer still needs complete command/completion handoff and diagnostic publication. The scripted failed-initialization case proves restrictions, not actual native initialization. Native open/configure/begin, attached-provider feasibility, driver-specific outcome/cancellation classification, low-level public provider completion and public declarations remain W2/W3 work.

Full operation correlation and telemetry lifetime/failure auditing remain open; independent completion notification handling is not evidence for every existing telemetry callback. Async mutation/hydration, immutable invocation capture and owned commands, relation/cache coordination, metadata/root orchestration and performance evidence remain on the W1 checklist. The official SQLite dependency, 0.9.2 compatibility baseline and .NET 10 benchmark target are unchanged.
