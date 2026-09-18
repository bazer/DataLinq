> [!WARNING]
> Internal W1 execution evidence. Public async declarations, packed consumers and release acceptance remain later gates.

# W1 Async Memory Execution

**Date:** 2026-09-18. This connects the existing Memory backend to the [async query-plan boundary](W1%20Async%20Query%20Plan%20Execution.md), covering internal execution under AAPI-74–78 and the capture/cancellation rules in AAPI-17–21. The [full W1 audit](W1%20Completion%20Audit.md) remains open.

## Query execution

`MemoryQueryPlanBackend` now explicitly implements `IAsyncQueryPlanBackend`. It retains the existing capability profile and uses its existing local row-selection, ordering, scalar conversion and materialization code. Supported entity and direct scalar sequences, `Any`/`Count`, `Single`/default and ordered `First`/default retain their original constraints. Unsupported joins, local/SQL-row projections, unordered/paged shapes and numeric/Last reductions remain rejected where the synchronous backend rejects them. No rejected expression is compiled into a LINQ-to-Objects fallback.

The shared expression adapter captures ordinary invocation values at each `GetAsyncEnumerator()`. Memory's async enumerator validates the captured request with the combined method/enumerator token but does not compile a local execution plan or visit the store until the first move. Typed model-valued bindings are captured at enumeration creation and normalized once when the local plan compiles, preserving the existing Memory boundary; arbitrary user objects are not deep-cloned. New enumerations have independent positions and capture current ordinary-query arguments.

Local moves, disposal, scalar/row terminals and lookup may complete immediately. There is no `Task.Run`, artificial yield, blocking wait or database-I/O fallback. The same effective token reaches predicate compilation, scanning, sorting, scalar conversion and model materialization. Validation of source/backend/shape precedes pre-cancellation; pre-canceled empty and warm-cache executions do not visit the store. A caller can suspend between moves without requiring Memory itself to yield.

The shared call gate rejects overlapping/reentrant moves, `Current` access and disposal before they change the active enumerator. Completion, failure and disposal clear its local cursor/current value and release any linked token source. Unused enumerators stay cold. Cancellation does not undo rows already delivered. Materializing terminals return no successful partial collection, although complete, individually valid materialized models may remain cached.

## Lookup and errors

Internal `FindAsyncCore<TModel>` and synchronous `Find<TModel>` share the same lookup implementation. The async path supplies cancellation; the synchronous path remains a direct local call with no awaiting or blocking. Preserve exactly one primary-key column, non-null model-side input, normalization once, nullable absence and redacted `MemoryLookupException` diagnostics. The async addition does not accept composite keys or a canonical/provider-key overload.

Key validation and normalization precede cancellation, which is checked before store probing and around materialization, including warm identity-cache hits. A converter can run to completion after requesting cancellation; the following checkpoint rejects the operation. A valid complete row published by that local materialization can remain cached. A failed conversion does not publish a broken model and does not prevent a later successful lookup.

Immediate failure results preserve exception identity. Cancellation thrown by user conversion code remains cancellation even if its token was not requested. `MemoryAsyncResult` uses an already-completed faulted await on the cancellation path so the async method builder can retain both the original exception and canceled status; ordinary successful execution does not take that path or schedule work. Fatal converter failures remain the original failures, not absence or a redacted ordinary-conversion error.

## Scope and verification

This does not add public methods or interfaces, Memory prepared-query execution, navigation, mutation, transactions, SQL provider ownership, `SeedAsync` or new query shapes. The existing frozen public-surface check still expects only `Find`, `Query` and `Seed`. The synchronous capability-count and Memory smoke dependency-isolation checks remain applicable regression evidence; they are not new packed async-consumer or Native AOT acceptance evidence.

The `MemoryAsyncExecutionTests` cases cover capture/rebinding, cold and immediately completed moves, entity/scalar ordered paging, short-circuit reductions, row cardinality/defaults, supported/rejected parity, validation before cancellation, empty/warm paths, both/same tokens, suspended consumers and independent enumerators, typed conversion, partial-materialization failure, reentrant call rejection, lookup identity/normalization/redaction/recovery, and original cancellation/fatal exception identity. New converter observations are test-local and do not introduce global scheduling locks.

The first focused run passed **47/48**: one assertion incorrectly expected Memory's typed-binding conversion during enumerator construction instead of the first move. The captured model value was already stable. After correcting that assertion and expanding coverage, **61/61 passed** (`artifacts/w1-memory-focused.json`). The initial result is retained as `w1-memory-focused-initial.json`. Review also replaced direct canceled-result construction, which would lose the converter's original cancellation exception; the final cases verify both exception identity and canceled status.

Broad Release / .NET 10 local verification:

- **210/210 Memory tests passed**, maximum parallelism 16, `artifacts/w1-memory-full.json`.
- **2,362/2,362 unit tests passed**, maximum parallelism 16, `artifacts/w1-memory-unit.json`.
- **527/527 compliance tests passed for each SQLite anchor**, maximum parallelism 8, `artifacts/w1-memory-sqlite-file.json` and `w1-memory-sqlite-memory.json`.
- Memory/core .NET 8/9/10 and Memory-test, unit/dependency and compliance Release builds passed with zero warnings/errors. Logs share the `w1-memory` prefix.

The **3,626** broad local cases are complete successful bounded runs. Their stricter summary `ValidForEvidence` flag remains false because canonical full-matrix release evidence also requires the complete provider scope and clean runner/checkout identity. Planning links and whitespace are checked before integration; no normal documentation, navigation or website presentation changed. The PR records exact-head CI and merge evidence. These tests do not satisfy public/native/performance release gates.

## Remaining work

Query telemetry/correlation, managed non-query/mutation/hydration, async relation coordination and full relation/index publication, metadata/provisioning and owning-root disposal remain W1 work. Final I/O-path coverage and comparable .NET 10 coordination/allocation evidence are still required. Public query/lookup declarations, provider-free packed async consumers and source-boundary compatibility remain W3/later gates. The 0.9.2 compatibility baseline and SQLite W0-F1 limitation are unchanged.
