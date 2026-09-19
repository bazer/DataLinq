> [!WARNING]
> Internal W1 orchestration and controllable-provider evidence only. Native probe implementations remain W2 and public database/provider declarations and consumers remain W3. W1 and SQLite W0-F1 remain open.

# W1 Async Existence Probes

**Date:** 2026-09-19. Implements the internal probe boundary in [AAPI-64](Async%20Public%20API%20Decisions.md#aapi-64-async-existence-checks-preserve-their-distinct-probe-semantics). The [W0 inventory](W0%20IO%20Execution%20Map.md#metadata-probes-provisioning-and-setup) and [completion audit](W1%20Completion%20Audit.md) retain the wider I/O and evidence obligations.

## Captured execution and owned resources

[AsyncExistenceProbes](../../../../src/DataLinq/Execution/AsyncExistenceProbes.cs) adds internal provider entry points for availability, database existence and table existence. A provider must explicitly implement the internal source capability. Null provider/table validation, captured provider lifecycle/capability validation and command-factory validation precede the relevant cancellation or dispatch boundary. A synchronous-only provider does not gain async support by calling its existing methods.

The immutable request captures the probe kind and optional database/table arguments. The source binds those arguments to its effective provider identity before suspension. Backend-specific name normalization, whether a database-name override is meaningful, connection configuration and keeper ownership belong to that captured plan. These are not inferred uniformly across SQLite and server providers. Each invocation obtains fresh state without borrowing an application transaction or reconstructing a provider.

The captured plan has three execution shapes:

| Shape | Common execution | W0 path it can express |
| --- | --- | --- |
| Local | Direct local callback and cancellation checks, without a worker or execution session | SQLite file existence or an existing in-memory identity check |
| Scalar | Owned scalar command, independent async command/session cleanup, then local result interpretation | MySQL/MariaDB availability and information-schema existence queries |
| First row | Owned reader acquisition, one row advance, then reader/command/session cleanup | SQLite's table-existence query |

The session creator constructs unopened resources without I/O and transfers ownership on success; it must clean partial construction it cannot hand off. Access and command-factory collaborators are captured before opening can suspend. The opening, acquisition and row/command execution paths receive the request token. Non-cooperative opening remains owned until it settles; cancellation cannot abandon its session. Cleanup uses independent parameterless async disposal, and results are checked for cancellation again after cleanup. First-row success does not skip cleanup or read the remaining rows.

Local checks retain their narrow semantics. A future `File.Exists` binding can legitimately return false for some filesystem/access conditions; an unexpected exception from a local callback is not swallowed by a generic availability catch. Local work can complete synchronously and does not use `Task.Run`.

## Failure policy

Only availability may map an explicitly classified expected provider/open failure to false. Classification is an I/O-free provider decision, applied after owned cleanup has settled. The common boundary requires all of these conditions:

- The operation is `FileOrServerExists`, and the failure occurred during opening, command execution or row loading.
- No request cancellation has been observed, the exception is not cancellation, and an existing failure context does not report cancellation.
- The failure is not an argument, unsupported-capability or disposed-object error.
- There is no cleanup failure or secondary failure, including a repeated exception object that failed both execution and cleanup.
- The captured classifier recognizes the failure and returns without throwing or observing request cancellation.

Database/table query failures remain exceptions, not absent-object results. Result interpretation errors remain exceptions. A classification failure is retained alongside the original primary failure instead of hiding it. An earlier provider failure is preserved if cancellation occurs while classifying it, but it cannot become false. Original exception identity, ordered secondary failures and cleanup facts flow through the existing internal failure context. Completion is `NotApplicable`, transaction identity is absent and recovery actions are `None`; probes do not perform automatic retries, rollback or repair.

The [runtime metadata reader](W1%20Async%20Metadata%20Reads.md) does not call a boolean precheck. It attempts the real read so that connection, cancellation and schema-read failures remain available to W5 validation. A successful probe does not promise that a subsequent operation will succeed.

## Verification

[Controllable probe tests](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.Probes.cs) passed **56/56** focused Release/.NET 10 cases (`artifacts/w1-probes-focused.json`, maximum parallelism 8). Coverage includes the three execution shapes, true/false results, direct local completion, captured effective identity and arguments, collaborator/interpreter replacement during suspended opening, fresh calls with an independent application transaction, unsupported synchronous providers with an explicit no-fallback counter, validation/cancellation ordering, first-row cleanup, non-cooperative opening, selective availability mapping and original/secondary cleanup failures.

Two adversarial cases initially failed: an ordinary exception with an attached cancellation cause could be classified as unavailable when the request token was not canceled. The negative-control report is retained as `artifacts/w1-probes-cancellation-negative-control.json` (**0/2 passed**). The common eligibility check now honors that existing cancellation evidence as well as exception type and request token; both cases pass in the final 56-case run. This proves handling of reported internal evidence, not any native driver's cancellation classification.

Broad local checks passed **2,934/2,934 unit**, **210/210 Memory**, and **528/528 compliance on each SQLite anchor**: **4,200 cases**, zero failures/skips. Reports are `artifacts/w1-probes-unit.json`, `w1-probes-memory.json`, `w1-probes-sqlite-file.json` and `w1-probes-sqlite-memory.json`. Unit/Memory parallelism was 16; compliance was 8. Invocations/artifacts are complete with exit zero. `ValidForEvidence=false` remains correct for these bounded development runs; they are not canonical full-provider/clean-runner release evidence.

Core .NET 8/9/10 and unit/dependency/compliance/Memory Release builds passed with zero warnings/errors (`artifacts/w1-probes-*-build.log`). Planning docs are excluded from DocFX and published navigation/presentation did not change. The PR records exact-head CI and merge verification separately.

## Remaining work

No native or public probe implementation changes here. W2 must bind actual server query/connection failure classification, schema/table identifier rules, SQLite file/named-memory behavior, no missing-database creation, independent resources and real driver interruption limits. W3 must verify database forwarding, public signatures/default capability behavior and packed/custom consumers against the 0.9.2 compatibility baseline.

Explicit journal-mode orchestration and constructor/setup auditing remain open, together with complete diagnostics/correlation, internal adapter compatibility readiness, comparable .NET 10 coordination/allocation evidence and the final cross-family I/O audit. The [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception) still does not close W0-F1 or authorize native/public/release acceptance.
