> [!WARNING]
> Internal W1 orchestration and controllable-provider evidence, not public or native async navigation support. W1 remains open; native adapters are W2 and public/generated/packed consumers are W3.

# W1 Async Relation Loading

**Date:** 2026-09-18. Implements the per-holder coordination and core publication paths in AAPI-39–41. Continues [query/relation ownership](W1%20Query%20and%20Relation%20Ownership.md) and [canonical async row loading](W1%20Canonical%20Async%20Row%20Loading.md). The [completion audit](W1%20Completion%20Audit.md) remains the full checklist.

## Coordination and capture

Collection and reference holders now share one cold-load slot between synchronous and internal asynchronous callers. Synchronous callers wait and execute synchronously. Async callers await the slot with their own cancellation token. Clear and notification callbacks use only the short state lock, never the load slot; no thread-affine lock spans an await.

A canceled waiter stops only its own wait. Failure or cancellation of the owner releases the slot, without retaining a failed task. The next eligible caller rechecks the current snapshot and, if necessary, makes its own attempt with its captured invocation and token. This is not a retry of the failed operation or a database-wide coalescing policy. Transaction admission happens before waiting, so same-transaction overlap still rejects immediately, even with pre-cancellation or a warm holder.

Relation metadata, provider-key inputs, SQL, reader factory policy and capability validation are captured before suspension. Warm holders do not bypass source/capability validation or pre-cancellation. No native resource is acquired by preparing a waiting invocation. A null reference component preserves the existing no-query absence rule; it requires no unused reader capability.

Subscription identity uses that same captured provider key. Final review found that recomputing it from a custom mutable `IProviderKey` after the await could subscribe an empty collection or absent reference to the wrong key and miss a later insert. Two controlled cases mutate the borrowed key while dispatch is paused, then apply a committed insert for the original key and prove the holder is invalidated. Synchronous loads also capture subscription identity before I/O.

## Complete results and publication

Internal collection values/keyed views and optional/required references consume the async result directly. They do not warm a cache and then call a synchronous getter. Required missing references use the existing model/property error. Non-primary-key references preserve cardinality failure, while primary-key lookup keeps the existing neutral versus provider-sensitive distinction. Dictionary construction and reference validation remain inside the transaction read boundary; failures are observed before helper ownership is released.

The relation path reuses canonical single/index decoding where eligible and provider-matched SQL otherwise. String/composite predicates retain provider comparison semantics rather than imposing CLR equality on returned foreign keys. Complete cached index membership can load missing model rows through the existing captured async batch loader. Native reader and command cleanup settle before model-result construction and relation publication.

Holder clear generations, table read generations, per-load subscriptions and subscription-before-publication protect pending loads. An invalidated successful load may return its obtained result, but cannot install a stale holder/index/row entry or start an automatic reload. A newer independent holder's publication is not overwritten. Clearing a holder alone may retain complete valid row/index entries under their own cache policy.

Failed/canceled reading or cleanup cannot publish partial membership or cache false absence. Existing valid individual rows are retained. Empty collections and absent optional references become reusable holder results only after successful evaluation. Transaction membership is never inserted into the shared committed index cache, and terminal transaction fallback rechecks the source before reusing a snapshot.

## Verification

The initial focused run passed **30/30 cases**, followed by **49/49** expanded cases. Final capture-boundary verification passed **51/51 `AsyncRelation_*` cases**, Release / .NET 10, maximum parallelism 8 (`artifacts/w1-relation-loading-capture-focused.json`). Coverage includes:

- independent waiter cancellation, failed/canceled owner release, captured waiter factory/token, and mixed synchronous/asynchronous ownership;
- transaction overlap before waiting/cancellation, independent holders/databases, transaction-local membership, commit/rollback source fallback, and warm capability validation;
- invalidation during suspended cleanup, independently published newer state, no second load on return, absence completeness, partial-read failure/cancellation and retained valid rows;
- index-hit row refills, malformed/duplicate neutral results, string/composite provider-matched references, cardinality, required-missing errors, null composite references, invalid primary keys and keyed-view consistency/duplicate rejection;
- escaped helper work drained through cleanup, no successful helper commit, and cleanup failure removing Continue recovery.

The existing synchronous compliance publication races now start a waiting same-holder caller before releasing the first loader. This reconciles the older competing-load tests with the accepted AAPI-39 baseline update; native synchronous invalidation coverage remains intact.

Development failures are retained in artifacts. One assertion incorrectly expected an `IN` predicate for the optimized single-key refill; it now checks the actual primary-key predicate. A malformed-index fixture was corrected to test invalid provider data rather than invent foreign-key equality validation absent from the source-index contract. Background cache eviction was stopped in the controlled fixture so it cannot interfere with deliberate cache-identity/invalidation assertions. These changes do not disable production maintenance.

Broad Release / .NET 10 verification initially passed **3,988 cases**. After the captured-subscription correction, the repeated runs passed **2,726/2,726 unit**, **210/210 Memory**, and **527/527 compliance cases on each SQLite anchor**: **3,990 cases**, no failures or skips (`artifacts/w1-relation-loading-capture-unit.json`, `w1-relation-loading-capture-memory.json`, `w1-relation-loading-capture-sqlite-file.json`, `w1-relation-loading-capture-sqlite-memory.json`). Unit/Memory used maximum parallelism 16; compliance used 8. Invocation and artifacts are complete. Local `ValidForEvidence` remains false because these bounded development checks are not canonical full-provider/clean-runner release acceptance.

Core .NET 8/9/10 and unit/dependency/compliance/Memory Release builds passed with zero warnings/errors (`w1-relation-loading-*-build.log`). Exact-head CI and merge evidence are recorded with the integration PR. Normal documentation and site presentation are unchanged; planning links and whitespace are checked separately.

## Remaining gates

This slice exercises key-bearing SQL relation targets with controllable integer/string/composite fixtures. The final W1 relation I/O-map reconciliation still needs the less common converted/provider-layout and target-shape boundaries; these fixtures alone do not establish their parity. Full diagnostic correlation and comparable .NET 10 allocation/coordination measurements remain W1 work, including the added per-holder semaphore cost. No performance claim follows from the signature or passing functional tests.

Native driver interruption, provider layouts and adapter adoption remain W2. Public `ValueTask` declarations, generated navigation, custom/mock relation defaults and packed-consumer parity remain W3/T10. This internal `Task` orchestration does not freeze those public signatures. Memory navigation support is not added. Metadata/provisioning, owning-root disposal, native synchronous adapter compatibility and the final cross-family audit remain on the W1 checklist. SQLite W0-F1 remains under its recorded limited exception.
