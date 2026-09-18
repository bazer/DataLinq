> [!WARNING]
> Internal W1 implementation evidence, not a shipped public async API or native-provider acceptance.

# W1 Canonical Async Row Loading

**Date:** 2026-09-18. This slice extends the [captured fluent reads](W1%20Captured%20Fluent%20Reads.md) and [owned async commands](W1%20Owned%20Async%20Commands.md) into canonical row loaders and one actual cache/materialization path. It does not close the [W1 completion audit](W1%20Completion%20Audit.md).

## Implementation

`DataSourceAccessSourceRowLoader` now has internal asynchronous single-primary-key, finite primary-key batch and index-row loads. They bind SQL and the selected async factory before suspension, require the explicit async capability, and use `ProviderRowDecoder` and the existing canonical result builders. Batch binding and returned-key validation use the same owned snapshot, including when the original request borrowed synchronous caller storage. The result retains the original request identity. Index matching remains the provider's responsibility; returned primary keys must still be unique. A singular read rejects a second row and validates the returned canonical key.

`AsyncBufferedRead` owns the terminal operation. It validates lifecycle/capability before pre-cancellation, acquires one private transaction owner, initializes/executes under that owner, and buffers through genuinely asynchronous reader calls. Both reader and generated-command cleanup settle before result construction and model materialization. Cleanup has no operation cancellation token and no synchronous fallback. The owner remains held through model construction, cache publication and failure reporting. Explicit nested ownership is allowed only for the original transaction; user callbacks cannot inherit it.

Failure handling preserves the original exception, ordered secondary cleanup failures, and the cleanup-failure fact even when the provider rethrows the same exception instance. Recovery uses settled provider evidence, including initialization failure, before transaction admission is released. Callback helpers observe unfinished terminal operations and cannot commit or return their callback result while that work is outstanding.

`TableCache.GetCanonicalRowAsyncCore` integrates the loader with the existing materialization services and row cache. Cache hits still validate async capability, cancellation and transaction ownership. Misses capture the generation before I/O; publication uses that generation after async cleanup and model construction. Invalidation can let the original caller receive its old row, but prevents repopulation or replacement of newer cache data. There is no retry. Transaction rows stay in the transaction cache.

This cache entry point accepts only the existing neutral canonical-key subset. It explicitly rejects provider-sensitive key shapes rather than applying CLR equality to collation-sensitive keys or invoking synchronous SQL. It does not normalize arbitrary model keys or add a public declaration. Existing synchronous loaders remain direct.

## Verification

The new `AsyncCanonical_*` unit cases use controllable providers with synchronous reader methods that throw. They cover borrowed-input/factory capture; empty, valid, duplicate, unrequested and malformed rows; batch and index result completeness; cancellation at dispatch, row advancement and both cleanup stages; initialization success/failure; validation precedence; private nested ownership; cache hits; constructor re-entry/failure/invalidation; stale and newer publication across suspended cleanup; transaction-cache isolation; ordered/identical cleanup failures; and helper admission/draining.

Final local Release / .NET 10 results:

- `artifacts/w1-canonical-loads-focused.json`: **33/33 passed**.
- `artifacts/w1-canonical-loads-unit.json`: **2,235/2,235 passed**, full unit suite at maximum parallelism 16.
- `artifacts/w1-canonical-loads-memory.json`: **149/149 passed**, full Memory suite at maximum parallelism 16.
- `artifacts/w1-canonical-loads-sqlite-file.json` and `w1-canonical-loads-sqlite-memory.json`: **527/527 passed each**, full compliance anchor runs at maximum parallelism 8.
- Unit/dependency, compliance, Memory and core .NET 8/9/10 builds passed with zero warnings/errors. Logs use the `w1-canonical-loads-` prefix. Relative planning links and the diff whitespace check pass. Only internal planning pages changed; no normal docs/navigation or generated website behavior changed.

The PR records exact-head CI. The initial focused run passed 22/23 cases: its publication fixture had committed caching disabled. The final fixture declares `[UseCache]` on its own isolated generated model; no production cache policy was weakened. Build-time test setup corrections included fixture/provider types, nullable assertion types and the non-generic transaction inspection call. The initial failed focused artifact is retained as `w1-canonical-loads-focused-initial.json`.

## Remaining Work

The next read slice must compose buffered canonical batches with model/cache query execution, preserve ordering and duplicates, capture every invocation binding, and cover provider-sensitive/model-key paths. Async index loading here does **not** yet publish a complete index cache. Relation owner/waiter coordination, independently canceled waits, shared index/reference/collection generation checks and complete relation publication remain open. Native adapters remain W2 and public declarations/generated async navigation remain W3.

The full W1 checklist also retains expression/prepared and raw model execution, non-query/mutation/hydration, metadata/provisioning, owning-root disposal, complete diagnostics/telemetry, source auditing and the .NET 10 comparison against the frozen 0.9.2 baseline. The SQLite W0-F1 limitation is unchanged. No package publication, baseline rewrite or public compatibility freeze is part of this slice.
