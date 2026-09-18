> [!WARNING]
> Internal W1 orchestration evidence. Native provider adoption, public declarations and packed consumers remain later gates.

# W1 Async Raw Reader Lifetimes

**Date:** 2026-09-18. This extends [raw eager commands](W1%20Async%20Raw%20Commands.md) through AAPI-56–59's returned-reader and ephemeral sequence families, and closes the conservative raw-model failure gap recorded there. The [full W1 audit](W1%20Completion%20Audit.md) remains open.

## Reader ownership

Internal `DatabaseAccess.ExecuteReaderAsyncCore` overloads accept SQL strings or borrowed commands. `AsyncRawDataReader` owns managed admission before acquisition and registers for helper draining while acquisition is still pending. It retains admission between advances and after EOF until disposal. Pausing at a row or exhausting rows does not implicitly dispose a caller-owned reader. Other managed queries, mutations, raw execution and completion remain rejected throughout that lifetime.

The acquired reader retains created-command ownership through `OwnedAsyncDataReader`; borrowed command identity, timeout and caller ownership remain unchanged. Neither wrapper owns the surrounding transaction. Async advancement and disposal use explicit async capabilities; synchronous advancement/disposal remain direct calls, without blocking on async work. Both disposal forms share lifecycle state and do not release resources twice. Local getters reject invalid positions, overlapping calls and helper-closed access while forwarding already available row data. `GetOrdinal` can be used before the first row. Caller-side getter/materialization code does not become a managed model query.

Validation precedes pre-cancellation and admission. Unused/pre-canceled operations do not initialize or create commands. Private initialization is published once on success; failure/cancellation after initialization starts is drained while private and remains terminal. A cancellation observed after successful initialization but before command dispatch preserves ready state and valid prior work. Late cancellation after successful reader acquisition still transfers ownership to the caller; individual reader advances take their own tokens. Failed/canceled advances await uncancelable reader/command cleanup, publish recovery restrictions and only then release admission and report the original failure.

The private borrowed-reader entry point validates an explicit owner from the same transaction and dispatches under that owner. It does not create a second gate owner or transfer responsibility away from the enclosing managed operation. Native factories still need to adopt that boundary in W2.

Helpers close reader admission when the callback ends, await pending acquisition/advancement, drain escaped readers, and reject unfinished-work callback success without committing. Failure publication precedes lease release, so an error during draining is not lost. Later disposal of a helper-drained reader is harmless. Standalone database-root readers have independent lifetimes and do not share a transaction lock.

## Deferred sequences and raw effects

Internal `ReadReaderAsyncCore` uses the existing async enumerator for cold capture, combined method/enumerator tokens, early termination and cleanup. Each enumeration captures the current factory independently. It yields the same `IDataLinqDataReader` current-position object across rows, not snapshots. The enumerator owns advancement and disposal: callers must copy needed values during iteration and must not manually advance or dispose the yielded reader. Materialized model/row APIs remain the independent-result alternative.

`RawAsyncReaderSource` wraps the lower-level sequences and existing raw-model streams. Owned and borrowed command sources record dispatch explicitly; private initialization forwards that observation. Raw failures after dispatch cannot acquire the ordinary-read reuse exception, even if a provider claims `OrdinaryRead` or `NoStatement` effects. The implementation does not parse SQL. A custom source without dispatch observation is conservatively considered dispatched once opening begins; its stronger initialization-failure classification is preserved rather than weakened to unknown effects. Pre-dispatch construction/validation/cancellation remains distinguishable for sources that provide the internal observation.

Reader/command cleanup and evidence-assessment failures retain original exception identity, encounter order and the cleanup-failure flag, including repeated use of one exception instance. Recovery is assessed only after cleanup settles. Raw model constructor/conversion failures now receive the same conservative post-dispatch treatment. Successful raw results still do not publish row-cache entries, create tracked changes or hydrate mutations.

## Verification

The final Release / .NET 10 focused runs passed **57/57 `AsyncRawReaders_*` cases** and **26/26 existing `AsyncRaw_*` model cases**, maximum parallelism 8. Artifacts: `artifacts/w1-raw-readers-focused.json` and `w1-raw-readers-models.json`.

The initial new-reader run passed 46/46 before coverage expansion. The first raw-model regression run passed 21/26: four cancellation cases and one constructor-failure case still expected transaction reuse, contrary to AAPI-59. Those expectations now assert rejection and valid recovery. The cache test inspects empty transaction/committed cache structure without attempting a forbidden new read through the failed transaction. Initial artifacts remain `w1-raw-readers-focused-initial.json` and `w1-raw-readers-models-initial.json`.

Cases cover eager acquisition, ownership through paused rows/EOF/cleanup, pre-cancellation/capability ordering, actual command rejection without framework synchronous fallback, private first use and interrupted initialization, ready-state cancellation, cleanup ordering and repeated exception identity, row cancellation/failure, concurrent move/getter/dispose rejection, sequential sync/async use and disposal, late acquisition cancellation, deferred/repeated enumeration and ephemeral values, combined tokens, raw-model/provider-effect claims, escaped helper readers and unfinished acquisition/advancement, standalone concurrency, private/wrong ownership, pre-dispatch tracked-work preservation and custom-source conservative classification.

Broad Release / .NET 10 local results:

- **2,464/2,464 unit tests passed**, maximum parallelism 16, `artifacts/w1-raw-readers-unit.json`.
- **210/210 Memory tests passed**, maximum parallelism 16, `artifacts/w1-raw-readers-memory.json`.
- **527/527 compliance tests passed for each SQLite anchor**, maximum parallelism 8, `artifacts/w1-raw-readers-sqlite-file.json` and `w1-raw-readers-sqlite-memory.json`.
- Core .NET 8/9/10 and unit/dependency, compliance and Memory-test Release builds passed with zero warnings/errors. Build logs share the `w1-raw-readers` prefix.

The **3,728** broad cases are complete successful bounded local runs. Their stricter `ValidForEvidence` flag remains false: canonical full-matrix release evidence also requires complete provider scope and clean runner/checkout identity. Planning links and whitespace are checked before integration; the PR records exact-head CI and merge evidence. No normal documentation, website presentation or navigation changed, and no public/native/performance acceptance is inferred.

## Remaining work

These are internal execution paths, not public/native support claims. The [synchronous owned-dispatch follow-up](W1%20Synchronous%20Owned%20Command%20Dispatch.md) carries private admission through managed synchronous query, loader and mutation paths while preserving default public override dispatch. Synchronous raw adapter entry points still need shared ownership integration and compatibility evidence. Escaped native handles remain outside observable execution. Native command/reader/connection adoption, caller getter I/O feasibility, provider conversion fidelity and public/packed signatures remain W2/W3 gates.

Tracked async mutation/hydration, complete telemetry/correlation, relation coordination and index/reference/collection publication, metadata/provisioning, owning-root disposal and final I/O-map/performance evidence remain W1 work. No full W1 closeout is inferred from this reader slice. The .NET 10 performance target, 0.9.2 baseline and SQLite W0-F1 limitation are unchanged.
