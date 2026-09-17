> [!WARNING]
> Internal 0.10 execution work. This closes eager synchronous read reporting and owned cleanup gaps; it does not expose public async APIs, establish native provider feasibility or close W1/W0-F1.

# W1 Eager Read Failure Reporting

**Recorded:** 2026-09-17. Follows merged [helper lifetime PR #156](https://github.com/bazer/DataLinq/pull/156), under the accepted [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception). The [completion audit](W1%20Completion%20Audit.md) retains the remaining requirements.

## Failure Observation Before Ownership Release

An eager operation that outlives a callback must report its failure before releasing its execution lease. Otherwise the helper can finish draining without observing that failure. Point-key/cache reads, canonical single/batch/index row loaders, first-row/scalar queries, scalar and exact-key LINQ terminals, and eager relation references/collections now report at their outer owned read boundary. A private nested step leaves reporting to its outer owner. Failure during read-scope construction after admission is also reported before release.

The audit covers every current `BeginRead` site. Synchronous iterators and internal async enumerators retain their existing outer reporting/draining integration. The raw transaction query paths remain beneath that iterator boundary; this slice does not change raw `DatabaseAccess.ReadReader` compatibility or its existing aggregate cleanup behavior.

## Independent Owned Resource Cleanup

`ReadCommandResources` owns the command and reader in generated synchronous read paths. It attempts reader and command disposal independently, retains the original operation exception and stack, and attaches ordered secondary cleanup failures before releasing transaction ownership. With no operation failure, the first cleanup failure becomes primary. Repeated disposal does not repeat cleanup or replay the exception. The successful path uses a value-type owner and allocates no failure collector.

Source-row loading, first-row/cache queries, generated query enumeration and scalar commands use this owner. A materialization failure is distinguished from provider execution/row-loading failures without guessing native provider causes. Scalar success telemetry is set only after command cleanup succeeds.

These are conservative internal failure snapshots, not new synchronous transaction trust enforcement or a public diagnostics contract. Complete operation correlation, cancellation/native cause classification, arbitrary telemetry-listener failure composition and initialization/completion/mutation integration remain in the completion audit. Existing telemetry callbacks can still throw; this slice does not claim to preserve an earlier provider error through every telemetry callback.

## Development Evidence

Twenty-six new TUnit cases cover:

- Eleven eager query/source routes and three relation routes paused during provider execution while the helper closes admission. Drain waits, competing work is rejected, the original pending failure is imported once, and cleanup occurs before handoff.
- Nine read-plus-cleanup failure routes, preserving the original exception object/type/stack and disposing each owned resource once.
- Root and transaction cleanup-only failures, ordered diagnostics and idempotent disposal.
- Materialization failure with independent reader and command cleanup failures.

Local Release / .NET 10 results:

- `artifacts/w1-eager-read-transactions.json`: **235/235 passed**, transaction-focused tests.
- `artifacts/w1-eager-read-unit.json`: **2,064/2,064 passed**, full unit suite.
- `artifacts/w1-eager-read-sqlite-file.json` and `artifacts/w1-eager-read-sqlite-memory.json`: **519/519 passed each**, compliance anchor shards at maximum parallelism 8.
- `artifacts/w1-eager-read-build.log`, `artifacts/w1-eager-read-compliance-build.log` and `artifacts/w1-eager-read-core-build.log`: zero warnings/errors. Core targets .NET 8/9/10.

These modified-checkout results are development verification, not frozen release evidence. The new tests use scripted providers and a test-only worker to pause direct synchronous I/O; production introduces no `Task.Run` facade. Exact PR-head CI is recorded in the PR. Planning pages are excluded from DocFX; local links and whitespace are checked separately.

## Next Boundary

Complete internal initialization/completion handoff and managed finalization, then continue async mutation/hydration, immutable invocation capture and command ownership, relation/cache coordination, metadata/root orchestration and performance evidence. The official SQLite dependency, 0.9.2 compatibility baseline and .NET 10 benchmark target are unchanged. Native provider/public acceptance and release gates remain open.
