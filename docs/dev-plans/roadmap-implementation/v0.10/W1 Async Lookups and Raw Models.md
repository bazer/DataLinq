> [!WARNING]
> Internal W1 evidence, not a public async API or native-provider acceptance gate.

# W1 Async Lookups and Raw Models

**Date:** 2026-09-18. This extends [async model queries](W1%20Async%20Model%20Query%20Batches.md) and [canonical async loading](W1%20Canonical%20Async%20Row%20Loading.md). It addresses the internal orchestration behind AAPI-49/50 and AAPI-100, using the reader/capture/ownership rules in AAPI-17–21 and AAPI-57/58. The [full W1 audit](W1%20Completion%20Audit.md) remains open.

## Lookup behavior

`AsyncModelLookup` separates provider-domain `DataLinqKey` input from model-side components. The latter run through `KeyFactory` once, in metadata order, before the first suspension. The transaction owner covers conversion, so user converters cannot re-enter that transaction. Supported mutable key storage is owned before dispatch; arbitrary user objects are not deep-cloned. Provider keys are not sent through model-to-provider conversion again.

`TableCache.GetProviderRowAsyncCore` reuses strict neutral loading for supported neutral key shapes and uses SQL matching for provider-sensitive shapes. A matching stored string key may differ from the requested spelling. The returned row is cached under its actual canonical identity, never a fabricated lookup alias. Generation checks prevent a suspended old read from overwriting newer entries, and transaction rows stay transaction-local.

Provider-sensitive single lookup preserves the existing synchronous first-row behavior, then awaits reader and owned-command cleanup before model construction/publication. It does not impose neutral CLR requested-key equality or duplicate-row validation on that legacy path. Neutral singular loading retains its stricter validation. This first-row rule does not change the separate exactly-one reference-navigation contract.

Only a valid missing row returns null. Shape/conversion errors, unsupported capability, provider failure and cancellation remain failures. Warm cache hits still validate capability, cancellation and transaction ownership. The tests compare the existing `DataLinqKey.Null` sentinel behavior explicitly: a neutral integer lookup rejects it, while the provider-sensitive string path executes its existing null predicate and returns null for no match. This is not a global redefinition of null keys or the distinct C# null input shortcut.

Eligible fluent simple-key queries now use this shared lookup, including provider-sensitive keys. The initial key command is bound/validated but not dispatched for this shortcut. Existing paging, join and derived-source eligibility guards remain in force; queries outside the shortcut retain key-first batch loading. Public `ValueTask` lookup declarations and generated helper signatures remain W3 work; these shared execution methods are internal.

## Raw model behavior

Internal `DataSourceAccess.GetFromQueryAsyncCore` and `GetFromCommandAsyncCore` capture per enumeration without I/O. String SQL uses an owned generated command. The separate `IAsyncBorrowedReaderFactory` binds caller commands without cloning, mutating or taking ownership of them, and allows adapters to compose owner-aware lazy initialization. The caller must keep a borrowed command stable throughout active execution and reader lifetime.

Raw results stream through the shared async reader enumerator. Each enumeration resolves table-column names against its actual reader, allowing reordered columns and extra unmapped columns. The full-row decoder takes an explicit ordinal layout, owns supported mutable provider values, and materializes scalar conversions once. Known canonical identity passes into the immutable constructor instead of being reconstructed through another user conversion. Keyless views remain supported. Raw materialization does not publish row-cache entries.

Transaction ownership covers dispatch, current-row materialization, streaming lifetime and asynchronous cleanup. Both cancellation tokens apply. A row already delivered is not undone by later cancellation, while subsequent advancement fails and waits for uncancelable cleanup. Reader failures retain primary/secondary ordering, caller commands remain borrowed even on failure, and escaped helper readers drain before callback success is rejected. The raw failure tests here prove rejection of unknown effects, not the full AAPI-59 rule: raw dispatch must reject continued use even when a provider claims ordinary-read effects. That reader-path gap remains explicit in the [raw command follow-up](W1%20Async%20Raw%20Commands.md#remaining-work), which closes the eager scalar/non-query boundary only.

Synchronous dispatch/materialization implementations remain direct. Native SQL adapters have not adopted these internal capabilities yet; that is W2. Public class/override declarations, legacy-subclass compatibility and consumer evidence are W3.

## Verification

The `AsyncLookup_*` cases cover scalar/composite/model/provider keys, conversion ownership and failure, mutable key capture, provider-sensitive matching and actual cache identity, first-row cleanup, invalidation, transaction cache isolation, missing/error distinctions, null sentinel parity, warm-cache validation and fluent paging guards. The `AsyncRaw_*` cases cover cold/repeated capture, command identity/stability, reordered and missing columns, binary ownership, converted identity, keyless views, constructors and no cache publication, both tokens, cleanup failures, unknown raw effects, lazy initialization and escaped helper draining.

Focused Release / .NET 10 results: **23/23 lookup cases**, **26/26 raw model cases**, and **26/26 existing model-query regression cases** passed. Artifacts: `artifacts/w1-lookups-focused.json`, `w1-raw-focused.json` and `w1-lookups-models-regression.json`.

The initial runs passed 15/17 lookup and 23/24 raw cases. The raw test exposed an unnecessary model-to-provider identity roundtrip during construction; carrying known canonical identity fixed it. Lookup fixture corrections made a failure checkpoint genuinely fail and asserted SQLite's parameterized null predicate. A subsequent 22/23 lookup run exposed shared converter-test state; the first full unit run then passed 2,309/2,310 with only the scheduling-policy guard rejecting newly added global locks. Converter probes now use test-local observations and no new scheduling locks. Initial artifacts are retained as `w1-lookups-focused-initial.json`, `w1-raw-focused-initial.json`, `w1-lookups-focused-fixture-failure.json` and `w1-lookups-raw-unit-initial.json`. The initial build also corrected the raw path's metadata receiver.

Broad Release / .NET 10 verification:

- `artifacts/w1-lookups-raw-unit.json`: **2,310/2,310 passed**, full unit suite, maximum parallelism 16.
- `artifacts/w1-lookups-raw-memory.json`: **149/149 passed**, full Memory suite, maximum parallelism 16.
- `artifacts/w1-lookups-raw-sqlite-file.json` and `w1-lookups-raw-sqlite-memory.json`: **527/527 passed each**, full compliance anchors, maximum parallelism 8.
- Core .NET 8/9/10 and unit/dependency, compliance and Memory Release builds passed with zero warnings/errors. Build logs share the `w1-lookups-raw` artifact prefix.
- Changed planning-document links and whitespace checks pass. No normal documentation, website presentation or navigation changed.

The PR records exact-head CI and integration. No public/native/performance acceptance is inferred from these controllable tests.

## Remaining work

[SQL expression/prepared execution](W1%20Async%20Query%20Plan%20Execution.md) now extends this slice through capture, projection and terminal execution. [Memory async query/narrow lookup execution](W1%20Async%20Memory%20Execution.md) supplies the separate local backend slice. Query telemetry/correlation, non-query/mutation/hydration, async relation coordination and complete relation/index publication, metadata/provisioning and owning-root disposal remain open. W1 closeout also requires the full I/O-map audit and comparable .NET 10 coordination/allocation evidence. The 0.9.2 compatibility baseline and SQLite W0-F1 limitation are unchanged.
