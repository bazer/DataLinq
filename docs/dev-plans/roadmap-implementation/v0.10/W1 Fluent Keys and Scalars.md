> [!WARNING]
> Internal 0.10 key/group/scalar orchestration with controllable providers. This does not add public async APIs or native adapters. W1 and the SQLite W0-F1 limitation remain open.

# W1 Fluent Keys and Scalars

**Recorded:** 2026-09-18. Builds on merged [captured fluent reads, PR #161](https://github.com/bazer/DataLinq/pull/161), under the [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception). Requirements come from [AAPI-17–26/34–38/50/86/87](Async%20Public%20API%20Decisions.md), the [W0 I/O map](W0%20IO%20Execution%20Map.md) S4 and the [W1 audit](W1%20Completion%20Audit.md).

## Key and Grouped-Key Reads

Internal `Select<T>.ReadKeysAsyncCore` and `ReadPrimaryAndForeignKeysAsyncCore` capture statement, bindings, source and required column ordinals at enumerator construction. They do not mutate the builder or perform I/O during capture. Later changes to the selection cannot change which reader fields supply key components. A missing required selected key column, or an index containing columns from another table, fails before dispatch rather than silently interpreting a different field as a key.

Keys are decoded directly into canonical provider values with the existing `ProviderRowDecoder`, then normalized and detached by `DataLinqKey`. They do not round-trip through model converters. Binary identity is independent of reader buffers. Grouped reads build a union of primary/foreign-key columns and decode shared fields once in reader order; an owned binary field is not transferred twice to construct two keys. Composite and nullable foreign-key identity, row order within each group and duplicates are retained.

`AsyncReaderEnumerable<T>` now also accepts an invocation-local buffer. Grouped execution reads all pairs, constructs complete groups and awaits reader/command cleanup before exposing its first group. No partially accumulated group is returned on cancellation, decoding or cleanup failure. The transaction's execution slot remains owned through buffered enumeration, matching the existing synchronous grouped path, even though native resources have already been closed. Combined method/enumerator cancellation, overlapping-call rejection and helper draining apply to both streaming and buffered modes.

## Managed Scalar Reads

Internal raw and typed `Select<T>.ExecuteScalarAsyncCore` capture during the call. `IAsyncSqlScalarFactory` binds a command source and, for typed execution, its local conversion policy together. Provider scalar null/cast differences are preserved by that policy rather than imposing a new generic coercion. The controllable tests verify policy retention; native factory implementation remains W2.

`AsyncScalarRead` supplies lifecycle/capability validation before pre-cancellation, managed transaction admission, owner-aware initialization/dispatch, conversion after command cleanup and failure publication before releasing admission. Successful scalar execution is not retroactively canceled by a request arriving during settled cleanup. A failed conversion is a materialization failure; it does not obscure the fact that provider work and command cleanup have settled.

`InitializingTransactionScalarSource` uses the same lazy resource and private owner as managed scalar execution. Open/configure/begin must finish before command creation. Interrupted initialization remains private and terminal, with cleanup independent of the request token. Successful initialization followed by pre-command cancellation remains distinct: no command is created and confirmed initialization can be reused. Callback helpers await unfinished scalar work/cleanup and reject the callback result instead of committing.

Recovery uses settled provider evidence, not exception names or connection-open state. Unknown effects, lost trust, failed assessment or cleanup cannot establish continued transaction use. Original exceptions remain primary and secondary errors remain ordered.

The new scalar integration exposed a cross-boundary cleanup issue: execution and disposal can throw the same exception object. Identity deduplication correctly retains that object once, but previously the next layer could lose the fact that cleanup also failed. `ExecutionFailureContext` now preserves an internal `HasCleanupFailure` bit through snapshots, imports and recovery transitions. The primary stage and exception identity remain unchanged; cleanup failure still removes Continue/Rollback recovery. This is internal bookkeeping, not an addition to the accepted public diagnostic surface.

## Development Evidence

Thirty-four new TUnit cases cover canonical/typed/binary key identity, selected ordinals, composite/null grouping, overlapping ownership transfers, complete buffering, cancellation and independent cleanup, unused/empty groups, callback draining, scalar source/conversion capture, initialization interruption, recovery trust and identical execution/cleanup exceptions.

Local Release / .NET 10:

- `artifacts/w1-fluent-keys-scalars-focused-final.json`: **76/76 passed**, async transaction-method filter, including existing cases.
- `artifacts/w1-fluent-keys-scalars-transactions.json`: **335/335 passed** at maximum parallelism 16.
- `artifacts/w1-fluent-keys-scalars-unit.json`: **2,199/2,199 passed**, full unit suite at CI parallelism 16.
- `artifacts/w1-fluent-keys-scalars-sqlite-file.json` and `w1-fluent-keys-scalars-sqlite-memory.json`: **519/519 passed each**, compliance anchor shards at maximum parallelism 8.
- `artifacts/w1-fluent-keys-scalars-build-final.log`, `w1-fluent-keys-scalars-core-build.log` and `w1-fluent-keys-scalars-compliance-build.log`: unit/dependency, core .NET 8/9/10 and compliance builds passed with zero warnings/errors.

The earlier focused run also passed (71/71) before the shared-field, empty-group and assessment-failure cases were added. Exact-head CI belongs in the PR. These are development/regression checks, not native async or frozen release evidence. Planning pages are excluded from DocFX; relative links and whitespace are checked separately.

## Remaining Work

S4 remains open for fluent model/cache execution and raw model/string entry points. Expression/prepared backend integration, canonical cache loading and generation-safe publication remain separate work. Non-query/mutation/metadata orchestration, complete operation correlation/telemetry, owning-root disposal, the .NET 10 performance comparison and the full W1 exit audit are not closed by these tests. The accepted synchronous relation naming and required-reference prerequisites must also be verified explicitly; they are not implied by these internal async paths.

Native provider factories, connection lifetimes and conversion fidelity remain W2; public declarations/consumer evidence remain W3. The compatibility baseline stays 0.9.2 and benchmarks stay on .NET 10.
