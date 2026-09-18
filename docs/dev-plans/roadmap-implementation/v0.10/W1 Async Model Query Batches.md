> [!WARNING]
> Internal W1 evidence, not a public async API or native-provider release gate.

# W1 Async Model Query Batches

**Date:** 2026-09-18. This follows [canonical async row loading](W1%20Canonical%20Async%20Row%20Loading.md) and extends the actual fluent `Select` model path. The [full W1 audit](W1%20Completion%20Audit.md) remains open.

**Follow-up:** [Async lookups and raw models](W1%20Async%20Lookups%20and%20Raw%20Models.md) extends the simple-key shortcut to provider-sensitive keys and adds shared lookup/raw-model execution. The implementation and results below describe this earlier batch-loading slice.

## Implementation

Internal `Select.ExecuteAsyncCore` captures SQL, parameters, projection and provider policy per enumerator without I/O. `ExecuteBufferedAsyncCore` captures at the terminal invocation. Neither edits the caller's `WhatList`. Keyed queries project provider keys, retain the database's ordering/paging and duplicates, then load cache misses in bounded batches of 500 distinct keys. Missing rows are skipped when replaying the original key sequence. Exact neutral simple-key predicates retain the cache shortcut; limits, offsets, joins and derived sources retain the existing shortcut eligibility rules. Provider-sensitive predicates currently use the initial key query rather than a direct-key shortcut.

The initial reader and generated command close before any batch begins. Batches and model constructors share the outer enumeration's private transaction owner. No result is yielded until the finite load succeeds, and that owner remains active during buffered enumeration. Method and enumerator tokens apply throughout. A failed later batch returns no successful partial collection; already valid individual cached rows may remain, but no complete relation/index snapshot is published.

`IAsyncSqlReaderFactory.CaptureInvocation` freezes the binder policy for a multi-command invocation. Retaining an adapter reference alone was insufficient: it could resolve a replacement factory after the first read suspended. The explicit snapshot can bind SQL derived from earlier results without rediscovering provider policy. All finite missing-key batches bind before the first batch suspends. There is no deep-copy claim for arbitrary provider code or delegate closures; an adapter must supply an invocation-stable binder.

Neutral batches reuse strict canonical requested-key/duplicate validation. Other key shapes use provider-matched SQL batches, decode owned canonical values and preserve the existing SQL path's matching rules without applying neutral CLR-equality validation. Every keyed query still selects keys first, including derived sources that expose only keys; replacing that stage with a full-row projection would change valid query semantics. Keyless views retain their captured row layout, including unmapped expression ordinals, and materialize only after cleanup.

The reader enumerator now supports a private post-cleanup continuation. If a child read already attached a settled failure context, the parent preserves it instead of replacing it with evidence from the earlier key read. Otherwise cancellation/local failures use evidence from the latest read. This prevents either incorrectly permitting reuse after an untrusted child failure or unnecessarily forbidding reuse after a trusted child failure. Escaped enumerators remain visible to callback-helper draining.

Each batch captures the cache generation before its read. Invalidation during async cleanup prevents old rows from overwriting newer cache entries or repopulating absent entries. Transaction materialization remains transaction-local, and constructors cannot re-enter the transaction even after native cleanup.

## Verification

The `AsyncModels_*` controllable cases cover ordinary/terminal/repeated capture, adapter and factory-policy replacement, projection isolation, cache hits/misses, database order and duplicate keys, 1,001-key batching, cancellation/failure in later batches, trusted/untrusted child recovery, both tokens during buffered iteration, escaped helper work, binary ownership, string/provider identity, composite keys, converted keys, keyless projections, derived sources, cache shortcut/paging guards, generation races and constructor failure after a valid cached prefix. Synchronous reader methods throw in these fixtures.

Local Release / .NET 10 verification:

- `artifacts/w1-model-batches-focused.json`: **26/26 passed**.
- `artifacts/w1-model-batches-unit.json`: **2,261/2,261 passed**, full unit suite, maximum parallelism 16.
- `artifacts/w1-model-batches-memory.json`: **149/149 passed**, full Memory suite, maximum parallelism 16.
- `artifacts/w1-model-batches-sqlite-file.json` and `w1-model-batches-sqlite-memory.json`: **527/527 passed each**, full compliance anchors, maximum parallelism 8.
- Core .NET 8/9/10 and unit/dependency, compliance and Memory Release builds passed with zero warnings/errors. Logs share the artifact prefix. Planning links and whitespace checks pass; no normal docs/navigation changed.

The initial focused run passed 15/17 cases and exposed the adapter-factory capture bug. Its failed artifact is retained as `artifacts/w1-model-batches-focused-initial.json`; the invocation snapshot fixed both failures. Initial fixture builds also corrected keyless metadata to a view and corrected fluent/provider fixture types. The PR records exact-head CI. These are internal development results, not packed public/native acceptance or performance evidence.

## Remaining Work

This does not integrate expression/prepared backends or raw string model execution, freeze public signatures, prove native cancellation, or finish all provider-sensitive direct-key shortcut/lookup normalization cases. Query-level telemetry/correlation and .NET 10 coordination/allocation comparisons still require the broader W1 audit. Relation load coordination, complete index/reference/collection publication, non-query/mutation/hydration, metadata/provisioning and owning-root disposal also remain open. The frozen 0.9.2 compatibility baseline, .NET 10 benchmark target and SQLite W0-F1 limitation are unchanged.
