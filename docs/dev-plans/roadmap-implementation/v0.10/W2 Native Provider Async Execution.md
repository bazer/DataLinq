> [!WARNING]
> W2 is in progress. Internal native-provider implementation is not public async support, W0-F1 closure or release approval.

# W2 Native Provider Async Execution

**Started:** 2026-09-24, from `v0.10` commit `5d5def9a` after [W1 closeout](W1%20Closeout.md).

**Workflow:** one branch, `codex/0.10-w2`, and one draft PR, **Implement W2: native provider async execution**, targeting `v0.10`. The [accepted workflow exception](Branch%20PR%20and%20Benchmark%20Workflow.md#w2-single-pr-exception) preserves coherent commits, incremental reviews and final merge-commit integration. No merge or publication is authorized by opening the PR.

## Scope And Acceptance

Bind W1's internal capabilities to the native providers, retain direct synchronous execution, and prove actual operation outcomes and owned-resource cleanup. MySQL and MariaDB share the implementation but require evidence against both server families. SQLite file and in-memory paths require explicit blocking, locking, cancellation and setup evidence; awaitable signatures do not establish interruptible native calls.

The [W1 I/O handoff](W1%20Closeout.md#f21-requirement-and-io-reconciliation), [W0 I/O map](W0%20IO%20Execution%20Map.md), [AAPI decisions](Async%20Public%20API%20Decisions.md) and [W2 exit gate](Implementation%20Order%20and%20Integration%20Plan.md#w2-native-provider-async-execution) remain authoritative. This plan breaks down that scope without reopening accepted policies.

## Milestones And Evidence Matrix

Each cell progresses separately through implementation and verification. A passing test with a controllable adapter cannot be recorded as native-provider proof.

| Milestone / required behavior | MySQL | MariaDB | SQLite file / memory |
| --- | --- | --- | --- |
| W2.1 Native standalone open, scalar/non-query dispatch, reader advancement and owned cleanup; validation/pre-cancellation; borrowed command ownership | Planned first slice | Shared first slice, separate verification | Pending |
| W2.2 Lazy first-use transaction initialization, sync/async admission, private publication, partial-initialization cleanup and attachment | Pending | Pending | Pending; preserve deferred Serializable begin |
| W2.3 Captured query/key/relation/fluent integration, actual row conversion, complete/invalidation-safe cache publication and early reader cleanup | Pending | Pending | Pending |
| W2.4 Tracked mutations/private hydration, commit/rollback certainty, independent recovery budget, disposal and mixed execution | Pending | Pending | Pending |
| W2.5 Metadata parsers, existence/availability, per-command timeout, provisioning, journal mode, keeper and owning-root lifetimes | Pending | Pending | Pending |
| W2.6 Native interruption, soft/hard cancellation, timeout/connection trust and no-dispatch/cleanup classification; final parity and performance review | Pending | Pending | Pending official fix adoption and affected reruns |

The first slice starts on the shared MySQL/MariaDB standalone access path so native work can proceed while the SQLite servicing package is pending. It must not imply transaction support before W2.2/W2.4, or public API availability before W3. Deterministic W1 tests remain necessary for failure combinations that live drivers cannot reliably reproduce.

## Provider Rules

- Validate lifecycle and concrete native capability before pre-cancellation and I/O. Reject unsupported commands/sources explicitly. Do not use `Task.Run`, sync-over-async or inherited synchronous fallbacks as native async proof.
- Separate connection/open/setup failure from command dispatch. Preserve original primary errors and ordered cleanup failures, including failed reader handoff and observer failure.
- Keep command ownership explicit. Owned commands/readers/connections receive independent cleanup; borrowed commands remain usable by their caller after the operation's owned resources settle.
- A canceled ordinary read is reusable only after cleanup and verified provider/transaction integrity. Interrupted writes retain W1 poisoning regardless of socket survival. Confirmed commit remains committed after later failures; lost confirmation remains unknown.
- Preserve cancellation, command timeout, MySqlConnector cancellation escalation and recovery rollback timeout as separate settings. The recovery budget is cooperative and never permission to abandon active work into a pool.
- Preserve synchronous SQLite constructor keeper/WAL setup and MariaDB version probing. No new async construction promise, automatic migration, retry or destructive provisioning recovery is introduced.

## SQLite Dependency Gate

The [upstream fix is merged and issue #39008 targets 10.0.13](SQLite%20Pool%20Ownership%20Investigation.md). The user explicitly authorizes W2 development and native tests while waiting. DataLinq still pins 10.0.11; this is not corrected-package evidence. Adopt the corrected official package through `master`, merge forward, and rerun the affected ownership/concurrency/provider evidence before closing W0-F1 or SQLite acceptance. Public API freeze and release approval remain gated.

## Verification And Review

- Record focused TUnit commands, actual provider targets, results and source identities at each milestone. Use the Testing CLI and preserve runtime-state discovery across targeted server runs.
- Review each coherent slice before considering its milestone verified. Run broader provider regression tests at integration checkpoints; retain failures and classify infrastructure limitations explicitly.
- Complete the per-operation matrix, representative sync/async parity, pre-dispatch/in-flight/cleanup cancellation, diagnostics and existing cache/lifecycle assertions before W2 closeout.
- Preserve the frozen W0 baseline and W1 cost dispositions. Final candidate performance and telemetry evidence must identify the actual source commit; intermediate runs do not replace final acceptance.
- Public/generated signatures and packed consumers remain W3; hosting remains W4; startup comparison/policy remains W5. No scope expansion, package publication or merge is implied.

## Execution Record

- **2026-09-24, kickoff:** recorded the accepted single-PR workflow, updated upstream SQLite status and mapped W1's provider handoff. Native implementation and verification begin with W2.1. No W2 milestone is closed by this planning commit.
