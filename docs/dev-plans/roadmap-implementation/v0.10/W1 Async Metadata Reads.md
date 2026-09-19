> [!WARNING]
> Internal W1 orchestration and controllable-provider evidence only. Native metadata readers remain W2, public factory declarations/consumers remain W3, and runtime schema comparison remains W5. W1 and SQLite W0-F1 remain open.

# W1 Async Metadata Reads

**Date:** 2026-09-19. Implements the internal metadata-read boundary in [AAPI-65/67](Async%20Public%20API%20Decisions.md#aapi-65-async-live-metadata-reading-retains-the-option-contract), the runtime-reader separation in [AAPI-108](Async%20Public%20API%20Decisions.md#aapi-108-validation-include-is-comparison-scope-and-empty-schemas-are-valid-input), and per-command timeout plumbing in [AAPI-109](Async%20Public%20API%20Decisions.md#aapi-109-runtime-validation-command-timeout-units-and-bounds). The [completion audit](W1%20Completion%20Audit.md) retains the full remaining W1 scope.

## Captured import and observational runtime reads

[AsyncMetadataRead](../../../../src/DataLinq/Execution/AsyncMetadataRead.cs) supplies internal import-factory and provider-bound runtime-reader entry points. [MetadataReadContracts](../../../../src/DataLinq/Execution/MetadataReadContracts.cs) requires explicit capability, I/O-free capture/validation, an owned unopened session, and an async parser using the common command context. Session construction transfers ownership only on success; the implementation must clean partial construction it cannot hand off. No public interface or native provider is changed. Synchronous-only factories/providers fail explicitly before request cancellation; there is no synchronous fallback or thread-pool facade.

Import copies mutable `Include` and captures the current naming settings, logger, destination identity and connection string before suspension. Incidental request formatting omits the connection string. Runtime reading has a separate purpose and takes no import `Include`: W5 owns comparison scope and its model-name validation. A readable existing empty database can produce a complete empty definition, while empty/missing-selected-object import behavior stays separate. Failed or missing definitions never become fabricated empty success.

Runtime capture binds to the actual provider, including its effective normalized identity and existing keeper ownership. Each invocation creates a fresh session; it does not borrow an application's active transaction. The contract forbids reconstructing a runtime provider, creating a missing database, changing journal settings, migrating or repairing schema. These are native-adoption obligations, not claims that controllable tests prove SQLite/server behavior. Multiple metadata queries still do not promise an atomic snapshot during concurrent DDL.

## Command and session lifetimes

[MetadataReadContext](../../../../src/DataLinq/Execution/MetadataReadContext.cs) captures access/command collaborators before opening can suspend. Its buffered reader and scalar operations share one sequential admission gate. Each snapshots SQL and parameter bindings, creates one owned command, executes through the existing explicit async access contracts, and settles command/reader cleanup before another command can start. Local metadata construction stays synchronous and uses the existing `MetadataDefinitionFactory` in the fixture.

Timeout normalization uses integer ticks: null leaves the command default alone, zero stays unlimited, positive values round up to seconds, and negative or greater-than-2,147,483-second values fail before I/O. Explicit timeout support must be validated; the common wrapper applies the setting to every actual created command, including scalar commands. A setter failure occurs after command ownership transfers, so it still triggers async cleanup. Timeout is not a connection-opening, whole-read or recovery-rollback deadline.

The request token reaches session opening, reader acquisition, each row advance and scalar execution. Opening/execution remain owned until settlement when a provider ignores cancellation. Independent parameterless reader, command and session cleanup never inherit a canceled request token. Cancellation checks after cleanup prevent publication of a canceled read, including a scalar result or an otherwise completed definition.

The context retains failures even if a parser catches them. Overlapping commands are rejected without closing the active reader and prevent a later partial success. Returning while a reader/scalar command is unfinished closes admission, drains that owned command, and fails the parse. A closed context cannot execute again. This drains only the metadata session's owned work, not application readers or transactions.

Non-cancellation operational exceptions become failed `Option` values retaining their original exception objects. Cancellation escapes as cancellation. Existing diagnostic failures remain unchanged on clean disposal; a subsequent cleanup failure is aggregated alongside the original diagnostic. Nested reader/command/session failures retain encounter order and identity deduplication. Completion is `NotApplicable`, transaction identity is absent and recovery actions are `None`; no retry, rollback, repair or whole-schema consistency promise is inferred.

## I/O-map reconciliation

The [frozen W0 inventory](W0%20IO%20Execution%20Map.md#metadata-probes-provisioning-and-setup) remains unchanged. Current native source inspection found these metadata shapes:

| Native path | Internal expression available now | Remaining native proof |
| --- | --- | --- |
| MySQL/MariaDB generated information-schema queries and extra index/column/check-constraint reads | Sequential buffered reader commands with per-command settings and owned cleanup | Translate actual query/parser paths without constructing a setup-performing runtime provider; preserve server metadata fidelity and resource ownership |
| MySQL/MariaDB `SHOW CREATE VIEW` | Buffered reader command | Native result/failure/cancellation and timeout behavior |
| SQLite `sqlite_master`, table/index/foreign-key PRAGMA reads | Buffered reader commands | Effective file/named-memory identity, keeper lifetime, missing-database no-creation and actual driver limits |
| SQLite original table SQL, currently read by `ExecuteScalar<string>` | Owned scalar command under the same admission/timeout/cleanup boundary | Native scalar result/parsing behavior and command interruption limits |

This expresses the metadata I/O shapes; it does not port the native parsers or prove their fidelity. Boolean probes, journal-mode execution and constructor/setup auditing retain their separate obligations.

## Verification and observed regression lead

The TUnit partial [metadata tests](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.Metadata.cs) passed **58/58** focused Release/.NET 10 cases (`artifacts/w1-metadata-focused.json`, maximum parallelism 8). They cover capture, actual reader/scalar timeout settings and tokens, complete frozen definitions, empty/import separation, fresh provider-bound identity, independent application transactions, explicit unsupported capability, missing results, non-cooperative opening, capture/creation/reading/parsing/logging failures, overlapping/abandoned commands, cancellation and ordered cleanup failures.

Broad checks passed **2,878/2,878 unit**, **210/210 Memory**, and **527/527 compliance on each SQLite anchor**: **4,142 cases** on the passing runs, zero skips. Reports are `artifacts/w1-metadata-unit.json`, `w1-metadata-memory.json`, `w1-metadata-sqlite-file.json` and `w1-metadata-sqlite-memory-repeat.json`. Unit/Memory parallelism was 16; compliance was 8. These are complete local invocations/artifacts with exit zero, not canonical full-provider/clean-runner evidence (`ValidForEvidence` remains false).

The first in-memory compliance run had **526 passes and one failure**, retained in `artifacts/w1-metadata-sqlite-memory.json`: `RequiredReferenceTests.ScalarReferences_InvalidationRechecksMissingAndPreviouslyResolvedTargets`, line 61, returned different required/optional parent instances. The full rerun passed, as did the six-test reference class and 20 independent repetitions of that class. Those passes did not resolve the failure. Initial inspection identified startup no-op age cleanup as a plausible publication-generation race; the separately verified disposition is [below](#reference-identity-test-follow-up). This slice adds no call from native providers into the metadata coordinator.

Core .NET 8/9/10 and unit/dependency/compliance/Memory Release builds passed with zero warnings/errors (`artifacts/w1-metadata-*-build.log`). The prebuilt-graph guard correctly rejected a stale unit dependency after a final comment edit; rebuilding preceded the final test runs. Planning docs are excluded from DocFX, and published navigation/presentation did not change. The integration PR records exact-head CI separately.

## Reference-identity test follow-up

The underlying conservative publication behavior was already reproduced by `CachePublicationTests.ScheduledCleanupDuringConstructionLeavesAnUncachedResult` in [PR #148](https://github.com/bazer/DataLinq/pull/148). Cleanup advances the read generation even if it evicts no rows. A load overlapping that change may return a valid but uncached instance; two navigations need not return the same instance across that boundary. The failed required-reference assertion assumed a stable cache while leaving startup maintenance enabled.

Four required-reference tests now stop and join their own scheduler before asserting warm-load counts or reference identity. Their explicit insertion, deletion and invalidation checks remain unchanged. Custom-holder and transaction tests do not receive a blanket scheduler exemption, and production cache behavior is untouched.

`ScalarReferences_CleanupDuringConstructionPreservesValuesWithoutStableIdentity` uses the existing provider-scoped constructor barrier to exercise the actual generated required/optional properties. It runs both controls: no overlap gives the same instance; a forced zero-eviction scheduled cleanup during parent construction gives different instances with the correct value, followed by stable shared identity on subsequent loads. The test does not sleep or rely on a lucky interleaving. This supplies a deterministic explanation and regression contract rather than treating repeated green runs as a fix.

Follow-up Release/.NET 10 checks passed **7/7** focused reference cases and **528/528 compliance on each SQLite anchor**, zero failures/skips. Reports are `artifacts/w1-reference-isolation-focused.json`, `w1-reference-isolation-sqlite-file.json` and `w1-reference-isolation-sqlite-memory.json`; all invocations/artifacts are complete, with exit zero and local `ValidForEvidence=false`. The compliance/dependency build passed with zero warnings/errors (`artifacts/w1-reference-isolation-build.log`). Server-provider execution is recorded by the follow-up PR's exact-head CI. The original failed metadata regression report is retained.

## Remaining work

Native factory/default compatibility, actual import filtering/metadata fidelity, provider timeout/cancellation classification, SQLite identity/keeper/no-creation evidence and public consumers retain W2/W3 gates. W5 still owns configuration callbacks, comparison `Include`, finalized-model validation, comparison results, severity policy and operational-failure translation. No schema validator is shipped by this internal reader.

Boolean probes, explicit journal-mode orchestration, complete diagnostics/correlation, internal adapter compatibility readiness, comparable .NET 10 coordination/allocation measurements and final requirement-to-I/O-path verification remain W1 work. Published compatibility remains 0.9.2; the [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception) still does not close W0-F1 or authorize native/public/release acceptance.
