> [!WARNING]
> Internal W1 execution and controllable evidence, not public/native async mutation support or W1 closeout. Public overload/namespace/inference and generated packed-consumer gates remain W3; native binding remains W2.

# W1 Mutation Contracts

**Date:** 2026-09-18. Continues [tracked mutations](W1%20Async%20Tracked%20Mutations.md) and [database helpers](W1%20Database%20Mutation%20Helpers.md) with internal overload, custom/generated-model, key and finalization evidence. The [completion audit](W1%20Completion%20Audit.md) remains the full W1 checklist.

## Local edits and captured state changes

Internal transaction overloads mirror existing base-mutable, strongly typed mutable and immutable editing families. Insert/update and direct Save require a model. Only immutable-plus-edit and base-mutable-plus-edit Save retain null-means-new behavior; the strongly typed primitive remains strict. Editing is synchronous and exactly once, after argument/lifecycle validation and the initial cancellation check, but before final validation/capture and database execution. No asynchronous editing delegate is introduced. As with any `Action`, an `async void` lambda remains unsupported misuse rather than something this API can await.

Save now chooses insert/update from the lifecycle after local edits, matching synchronous Save and AAPI-27/29/70. In particular, a local edit can reset a new mutable to an existing committed baseline before changing it; the resulting operation is update, not the insert selected during initial preflight. The final capture still happens before suspension. Invalid post-edit assignments win over a token canceled by the delegate, without poisoning the transaction or undoing those local assignments.

An internal `StateChange.ExecuteQueryAsyncCore` routes existing captured candidates through the same transaction-owned command/snapshot/cache/finalization pipeline. It validates the candidate before reservation, preserves explicit low-level execution rather than applying the high-level unchanged-update shortcut, and records the original state-change object on success. Cancellation or capability validation before execution leaves the candidate unstarted and retryable. A started candidate is single-attempt; rejected reuse performs no second command.

## Custom and generated model boundaries

Generated mutable setters, including strongly typed properties, reject edits while the input is reserved. Tests call the internal primitives with actual generated mutable classes; this does not establish public generated extension binding or packed-consumer compatibility.

Legacy `IMutableInstance` objects retain the existing low-level StateChange contract: object-identity submission exclusion, snapshot checks, generated-key assignment and deletion finalization, but no invented managed baseline ownership or authoritative hydration. They therefore do not require a reader capability merely for low-level insert/update. Arbitrary custom setters cannot be intercepted. Pre-execution drift rejects without mutation poisoning; drift discovered after a write poisons the transaction. Releasing the reservation cannot manufacture a trustworthy managed baseline for a custom object that has no such contract.

The same pipeline proves converted generated IDs through provider-to-model assignment and authoritative hydration, canonical scalar lookup parameters, composite key ordering, and frozen relation-impact values derived from the authoritative row. A later edit does not change the recorded relation impact. Private hydration works in write-only transactions.

Failures during generated-value decoding/conversion, pending cache notifications, guarded setter reentry, custom deletion finalization and missing/duplicate/wrong-key hydration are not recorded as successful changes. After writes they poison the transaction, invalidate affected known mutables (including prior work) and preserve conservative recovery. Generated-value failure precedes hydration. Custom-object lifecycle limitations remain explicit.

Finite insert batches enumerate once, preserve input/result/change order and return only after every model is finalized. Empty input still validates transaction state and cancellation, but binds no command and requires no unused async capability. Earlier mutation evidence retains all-input capture, reservation, enumeration-failure and partial-effect coverage.

## Verification

The initial focused run passed **41/41 `MutationContract_*` cases**; the final expanded run passed **45/45**, Release / .NET 10, maximum parallelism 8 (`artifacts/w1-mutation-contracts-focused.json`, `w1-mutation-contracts-final-focused.json`). The additional cases verify unstarted-candidate retry after cancellation/capability rejection and null/overflow generated-value failure before hydration.

Broad Release / .NET 10 verification:

- **2,675/2,675 unit cases**, maximum parallelism 16 (`artifacts/w1-mutation-contracts-unit.json`).
- **210/210 Memory cases**, maximum parallelism 16 (`w1-mutation-contracts-memory.json`).
- **527/527 compliance cases on each SQLite anchor**, maximum parallelism 8 (`w1-mutation-contracts-sqlite-file.json`, `w1-mutation-contracts-sqlite-memory.json`).
- Core .NET 8/9/10 and unit/dependency/compliance/Memory Release builds: zero warnings/errors (`w1-mutation-contracts-*-build.log`).

All **3,939** broad cases passed without skips, with invocation and artifacts complete. Local `ValidForEvidence` remains false: bounded development checks are not canonical full-provider/clean-runner release acceptance. Planning links and whitespace are checked separately; normal docs and website navigation/presentation are unchanged. Exact-head CI and integration evidence are recorded in the PR.

## Remaining gates

These cases extend internal mutation evidence; they do not prove native driver behavior, every public overload/extension receiver, bare-null ambiguity or packed/generated consumers. Those remain W2/W3. Full command/query/transaction diagnostic correlation and mutation coordination/snapshot allocation measurements still belong to W1.

The wider W1 audit remains open for relation owner/waiter coordination and invalidation-safe publication, metadata/provisioning, owning-root disposal, synchronous adapter compatibility, full telemetry, final I/O-map review and comparable .NET 10 performance evidence against 0.9.2. SQLite W0-F1 remains under its recorded limited exception.
