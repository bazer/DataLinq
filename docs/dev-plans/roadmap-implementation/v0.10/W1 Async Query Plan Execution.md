> [!WARNING]
> Internal W1 evidence for SQL query-plan orchestration, not public async APIs or native-provider acceptance. Memory internal execution is covered by the subsequent [Memory slice](W1%20Async%20Memory%20Execution.md).

# W1 Async Query Plan Execution

**Date:** 2026-09-18. This extends [async model queries](W1%20Async%20Model%20Query%20Batches.md) and [lookups/raw models](W1%20Async%20Lookups%20and%20Raw%20Models.md) into expression and prepared-query execution. It covers internal SQL execution under AAPI-17–21 and the accepted query semantics in AAPI-42–48. The [full W1 audit](W1%20Completion%20Audit.md) remains open; the subsequent [Memory slice](W1%20Async%20Memory%20Execution.md) covers AAPI-74–78 internal backend integration.

## Capture and backend boundary

Ordinary sequences parse and bind at each `GetAsyncEnumerator()` call. Prepared sequences bind at `ExecuteAsyncCore(...)`, before returning the sequence. Repeated enumeration of that prepared invocation reuses its captured arguments but creates fresh execution resources and observes rows at execution time. Neither sequence construction nor enumerator construction performs database I/O. Materializing terminals capture and start during the call, before the first suspension. Supported mutable arguments use existing binding snapshots; this does not deep-clone arbitrary objects or redefine local closure semantics.

The expression-facing adapter passes only `ValidatedQueryExecutionRequest` into the execution layer. Post-parse executor/backend contracts do not accept expression trees. Async preparation checks source ownership, backend binding and supported plan features, then requires the explicit `IAsyncQueryPlanBackend` capability. SQL lifecycle validation and native command capability checks precede cancellation, including warm-cache paths. Missing async capability never falls back to a synchronous database method. Existing synchronous preparation keeps its previous cancellation order and direct execution paths.

Prepared execution retains its current source constraints. This change does not allow a Memory source to execute a SQL prepared query or broaden either backend's supported translations.

## SQL execution and lifetime

Entity plans reuse captured fluent model loading, including its key-first hydration, provider-sensitive matching and transaction-local identity. Scalar-member, SQL-row and grouped-aggregate projections read native async rows directly. Column-aware decoding and model conversion follow the existing projection rules; mutable provider values are owned before a user converter can retain them. SQL-row aliases resolve against actual reader columns, while grouped keys retain the existing projection-slot ordinal rule.

Single-source local projections hydrate models before evaluating the existing normalized recipe. Joined local projections buffer canonical key tuples, close the join reader, then hydrate distinct source/key pairs through the same captured factory and execution owner. Duplicate tuples retain result order while reusing hydrated models. Provider-equivalent returned key spelling is allowed; cache identity remains the actual returned canonical key. This is per-key hydration, not a new batching optimization. A missing joined model, provider failure, cancellation or constructor failure cannot return a successful partial collection. Individually valid row-cache entries may remain.

`AsyncReaderTransform` composes local projections and terminal cardinality inside the original reader's ownership and failure boundary. `First`, `Single`, `Last` and default variants preserve existing SQL result shaping and LINQ result semantics; cardinality is evaluated after owned reader/command cleanup. Scalars reuse existing numeric, nullable/empty and overflow conversion rules, with conversion inside ownership after command cleanup. A list succeeds only after enumeration and cleanup both finish.

Both method and enumerator cancellation tokens apply. Buffered rows retain transaction admission between moves even when their native readers have already closed. Unused enumerators remain cold; a captured enumerator cannot execute after its transaction completes. Initialization failure stays terminal, and helper-owned escaped readers drain before helper completion. Catching a terminal failure permits completion only when settled provider evidence actually allows continued transaction use; unknown evidence cannot be upgraded by catching the exception.

## Verification

The `AsyncQueryPlan_*` tests use the actual expression parser, prepared binding and SQL renderer with controllable async adapters. They cover distinct capture boundaries and repeated execution; validation-before-cancellation without sync fallback; transaction ownership through cleanup, projection construction and cardinality; list failure without partial success; both tokens; scalar conversions; mapped/converted/binary projections; joined hydration, factory stability, duplicate tuples and failure evidence; warm transaction-local identity; terminal initialization failure; unused readers; and helper draining/completion restrictions.

- Focused Release / .NET 10: **52/52 passed**, `artifacts/w1-query-plans-focused.json`.
- Full unit suite: **2,362/2,362 passed**, maximum parallelism 16, `artifacts/w1-query-plans-unit.json`.
- Full existing Memory suite: **149/149 passed**, maximum parallelism 16, `artifacts/w1-query-plans-memory.json`. This is regression evidence, not proof of new async Memory execution.
- SQLite file and in-memory compliance anchors: **527/527 passed each**, maximum parallelism 8, `artifacts/w1-query-plans-sqlite-file.json` and `w1-query-plans-sqlite-memory.json`.
- Core .NET 8/9/10 and unit/dependency, compliance and Memory Release builds passed with zero warnings/errors. Logs use the `w1-query-plans` prefix.

These are complete, successful bounded local runs. Their summary `ValidForEvidence` flag is false: that stricter release gate requires a clean checkout/runner and the canonical full provider matrix, not these selected suites. No full-matrix release acceptance is claimed.

The initial focused run passed 14/27: the controllable SQL fixture lacked comparison rendering and the scalar test had not enabled its separate scalar adapter. After fixing those fixtures and extending coverage, 40/40 passed. The next run passed 51/52: its new warm-cache test incorrectly assumed a committed-cache row warmed a transaction cache. The corrected test explicitly loads a transaction-local row before asserting identity and zero subsequent dispatch; no production cache-isolation change was needed. Those earlier runs are retained in `w1-query-plans-focused-initial.json`, `w1-query-plans-focused-second.json` and `w1-query-plans-focused-warm-fixture.json`. Test compilation also corrected a nonexistent joined-model property and a callback parameter named `_` that was mistakenly used as a discard.

Changed planning-document links and whitespace are checked before integration. No normal documentation, navigation or site presentation changed. The PR records exact-head CI and merge evidence. Controllable adapters do not prove native SQL adapter feasibility or performance.

## Remaining work

[Memory async ordinary-query and narrow lookup orchestration](W1%20Async%20Memory%20Execution.md) now covers its existing capability subset and immediate cooperative execution rules. Query telemetry/correlation also remains open. The wider W1 requirements for non-query/mutation/hydration, relation coordination and complete index/reference/collection publication, metadata/provisioning, owning-root disposal, full I/O-map audit and .NET 10 allocation/coordination comparison are unchanged. Native providers remain W2, public declarations and consumers W3; the 0.9.2 compatibility baseline and SQLite W0-F1 limitation are unchanged.
