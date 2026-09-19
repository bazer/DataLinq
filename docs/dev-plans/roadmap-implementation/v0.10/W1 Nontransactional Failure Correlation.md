> [!WARNING]
> Internal W1 correlation for Memory and administrative work. This is not complete diagnostics, native provider evidence, public async API availability or W1 closeout. W2/W3 and SQLite W0-F1 remain open.

# W1 Nontransactional Failure Correlation

**Date:** 2026-09-19. Continues [read correlation](W1%20Async%20Read%20Correlation.md) and [mutation correlation](W1%20Async%20Mutation%20Correlation.md) under AAPI-91–95. See the [completion audit](W1%20Completion%20Audit.md) for the remaining gates.

## Administrative operation and provider identity

[AsyncMetadataRead](../../../../src/DataLinq/Execution/AsyncMetadataRead.cs) reports `MetadataRead`, including its sequential reader/scalar commands. Runtime validation-metadata acquisition captures the existing provider telemetry-instance ID before execution; factory import has no provider instance to identify. This is metadata acquisition, not the W5 schema comparison operation.

[AsyncExistenceProbes](../../../../src/DataLinq/Execution/AsyncExistenceProbes.cs) reports `ExistenceCheck` across local/scalar/first-row paths, and [AsyncJournalMode](../../../../src/DataLinq/Execution/AsyncJournalMode.cs) reports `ProviderConfiguration`. Both capture the existing provider telemetry-instance ID before suspension. [AsyncProvisioning](../../../../src/DataLinq/Execution/AsyncProvisioning.cs) reports `Provisioning`; its standalone factory does not manufacture a provider identity.

These operations have no application transaction ID or managed transaction recovery actions. They report `NotApplicable` completion, which does not imply absence of provisioning/configuration effects. Existing Option, availability-only false mapping, cancellation checkpoints, confirmed-command behavior and independent owned cleanup remain unchanged. The focused cases verify `Dispose` attribution for fresh secondary cleanup failures, original exception identity and encounter order; reused-failure classification remains open below.

The shared collector can distinguish an explicitly absent provider identity from a layer that merely has not supplied one. Factory boundaries reject an old provider identity carried by a reused exception, including when the old operation had no transaction. Existing historical snapshots remain unchanged. This advances identity attribution; the remaining full cause/stage/secondary attribution audit is not closed by this change.

## Owned disposal

[OwnedRootDisposal](../../../../src/DataLinq/Execution/OwnedRootDisposal.cs) accepts a copied optional provider ID for cleanup reports. [DatabaseCache](../../../../src/DataLinq/Cache/DatabaseCache.cs) captures its provider's ID during construction and supplies it to both disposal forms. This changes no resource ownership, maintenance shutdown, one-attempt behavior or dependent reader/transaction lifetime. Native provider root binding remains W2; an unbound standalone coordinator cannot invent an ID.

## Memory execution

[MemoryAsyncResult](../../../../src/DataLinq.Memory/MemoryAsyncResult.cs) publishes invocation-local query/key-lookup reports for the existing async local kernels. There is no SQL provider, transaction ID or transaction recovery action; completion is `NotApplicable`. Reusing an exception from SQL cannot import its identity, completion or recovery. The original exception and completed/canceled task behavior are retained. Matching requested-token cancellation is classified as cancellation; an unrelated canceled token on a user exception is not proof of request cancellation. Unproven classification remains unknown.

No thread-pool facade, new asynchronous I/O, result-materialization policy or cache behavior is introduced. Detailed stage/cause mappings, synchronous reporting and telemetry remain part of W1's final classification audit.

## Verification and remaining work

Focused evidence is in [administrative correlation tests](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.NontransactionalCorrelation.cs), [provisioning tests](../../../../src/DataLinq.Tests.Unit/Core/AsyncProvisioningTests.cs) and [Memory correlation tests](../../../../src/DataLinq.Tests.Memory/MemoryAsyncExecutionTests.Correlation.cs): 13 unit and 11 Memory cases pass. The first Memory run exposed a test-fixture mistake: unordered `First` correctly failed capability validation before cancellation. The test now uses supported ordered `First` and explicitly checks the exception type. The original 10/11 report is retained at `artifacts/w1-nontransactional-correlation-memory-focused.json`; the final report uses the `-final.json` suffix. No production change was needed for that failure.

Release builds of core (.NET 8/9/10), unit/dependencies, Memory and compliance completed with zero warnings/errors. Broad regressions passed 3,082 unit, 221 Memory and 528 cases for each SQLite file/memory compliance target: 4,359 total, zero skips. All six final focused/broad reports are complete for their invocations with complete artifacts and exit code zero. These local bounded reports have `ValidForEvidence=false`; they do not replace the release matrix or close W0-F1. Logs/reports use `artifacts/w1-nontransactional-correlation-*`. The three edited planning documents passed 124 local link/anchor checks and `git diff --check`; no published navigation/layout changed.

Remaining W1 work includes standalone raw-provider attribution, complete cause/stage/secondary mappings (including local-edit/preparation failures), synchronous ownership/failure publication, telemetry, internal adapter compatibility readiness, comparable .NET 10 coordination/allocation evidence and final requirement/code/test/I/O mapping. The compatibility baseline remains 0.9.2; no package publication is included.
