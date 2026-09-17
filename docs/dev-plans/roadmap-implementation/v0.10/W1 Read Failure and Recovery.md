> [!WARNING]
> Internal 0.10 integration, verified with controllable/scripted providers. This does not complete W1.3, wire native async providers, freeze public diagnostics or close W0-F1.

# W1 Read Failure And Recovery

**Recorded:** 2026-09-17. Follows [async reader enumeration](W1%20Async%20Reader%20Enumeration.md), merged in [PR #153](https://github.com/bazer/DataLinq/pull/153), under the accepted [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception).

The previous enumerator released admission after cleanup but deliberately made no transaction-trust decision. This slice connects the internal async reader boundary to conservative recovery restrictions. It implements a bounded part of AAPI-23 through AAPI-26 and the immutable-context decisions; it does not claim all mutation, initialization, completion or helper recovery paths are integrated.

## Evidence Before Reuse

An optional internal `IAsyncReadFailureEvidence` capability classifies settled work without I/O. Its evidence separates the failure cause, operation effects, transaction integrity and whether rollback remains valid. It must use actual provider evidence: neither a canceled token nor an open connection proves transaction integrity. The borrowed-command source forwards this capability when present and otherwise supplies unknown evidence. Returning rows does not establish that a raw command was read-only.

The enumerator awaits reader cleanup, obtains evidence and publishes restrictions **before** releasing its transaction lease. Reentrant classification callbacks still face ordinary admission. If classification itself fails, its exception is retained after earlier cleanup failures and recovery becomes disposal-only.

| Evidence after an admitted failure | Permitted recovery in this slice |
| --- | --- |
| Ordinary read or no application statement, confirmed transaction integrity, successful cleanup | Continue; rollback only when explicitly supported; disposal |
| Unknown read integrity or unknown/potential write effects, successful cleanup | Rollback only when explicitly supported; disposal; no continued execution or commit |
| Initialization failure, lost integrity, reader cleanup failure or failed assessment | Disposal only |
| No evidence capability | Disposal only; no guessed reuse or rollback |

These restrictions are consulted by managed reads, mutation preflight, commit, rollback and terminal relation-source validation. An invalid subsequent call does not overwrite the original transaction failure snapshot or dispatch another command. Valid pre-execution cancellation still leaves the transaction unchanged. No automatic rollback, reconnect, replay or retry is introduced. Disposal remains necessary even when rollback is not known to be valid.

The existing synchronous path has no recovery record until the internal async enumerator reports an admitted failure. Native synchronous statement classification and existing mutation poisoning are not replaced. In particular, the potential-write evidence case blocks continued execution, but full async mutation/hydration invalidation and consistency-critical local finalization still require integration.

## Failure Identity And Snapshots

`ExecutionFailures` retains the first exception with its original stack, records later failures in encounter order and continues safe independent cleanup. Reader cleanup and evidence assessment failures remain distinct. Aggregates are retained as the original exception objects, not flattened; the primary exception is not duplicated in the secondary list. Cleanup-only failures remain reportable once, without replay during later enumerator disposal.

Internal `ExecutionFailureContext` snapshots have getter-only fields and defensive read-only secondary collections. They separate cause, stage, completion and recovery, with transaction ID correlation. An internal weak-key association supports direct lookup on the supplied exception without `Exception.Data` keys or automatic traversal of inner/aggregate exceptions. The structure adds no live transaction/connection/command or SQL/parameter payload; retaining original exceptions does not sanitize their own contents.

Provider-classified timeout/error remains that failure even when the token is also canceled. Cancellation observed at DataLinq's own checkpoints is classified as cancellation. Native adapters must still establish how their actual cancellation exceptions, timeouts and provider codes map to this evidence.

When a transaction already has this async failure record, later managed synchronous commit/rollback/disposal can publish a newer recovery snapshot. The original exception's reported snapshot stays unchanged. Existing conservative completion rules remain authoritative: confirmed completion survives later cleanup/notification failure; unknown commit remains unknown after a later rollback; disposal alone does not establish rollback. This is not a replacement for the pending native dispatch/confirmation split or typed reporting of every existing synchronous completion failure.

The eventual public `DataLinqFailure` accessor, `Transaction.FailureContext`, complete operation/provider correlation fields, public enum mappings and consumer/ApiCompat evidence remain W3 work. These internal names and enum values do not freeze that surface. General validation/overlap diagnostics and implicit-helper reporting remain incomplete.

## Development Evidence

The 36 new cases cover evidence/recovery combinations, immutable ordered secondary failures and stacks, direct context lookup, managed read/write/commit/rollback rejection, classification while admission is held, preserved prior transaction work after a trusted canceled read, confirmed/uncertain completion after recovery, and permitted/rejected terminal read-source fallback. New execution cases use only controllable readers and scripted transactions.

Local Release / .NET 10 verification:

- `artifacts/w1-read-recovery-policy.json`: **18/18 passed**, policy and failure-record tests.
- `artifacts/w1-read-recovery-transactions.json`: **158/158 passed**, transaction-focused tests including eighteen new recovery cases.
- `artifacts/w1-read-recovery-unit.json`: **1,987/1,987 passed**, full unit suite.
- `artifacts/w1-read-recovery-sqlite-file.json` and `artifacts/w1-read-recovery-sqlite-memory.json`: **519/519 passed each**, compliance anchor shards at maximum parallelism 8.
- `artifacts/w1-read-recovery-build.log`, `artifacts/w1-read-recovery-core-build.log` and `artifacts/w1-read-recovery-compliance-build.log`: zero-warning/error builds; core targets .NET 8/9/10.
- All 73 local links in the changed planning pages resolve; `git diff --check` is clean. These pages are excluded from DocFX.

PR CI is recorded separately against its head commit. These are modified-checkout development checks, not frozen release evidence or native-provider acceptance. The broader unit suite includes existing provider fixtures; the new cases themselves use doubles.

## Remaining Work

The next W1.3 slice is automatic recovery orchestration with the independent, captured rollback budget: begin the budget at rollback, attempt once, retain ownership until actual provider completion, and continue safe independent cleanup without losing the primary failure. Explicit caller rollback and recovery rollback must keep distinct token policies. The accepted 30-second default still needs provider feasibility evidence before API freeze.

Native classification and initialization, the provider completion-confirmation split, async mutation/hydration finalization, helper callback admission/draining, owned generated commands, complete invocation capture, relation publication and full diagnostic reporting remain open. Coordination/allocation costs still require W0 comparison. No package dependency, pooling policy, benchmark target or public declaration changes; the 0.9.2 compatibility baseline and .NET 10 benchmark target remain fixed.

W0-F1 still requires adoption of a corrected official SQLite dependency and affected evidence. Passing development checks does not close SQLite acceptance, final provider feasibility/public API freeze, release approval or publication.
