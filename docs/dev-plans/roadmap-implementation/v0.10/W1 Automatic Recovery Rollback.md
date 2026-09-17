> [!WARNING]
> Internal 0.10 orchestration, exercised with controllable resources. No production provider or public helper invokes this runner yet. This does not complete W1.3, freeze public options or close W0-F1.

# W1 Automatic Recovery Rollback

**Recorded:** 2026-09-17. Follows [read failure and recovery](W1%20Read%20Failure%20and%20Recovery.md), merged in [PR #154](https://github.com/bazer/DataLinq/pull/154), under the accepted [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception).

This slice implements the owned automatic rollback/cleanup boundary from AAPI-25, AAPI-63 and AAPI-97. It consumes the earlier execution gate and ordered failure collector. Helper callback admission/draining, native resource adapters and provider/host configuration wiring remain separate work.

## Captured Budget And Ownership

`RecoveryRollbackSettings` captures a getter-only duration. Its default is 30 seconds; accepted values are 1 through 4,294,967,294 milliseconds inclusive. Validation compares the exact duration, rejecting negative/infinite, zero, positive sub-millisecond and oversized values. The argument error identifies `RecoveryRollbackTimeout`. This is an internal snapshot, not the public `DataLinqExecutionOptions` declaration or its provider/host capture implementation.

`AutomaticTransactionRecovery` takes ownership by transferring an existing **idle** operation lease. Gate identity and the absence of an active step are validated before transfer. A pending provider operation must settle and release its step before recovery can be constructed; this runner does not drain it or close callback admission itself. The original lease cannot release the transferred owner. Recovery holds its own private step through rollback, both disposal attempts and failure-context publication.

The timer starts immediately before the one permitted rollback call, not during earlier work or construction. There is no request-token argument or linkage. Explicit caller-directed rollback remains a separate policy and is not implemented by this runner. A supported rollback flag is required; already committed/rolled-back or non-transactional completion skips rollback even if a stale flag permits it. A prior unknown completion stays unknown after recovery.

Expiry requests cancellation through the actual `CancellationTokenSource` timer. It does not stop awaiting the provider, free admission, begin concurrent disposal or retry. If a provider ignores cancellation and eventually confirms rollback, the result is still confirmed rollback unless earlier completion was already uncertain. Disposing a timer is not proof that provider work has stopped. This remains a cooperative rollback budget, not a total cleanup deadline.

## Independent Cleanup And Failure Reporting

The internal `IAsyncTransactionRecovery` bundle separates rollback, transaction disposal and connection disposal. A successful rollback return must mean confirmed database rollback; a thrown rollback has no confirmation at this boundary. Adapters must keep later observer/finalization failures outside that confirmation boundary. They must also guarantee that each disposal step is safe independently, await actual work and avoid another explicit rollback attempt. Native drivers may themselves perform rollback as part of resource disposal; this contract does not establish their behavior or feasibility.

After rollback settles, the runner awaits transaction disposal and then connection disposal without passing an expired request/recovery token. A failure in one safe step does not skip the other. No synchronous fallback, detached task, reconnect or replay is introduced. Timer setup failure skips rollback but still attempts disposal; it alone does not imply an attempted database completion.

The supplied `ExecutionFailures` belongs exclusively to this boundary once handed over. Original failure identity/type/stack and earlier secondary failures survive. Recovery and disposal failures are appended in encounter order, retaining aggregates without flattening. Without an earlier primary, the first recovery/cleanup failure becomes primary. An exception is classified as recovery cancellation only when it carries the canceled recovery token; token expiry alone cannot relabel a different provider failure. Other native cause classification remains pending.

The final immutable context has no caller recovery actions because the owned resources have been disposed/retired. Confirmed completion survives cleanup failure; connection disposal alone does not manufacture rollback confirmation. The original exception receives the final helper-boundary context before ownership is released. Previously returned snapshot objects remain unchanged. Concurrent disposal fails fast; later disposal does not retry resources or replay recorded failures. This runner is one terminal boundary, not a general cleanup retry mechanism.

The runner is not yet connected to public `Transaction.DisposeAsync`, mutation helpers, callbacks or lazy native resource initialization. It does not change synchronous transaction behavior. Preserving arbitrary exceptions from application `await using`/`await foreach` bodies remains subject to the accepted language-scope limitation.

## Development Evidence

The new TUnit cases exercise exact duration boundaries using the real runtime timer constructor, independent captured budgets, rejection before resource work, active-step/foreign-gate rejection, idle ownership transfer, canceled-request independence, cooperative and ignored expiry, blocked competing calls throughout both paused disposal steps, synchronous/asynchronous cleanup failures, primary stacks and ordered immutable snapshots, one-attempt behavior, timer setup failure and confirmed/unknown completion.

Local Release verification:

- `artifacts/w1-automatic-recovery-focused.json`: **27/27 passed**, new recovery tests on .NET 10.
- `artifacts/w1-automatic-recovery-unit.json`: **2,014/2,014 passed**, full unit suite on .NET 10.
- `artifacts/w1-automatic-recovery-build.log`: unit/dependency build, zero warnings/errors.
- `artifacts/w1-automatic-recovery-core-build.log`: core .NET 8/9/10 builds, zero warnings/errors. Timer boundary execution above is .NET 10 evidence, not a runtime test on all three targets.
- All **76 local links** in the five changed planning pages resolve; `git diff --check` is clean. These pages are excluded from DocFX.

CI is recorded separately against the PR head. These controllable-provider checks are development evidence, not native provider feasibility, performance acceptance or frozen release evidence. The tests manually fire the timer callback and pause provider stages; they do not use elapsed-time sleeps to prove races. No existing production dispatch path changes, so targeted local SQLite compliance shards were not repeated in this slice; CI still supplies its normal provider lanes.

## Next Slice And Remaining Gates

Next, implement the internal helper lifecycle that hands off to this runner: invoke/await the callback once, reject borrowed transaction completion, close admission when the callback ends, drain unfinished admitted work without committing, then transfer idle ownership to recovery. Preserve callback/operation/cleanup failure precedence throughout. The runner's active-step rejection is a prerequisite, not proof that helper draining is implemented.

Native rollback/commit confirmation and classification, partial initialization, async mutation/hydration finalization, provider/host settings and constructor compatibility, public diagnostics, complete invocation capture, relation publication and coordination/allocation cost acceptance remain open. The 30-second default still needs provider feasibility evidence. No package, pooling, public declaration, benchmark target or compatibility-baseline change is included.

W0-F1 still requires corrected official SQLite dependency adoption and affected evidence. The published 0.9.2 compatibility baseline and .NET 10 benchmark target remain fixed; native SQLite acceptance, public API freeze, release approval and publication are not claimed.
