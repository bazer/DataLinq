> [!WARNING]
> W2 is in progress. Internal native-provider implementation is not public async support, W0-F1 closure or release approval.

# W2 Native Provider Async Execution

**Started:** 2026-09-24, from `v0.10` commit `5d5def9a` after [W1 closeout](W1%20Closeout.md).

**Workflow:** one branch, `codex/0.10-w2`, and [draft PR #229, Implement W2: native provider async execution](https://github.com/bazer/DataLinq/pull/229), targeting `v0.10`. The [accepted workflow exception](Branch%20PR%20and%20Benchmark%20Workflow.md#w2-single-pr-exception) preserves coherent commits, incremental reviews and final merge-commit integration. No merge or publication is authorized by opening the PR.

## Scope And Acceptance

Bind W1's internal capabilities to the native providers, retain direct synchronous execution, and prove actual operation outcomes and owned-resource cleanup. MySQL and MariaDB share the implementation but require evidence against both server families. SQLite file and in-memory paths require explicit blocking, locking, cancellation and setup evidence; awaitable signatures do not establish interruptible native calls.

The [W1 I/O handoff](W1%20Closeout.md#f21-requirement-and-io-reconciliation), [W0 I/O map](W0%20IO%20Execution%20Map.md), [AAPI decisions](Async%20Public%20API%20Decisions.md) and [W2 exit gate](Implementation%20Order%20and%20Integration%20Plan.md#w2-native-provider-async-execution) remain authoritative. This plan breaks down that scope without reopening accepted policies.

## Milestones And Evidence Matrix

Each cell progresses separately through implementation and verification. A passing test with a controllable adapter cannot be recorded as native-provider proof.

| Milestone / required behavior | MySQL | MariaDB | SQLite file / memory |
| --- | --- | --- | --- |
| W2.1 Native standalone open, scalar/non-query dispatch, reader advancement and owned cleanup; validation/pre-cancellation; borrowed command ownership | Implemented internally; bounded native tests on 8.4/9.7 below | Implemented internally; bounded native tests on 10.11/11.4/11.8/12.3 below | Pending |
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

### W2.1 First MySQL/MariaDB Native Slice

[SqlDbAccess.Async](../../../../src/DataLinq.MySql/Shared/SqlDbAccess.Async.cs) binds W1's internal eager-command, captured-reader and borrowed-reader capabilities to MySqlConnector 2.6.2. Opening, scalar/non-query execution, reader acquisition/advancement and disposal use the concrete driver's async APIs. [SqlAsyncDataLinqDataReader](../../../../src/DataLinq.MySql/Shared/SqlAsyncDataLinqDataReader.cs) retains the existing scalar/UUID/binary decoding and owns its standalone connection explicitly. Synchronous execution remains direct; there are no new public async entry points.

Owned command cleanup composes with W1's existing coordinators. Borrowed commands stay caller-owned. Reader acquisition transfers connection ownership before completion observers run, so an observer failure cleans up an unreturned reader and its connection. Independent cleanup retains the primary exception and diagnostic context. Concrete command/parameter capability, nonempty SQL and attached-transaction rejection occur before cancellation and opening. Error evidence distinguishes initialization, actual dispatch, notification and cleanup without claiming standalone transaction recovery.

The [16 new TUnit methods](../../../../src/DataLinq.Tests.MySql/NativeAsyncCommandTests.cs) include two local validation cases and 14 cases parameterized across all six servers. They cover sync/async result parity, unchanged generic scalar casting, copied binary inputs/owned buffers, repeat enumeration, early sync/async disposal, one-connection pool reuse, canceled pool waits, canceled row advancement, failed authentication, provider identity, provider errors, logger and post-acquisition observer failure, native command timeout, live command cancellation and preservation of rejected caller-owned transactions. The live cancellation test observes its exact statement in the server process list while another connection holds a row lock before canceling it; it does not assume a timer proves dispatch.

Final working-source verification:

- **Release / .NET 10 provider-specific suite: 791/791 passed, zero failures/skips**, across MySQL 8.4/9.7 and MariaDB 10.11/11.4/11.8/12.3. The three batches pass 261, 265 and 265 cases. Independent TRX inspection finds all 90 new-case executions (30 per two-server batch, including the repeated local cases).
- Core/MySQL provider **Release builds pass on .NET 8/9/10 with zero warnings/errors**. The test project also builds cleanly on .NET 10.
- Relative file links in the five planning documents resolve; `git diff --check` passes. The Testing CLI state still lists all eight SQLite/server targets after targeted server execution.

The final local run is `20260923T222904705Z-b71c96f14a0c4117a611fa5411995f84`, recorded after midnight on 2026-09-24 in Europe/Madrid. It ran working changes based on kickoff `4f8550b0`, with unchanged checkout status during execution. The reused Testing CLI runner identifies `3bc97df2`; its provenance mismatch and dirty-source flags are retained. **ValidForEvidence remains false.** These are bounded developer checks, not a clean W2 candidate or full release-matrix receipt.

| Local summary under `artifacts/` | Result / scope | SHA-256 |
| --- | --- | --- |
| `w2-native-commands-focused.json` | Initial Debug probe: 13 passed / 8 failed; incorrect literal-cast/SLEEP test assumptions | `0a87dd4748a2b4eb1a19211b2b15bb9f2193e8f5538e3ecbd4c14fd05ca566c7` |
| `w2-native-commands-focused-release.json` | Release: 23 passed / 2 failed; SLEEP interruption assumptions | `ee84a00d9eb63e5d174d02a0499811fb8f93f5d4bd09e586ebc3927cac76440d` |
| `w2-native-commands-focused-release-locks.json` | Release: 25/25 passed after using observed lock contention | `b4f19ea28e4f1c9a876fc078ee9ceda3d211af3bdccb4a9f289b78e3544d5dbb` |
| `w2-native-commands-mysql-all.json` | Release: 788/788 passed before final foreign-parameter validation | `65ce508e758333a1909cbcef2a3d6bbfa3987f207006958b0c38b47d6a9e8de6` |
| `w2-native-commands-mysql-all-final.json` | Release: 791/791 passed with final parameter validation | `ecff84ad54911150dd441517e61f44a867a7ad58b97f9637c6f7873bfee39b99` |

The SLEEP experiments returned successful results on MySQL 9.7 where the initial test expected an exception; adding a second projection did not establish interruption failure either. Tests now use row-lock contention for an actual failing/canceled command. The implementation retains confirmed successful results rather than translating a late cancellation into failure. Literal scalar tests now preserve actual driver return types: DataLinq's existing generic cast is not silently widened to numeric conversion. An intermediate CLI build also failed with missing netstandard references after restore assets pointed outside the workspace cache; explicit sandboxed restore and builds succeeded. These failed probes/builds are retained, not counted as passes.

Reproduction after explicitly building the test project in Release:

```powershell
$env:DATALINQ_TEST_DB_HOST = '127.0.0.1'
.\scripts\dotnet-sandbox.ps1 build src/DataLinq.Tests.MySql/DataLinq.Tests.MySql.csproj -c Release -v minimal
.\scripts\dotnet-sandbox.ps1 exec src/DataLinq.Testing.CLI/bin/Debug/net10.0/DataLinq.Testing.CLI.dll run --suite mysql --alias all --configuration Release --no-build --output failures --summary-json artifacts/w2-native-commands-new-checkpoint.json
.\scripts\dotnet-sandbox.ps1 build src/DataLinq.MySql/DataLinq.MySql.csproj -c Release -v minimal
```

The direct CLI DLL invocation preserves the test configuration explicitly; the earlier `dotnet run` invocation's summary actually reported Debug. Rebuild the runner for a new clean-candidate receipt; do not relabel these working-source runs or overwrite retained evidence when collecting another checkpoint.

**Remaining:** SQLite native binding/adoption, managed transaction first-use and completion, full query/relation/mutation integration, administrative operations, hard-cancellation/connection-trust and combined-cleanup fault evidence, performance and final W2 acceptance. The next implementation slice is W2.2 native lazy transaction initialization. Passing this standalone slice does not establish transaction reuse after interruption or complete W2.1 for SQLite.
