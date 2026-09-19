> [!WARNING]
> Internal W1 read diagnostics with controllable adapters. This does not complete W1, introduce public diagnostics, or prove native provider behavior. W2/W3 and SQLite W0-F1 remain open.

# W1 Async Read Correlation

**Date:** 2026-09-19. Continues [failure correlation and admission diagnostics](W1%20Failure%20Correlation%20and%20Admission%20Diagnostics.md) for captured SQL read paths under AAPI-91–95. Remaining work is tracked in the [completion audit](W1%20Completion%20Audit.md).

## Captured read purpose

[ReadExecutionIdentity](../../../../src/DataLinq/Execution/ReadExecutionIdentity.cs) copies the operation kind and existing provider telemetry-instance identifier into each invocation. It grants no admission and retains no source, provider, transaction, command, SQL, parameters or keys. Transaction-bound reads use the provider identifier captured by their gate. A private child read inherits its explicit owner's kind, including `Unknown`; it cannot infer an unbound mutation kind from a lower-level row load.

The internal reader enumerator, buffered row loader and scalar coordinator publish that identity on failure. Fluent row/key/model/scalar queries, SQL query plans, prepared/direct/local projections and query continuation transforms carry `Query`. Direct-key query shortcuts and query batches keep that identity even though they use cache row lookup machinery. Direct model/provider-key lookups carry `KeyLookup`; reference and collection loaders, including cached membership row reloads, carry `RelationLoad`. Deferred raw readers and raw-model streams carry `RawCommand`.

Raw `DatabaseAccess` without a managed transaction has no provider identifier supplied by this integration and does not invent one. Raw-model source boundaries do have a provider and capture its identifier. Native standalone adapter binding remains part of the later integration audit.

Early query, lookup and relation admission guards carry the requested kind. An overlap rejection identifies both the requested and active operations without changing the active transaction's failure state. This includes existing synchronous query-root construction guards used before async execution; it does not claim complete synchronous failure publication.

## Failure and recovery preservation

Local key conversion, relation waiting and required-reference failures can occur without opening a reader. Their boundary now reports the read identity directly. A fresh local failure under an admitted transaction preserves continue/rollback/dispose recovery because no statement was attempted; independent-source failures advertise no transaction recovery. Validation remains a separate stage. Matching requested-token cancellation remains cancellation rather than a materialization error.

An already-reported child failure keeps its existing completion and recovery policy. The outer reader continuation adds missing correlation without replacing a failed row-load assessment with evidence from an earlier successful key read. Original exceptions and ordered cleanup failures remain intact; cleanup failures retain their own `Dispose` kind. No new provider trust, automatic retry or native interruption claim is introduced.

## Verification

Focused controllable-adapter coverage lives in [TransactionMutationFailureTests.ReadCorrelation](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.ReadCorrelation.cs). It exercises root and transaction paths, dispatch plus command cleanup, prepared/projection and direct-key queries, key conversion without dispatch, relation owner/waiter isolation, nested row-load cleanup restrictions, private owner identity and admission conflicts.

The pre-captured-enumerator negative control reproduced a reporting bug: when enumeration was rejected by an active transaction, the enumerator replaced the gate's `FinishActiveOperation` recovery with `None`. The reporting path now preserves the admission snapshot before work starts, without changing the active transaction. The failed report remains at `artifacts/w1-read-correlation-admission-negative-control.json`; the final focused run passes all **35** cases.

After the final source change, core builds on .NET 8/9/10 and the unit/dependency, compliance and Memory builds succeeded with zero warnings/errors. Broad local runs passed **3,037 unit + 210 Memory + 528 SQLite-file + 528 SQLite-memory = 4,303** cases with no failures or skips. Reports are `artifacts/w1-read-correlation-{focused,unit,memory,sqlite-file,sqlite-memory}.json`; these are bounded local development checks, not full release evidence. All 111 relative links/anchors across this record, the completion audit and the integration plan resolve; `git diff --check` passes. Planning-only changes do not change published DocFX navigation or layout.

## Remaining work

Mutation/Save owner binding, Memory and administrative/root correlation, remaining synchronous reports, complete cause/stage/secondary classification, telemetry, internal adapter compatibility readiness, comparable .NET 10 coordination/allocation evidence and final requirement/code/test/I/O mapping remain W1 obligations. This slice does not change the frozen 0.9.2 compatibility baseline, add public/generated async declarations, close W0-F1 or authorize package publication.
