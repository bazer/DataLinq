> [!WARNING]
> Internal W1 orchestration and controllable-provider evidence only. Native SQLite/MySQL/MariaDB provisioning remains W2; public factory defaults, declarations and packed consumers remain W3. W1 and SQLite W0-F1 remain open.

# W1 Async Provisioning

**Date:** 2026-09-19. Implements the internal provisioning boundary in [AAPI-84/85](Async%20Public%20API%20Decisions.md#aapi-84-async-provisioning-mirrors-existing-creation-helpers) and the [W0 provisioning inventory](W0%20IO%20Execution%20Map.md#metadata-probes-provisioning-and-setup). The [completion audit](W1%20Completion%20Audit.md) retains the full remaining W1 scope.

## Invocation capture and compatibility boundary

Internal `PluginHook` counterparts use the existing atomic registration snapshot. They select one SQL factory before invoking local SQL generation, and never re-read the registry after a generator callback or suspension. A registration replacement cannot redirect an in-flight operation.

The internal factory entry captures script text, destination name, connection string and foreign-key setting before binding an explicit `IAsyncSqlProvisioningFactory` plan. Provider-specific effective settings also belong to that plan's I/O-free capture. The request is immutable and its string representation omits both script and connection configuration. Existing provisioning executes `Sql.Text`; this does not create a general parameterized-script executor or start cloning unused parameter bindings.

Ordinary null validation precedes registration failure mapping and pre-cancellation. Missing registration and SQL-generation failures retain their existing `Option` behavior, including the original contained failure object. Generation remains synchronous local work. Synchronous-only factories cannot inherit async support through `DbCommand` defaults, a thread-pool wrapper or a call to their synchronous `CreateDatabase` method. Unsupported capability is rejected before request cancellation and before creation work.

No public interface changes or native provider implementation are included. The `partial` declaration of `PluginHook` organizes internal code without adding public methods. Existing synchronous provisioning remains direct and unchanged.

## Resource and failure orchestration

The captured plan validates lifecycle, inputs and initialization/execution/cleanup capabilities before pre-cancellation. Session construction is I/O-free and transfers ownership only on success; a failing implementation must clean partial construction it cannot hand off. The coordinator captures the session's access and command factory before initialization can suspend, then validates the owned command capability before initialization.

Initialization receives the request token and may create/open the destination. It is awaited to settlement even if the provider does not cooperate with cancellation. After initialization, a cancellation checkpoint prevents subsequent command creation/dispatch. This does not undo any initialization effects.

Script execution uses the existing `OwnedCommandExecution` non-query path, including its command validation, explicit provider dispatch, command ownership and independent async command cleanup. The session is then cleaned up independently, even after initialization, command construction, execution or command cleanup fails. Both cleanup boundaries are parameterless and never inherit an already-canceled request token. No normal result or failure escapes while owned cleanup is suspended.

Original operational exceptions and cancellation escape as exceptions, not ordinary failed options. Nested failure contexts retain ordered, identity-deduplicated cleanup failures. Completion is `NotApplicable`, transaction identity is absent and recovery actions are `None`: provisioning is not a tracked transaction, and no rollback/replay or whole-script atomicity claim is inferred. A confirmed command result is not retroactively canceled during cleanup, but cleanup failure still prevents a normal result.

The coordinator has no automatic retry, drop, delete-file or object-removal path. Session disposal must release only its own execution resources; preserving the intended in-memory database and any retained keeper is a native-adoption obligation. Controllable fixtures record destination creation separately from execution-resource ownership and prove that failure/cancellation settles cleanup without reverting that state. They do not prove real DDL atomicity, actual keeper lifetime or provider cancellation limits.

## Verification

`AsyncProvisioningTests` covers direct/registered entry points; validation, missing/legacy capability and option behavior; registry replacement during generation and suspension; text-only capture; replacement of session collaborators during initialization; pre-cancellation; partial initialization effects; suspended command/session cleanup; non-cooperative initialization; confirmed result timing; construction/handoff failures and repeated exception identity.

Final focused Release/.NET 10 verification passed **35/35**, maximum parallelism 8 (`artifacts/w1-provisioning-focused.json`). Broad checks passed **2,820/2,820 unit**, **210/210 Memory**, and **527/527 compliance on each SQLite anchor**: **4,084 cases**, zero failures/skips. Reports are `artifacts/w1-provisioning-unit.json`, `w1-provisioning-memory.json`, `w1-provisioning-sqlite-file.json` and `w1-provisioning-sqlite-memory.json`. Unit/Memory maximum parallelism was 16; compliance was 8. Invocation and artifacts are complete. Local `ValidForEvidence` remains false because these bounded development checks are not canonical full-provider/clean-runner release evidence.

Core .NET 8/9/10 and unit/dependency/compliance/Memory Release builds passed with zero warnings/errors (`artifacts/w1-provisioning-*-build.log`). Whitespace and 99 relative planning links passed validation. Planning docs are excluded from the published DocFX site; navigation/presentation did not change. The integration PR records exact-head CI and merge evidence separately.

The first focused run exposed three test-assertion mistakes: comparing the `Option` failure wrapper instead of unwrapping its contained failure, and querying a probe command's deliberately unsupported parameter collection in two cases. The report is retained as `artifacts/w1-provisioning-initial-probe.json`; these were corrected without changing production semantics.

## Remaining work

This supplies internal provisioning orchestration, not completion of the combined administrative area. Fresh multi-query metadata reading, copied configuration/include lists, per-command timeout propagation, complete-result publication, observational validation identity, probe failure mapping and explicit journal-mode execution remain W1/W2 integrations according to the accepted scope. Runtime schema comparison remains W5.

Native provisioning must still prove effective connection normalization, MySQL/MariaDB script execution and partial DDL effects, SQLite file/named-memory behavior and keeper ownership, command/connection cleanup after native failures, and actual interruption limits. W3 must prove public factory/default/interface/concrete receiver compatibility against published 0.9.2 consumers. Complete telemetry/correlation, comparable .NET 10 coordination costs and the final requirement-to-I/O-path audit remain W1 closeout gates.
