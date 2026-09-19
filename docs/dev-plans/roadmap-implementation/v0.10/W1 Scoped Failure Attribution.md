> [!WARNING]
> Internal W1 attribution, not diagnostics or W1 closeout. Successful-path scope costs and the remaining synchronous reporting audit are still open. W2/W3 and SQLite W0-F1 remain separate gates.

# W1 Scoped Failure Attribution

**Date:** 2026-09-19. Continues [nontransactional correlation](W1%20Nontransactional%20Failure%20Correlation.md) under AAPI-91–95 and the [completion audit](W1%20Completion%20Audit.md).

## Reproduced failure

A reused exception could carry an earlier operation's timeout, cleanup stage and secondary cleanup failure into a new read, even with exactly the same provider and transaction IDs. [ExecutionFailures](../../../../src/DataLinq/Execution/ExecutionFailure.cs) imported those facts before recovery was calculated; correcting identity only in the final snapshot was too late. The eight row/scalar/buffered/lookup cases in `artifacts/w1-attribution-negative-control.json` all failed against the prior implementation.

## Invocation provenance and owned handoffs

[ExecutionFailureScope](../../../../src/DataLinq/Execution/ExecutionFailureScope.cs) carries diagnostic lineage through `AsyncLocal`. A scope contains only its parent marker, never a transaction, command, connection, callback or admission lease. Scoped collection accepts current/descendant reports and rejects earlier or sibling reports before importing cause, stage, operation, secondary failures or cleanup facts. Recovery and independent owned-root cleanup steps use narrower scopes. Original exception identity, immutable historical snapshots, encounter order and explicit transaction admission remain intact. Entering a diagnostic scope grants no execution authority.

Scopes cover the common async reader/scalar/buffered/owned-command coordinators, local lookup/relation wrappers, mutation/completion/helpers, provisioning/metadata/probes/configuration, root disposal, synchronous raw coordinators and guarded synchronous iterator calls. Iterator scopes end before returning control to the consumer; they do not remain ambient across a synchronous `yield`. Direct exception lookup remains a latest-report accessor; internal execution uses scoped observation instead.

[TransactionOperationGate](../../../../src/DataLinq/Execution/TransactionOperationGate.cs) captures immutable reports while the actual lease owns execution. [TransactionCallbackRunner](../../../../src/DataLinq/Execution/TransactionCallbackRunner.cs) can therefore preserve a genuine child failure even when application work suppresses execution-context flow or replaces the exception's direct lookup afterward. Closed-helper draining consumes captured reports rather than rereading mutable lookup state. The lookup dictionary is allocated only after a failure and released when the callback closes; handled failures are not retained by the retired helper.

[MetadataReadContext](../../../../src/DataLinq/Execution/MetadataReadContext.cs) likewise retains each owned command's observed report and cancellation classification at failure time. A custom parser cannot replace that report by changing the direct lookup before returning a partial success.

## Verification

[Integration attribution tests](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.Attribution.cs) and [scope/cleanup tests](../../../../src/DataLinq.Tests.Unit/Core/ExecutionAttributionTests.cs) pass 32 cases: stale same-identity reports, consecutive real calls, mutations, synchronous raw commands, availability classification, metadata snapshots, suppressed-flow helpers, scope restoration, unchanged admission and independent cleanup provenance.

The first broad unit run found five fixture expectations that treated reports attached before execution as current provider evidence. Positive provider-report fixtures now publish at the actual failure boundary; stale cleanup classification instead uses current provider evidence. The original reports remain at `artifacts/w1-attribution-unit-first.json` and `artifacts/w1-attribution-negative-control.json`; no failed evidence was overwritten. Final local reports use the `artifacts/w1-attribution-*` prefix: 3,114 unit, 221 Memory and 528 cases for each SQLite file/memory compliance target, **4,391 passed with zero skips**. All five final focused/broad reports are complete for their invocations, with complete artifacts and exit zero. They have `ValidForEvidence=false` and do not replace the release matrix.

Release builds of core (.NET 8/9/10), unit/dependencies, Memory and compliance passed with zero warnings/errors. Local planning links/anchors and whitespace are checked before PR creation; no published navigation/layout changes are included.

## Remaining gates and cost risk

This implementation allocates scope markers and writes `AsyncLocal` on successful execution, including guarded iterator calls. It is **not allocation-neutral**, and no performance claim follows from the functional results. Comparable .NET 10 runs against the frozen [W0 baseline](W0%20Baseline%20Evidence.md), any required reduction in scope/coordination cost, and explanation of allocation changes remain mandatory before W1 closeout.

A subsequent [initial performance checkpoint](W1%20Initial%20Performance%20Checkpoint.md) captures nine W0 rows twice and a pre-attribution control. It confirms added update allocations before this change and variable timing warnings; all comparisons require review. It does not isolate scope cost or close the six-lane performance requirement.

Boundaries that have not established a scope retain the prior unbounded collector behavior. Direct synchronous eager loaders and their failure publication still need their full audit, along with standalone raw-provider identity, complete cause/stage mappings, telemetry, adapter compatibility readiness and the final requirement/code/test/I/O mapping. This record does not claim those remaining paths are covered. Public types/numeric values, native provider classification, schema comparison, package publication and release approval remain separate work.
