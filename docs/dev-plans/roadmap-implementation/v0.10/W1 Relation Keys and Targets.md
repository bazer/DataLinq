> [!WARNING]
> Internal W1 orchestration and controllable-provider evidence. Native async adapters remain W2, public/generated/packed async navigation remains W3, and W1 is not complete.

# W1 Relation Keys and Targets

**Date:** 2026-09-18. Follows [async relation loading](W1%20Async%20Relation%20Loading.md) and reconciles its less common key layouts and relation targets with the frozen [W0 I/O map](W0%20IO%20Execution%20Map.md#keys-materialization-and-relations). The [completion audit](W1%20Completion%20Audit.md) remains the full W1 checklist.

## Discovered target and cache defects

The metadata audit found a valid target not covered by the first async relation slice: a table's foreign key can reference a keyless view with a declared unique candidate key. The generated model and runtime metadata accept that relation. The existing synchronous reference loader could return its first result, but the internal async loader rejected it with `A relation row has no canonical primary key.`

A second reference to a different row exposed an existing synchronous defect: the loader put the first view row under the empty primary-key identity, so the second reference could return that first row. The unique relation predicate does not create a primary key for the row cache.

Both paths now materialize keyless relation rows without primary-key cache lookup/publication or index-to-primary-key membership. Async loading buffers canonical rows, settles reader/command cleanup, then uses the existing keyless materialization service under the same read owner. Holder snapshots still retain successful complete results under their existing generation/subscription policy. Cardinality and required-reference validation still run within the read boundary. The synchronous fix stays direct; it does not call the async implementation.

Initial probes putting foreign-key declarations on a view, or removing the primary key from a table, were rejected by the existing metadata rules. Those rejected probes were not evidence that all keyless view navigation is unsupported. The accepted unique-candidate-key fixture is the positive counterexample. No metadata or generator capability is added here, and this does not claim that a provider can create a physical foreign-key constraint referencing a view.

## I/O-path reconciliation

| Existing relation path | Internal execution and bounded evidence |
| --- | --- |
| Primary-key references with integral or resolved GUID keys | Captured canonical single-row loader; converted integral identity reuse, ordered composite integral keys and source-specific cache ownership are exercised |
| Provider-sensitive primary keys and other candidate keys | Provider-matched SQL retains database comparison semantics; earlier string/composite cardinality cases remain, with binary key/result ownership added here |
| Single-column canonical index loading | Converted integral keys and resolved GUID keys reuse the captured index loader; the supplied canonical key is not passed through a model converter again |
| Other relation indexes | Provider-matched SQL keeps scalar physical conversion or ordered composite predicates; complete row/index publication retains generation checks |
| Keyless unique-candidate view references | Dedicated uncached-row materialization preserves distinct results, optional absence, required errors and duplicate cardinality; no invented primary-key identity or membership |
| Generated synchronous navigation handle to internal async loading | Converted model key normalizes once when the holder is created; subsequent loading, warm reference reuse and keyed access add no model-to-provider conversions |
| Cache/index hits and invalidation | Earlier missing-row refill and independent-holder races remain; new cases cover conversion failure/cancellation after valid rows, constructor invalidation and keyless cleanup/publication boundaries |

The GUID cases use the actual SQLite/MySQL/MariaDB writers for Text32, Binary16Rfc4122 and NativeUuid parameter encoding respectively, and assert byte order as well as values. Their controlled readers supply canonical GUIDs. These tests establish capture/materialization/cache-domain separation, not native driver decoding, interruption or server-layout acceptance. Other physical formats retain their existing codec/provider tests and W2 acceptance obligations.

Binary inputs and reader-owned cells are mutated while cleanup is suspended; captured SQL, returned model values and canonical row identities retain their original bytes. Composite parameter order is checked explicitly. Converted-row failures retain already valid individual rows without publishing partial relation/index membership. Constructor-triggered invalidation may return obtained rows but cannot install those rows, membership or the holder as current state.

## Verification

`TransactionMutationFailureTests.RelationKeys.cs` and `.RelationViews.cs` add **29 cases**. Final focused verification passed **29/29 new cases** and **51/51 existing `AsyncRelation_*` cases**, Release / .NET 10, maximum parallelism 8 (`artifacts/w1-relation-layouts-focused-final.json`, `w1-relation-layouts-existing-final.json`).

Broad verification passed **2,755/2,755 unit**, **210/210 Memory**, and **527/527 compliance cases on each SQLite anchor**: **4,019 cases**, zero failures/skips. Reports are `artifacts/w1-relation-layouts-unit-final.json`, `w1-relation-layouts-memory.json`, `w1-relation-layouts-sqlite-file.json` and `w1-relation-layouts-sqlite-memory.json`. Unit/Memory maximum parallelism was 16; compliance was 8. Invocation and artifacts are complete. Local `ValidForEvidence` remains false because these bounded development checks are not canonical full-provider/clean-runner release acceptance.

Core .NET 8/9/10 and unit/dependency/compliance/Memory Release builds passed with zero warnings/errors (`artifacts/w1-relation-layouts-*-build.log`). The integration PR records exact-head CI and merge evidence separately.

Development evidence is retained. `artifacts/w1-relation-view-probe.json` reproduces the async rejection while the first synchronous result succeeds; `w1-relation-view-identity-probe.json` additionally reproduces the wrong synchronous result for a second key. The first converted-key probe incorrectly expected no normalization when creating a generated handle; the corrected test requires exactly that one conversion and no further conversion during loading/reuse.

The initial broad unit run passed 2,754/2,755. Its older provider-matched collection identity fixture left background maintenance enabled despite assuming one stable cache generation. The reference and collection fixtures now stop maintenance after creating the relevant table cache, matching the existing controlled relation fixture. Explicit invalidation races remain enabled in their dedicated cases; production cleanup is unchanged. The initial failed report is retained as `artifacts/w1-relation-layouts-unit.json`.

Planning documentation is excluded from the published DocFX site. No site navigation or presentation changed; relative planning links and whitespace are checked separately.

## Remaining W1 gates

This closes the bounded key-layout/target-shape follow-through from the first relation slice. Full diagnostic correlation, comparable .NET 10 allocation/coordination measurements and the final cross-family I/O audit remain open. Native synchronous adapter ownership/override compatibility, metadata/provisioning and owning-root disposal still require their own integration slices. W2/W3/W5, SQLite W0-F1 and release approval are not closed by this evidence.
