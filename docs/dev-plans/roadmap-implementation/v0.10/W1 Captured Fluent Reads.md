> [!WARNING]
> Internal 0.10 fluent row-read orchestration with controllable providers. Native provider factories and public async APIs are not added. This is a bounded part of W1; W1 and W0-F1 remain open.

# W1 Captured Fluent Reads

**Recorded:** 2026-09-18. Follows merged [owned-command PR #160](https://github.com/bazer/DataLinq/pull/160), under the [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception). See the [completion audit](W1%20Completion%20Audit.md), [accepted API decisions](Async%20Public%20API%20Decisions.md) AAPI-17–21/58/86/87 and [W0 I/O map](W0%20IO%20Execution%20Map.md) S4.

## Captured Invocation

`Select<T>` now has internal raw-reader, row-sequence, first-row and buffered-row execution methods. They use the actual fluent SQL renderer/template path, `CapturedSql`, an explicit source-bound `IAsyncSqlReaderFactory`, and the existing managed async enumerator/owned-command pipeline. These are internal integration points, not public API declarations or native provider implementations.

Ordinary sequences capture when each enumerator is obtained. First-row and buffered terminals capture during the method call before suspension. Capture binds the selected source, rendered statement, parameter values, table and selected-column reader layout together. It creates no command or reader and performs no database I/O. Later builder, parameter-array or factory replacement cannot redirect that invocation. Another enumeration captures fresh state; neither the sequence nor this path permanently caches results. Concurrent builder mutation during capture remains unsupported.

`AsyncReaderInvocation<T>` keeps each source and materializer together. Materialization never consults the live builder after capture, and execution does not append projections to the caller's builder. The existing synchronous execution methods remain direct and unchanged.

`CapturedSql` owns private bindings and returns fresh bindings on each `ToSql` call. Arrays use the existing shallow-array-copy policy; arbitrary scalar objects are not deep-cloned. A native parameter must supply an independent `ICloneable`/`IDataParameter` clone to retain provider metadata. The tests prove MySqlParameter's clone behavior and explicitly reject the non-cloneable SqliteParameter. Built-in SQLite/MySQL generated SQL uses ordinary bindings, so this restriction does not affect that path. Native parameter capture without that contract needs an explicit adapter policy; it must not silently drop metadata. Borrowed caller commands retain AAPI-58's separate stability contract and are not cloned here.

## Rows and Lifetimes

The captured layout retains actual reader ordinals, including gaps from unmapped expressions. Such expressions do not create RowData slots or shift later mapped values. Duplicate selected columns retain the final selected value, partial rows preserve missing-column versus SQL-NULL presence, and model nullability checks still run during materialization. Mutable binary values are detached from provider buffers before cleanup or the next advancement.

Raw reader results remain borrowed current-cursor views. RowData results are independent rows. First-row execution closes the reader/command after its first row (or absence); buffered execution returns only the complete collection after cleanup. Cancellation, materialization failure or cleanup failure cannot produce a successful terminal result. Owned cleanup remains independent of the request token, and the managed transaction retains admission until cleanup settles.

The factory's returned source must retain the selected source/initialization policy and validate lifecycle/capability before pre-cancellation. The existing transaction enumerator supplies transaction validation, admission, combined tokens and failure publication. Native root lifecycle and initialization binding are not proved by a controllable factory.

## Development Evidence

Twenty-one new TUnit cases exercise the real fluent SQL/row path and captured SQL values. They cover per-enumerator SQL/materializer isolation, mutations before and after capture, selected-factory retention across suspension, delayed command cleanup, buffered failure after an earlier row, borrowed reader views, projection ordinals, binary ownership, empty first results, nullability/presence, cleanup failures, unsupported capability and completed-transaction precedence over cancellation.

Local Release / .NET 10:

- `artifacts/w1-captured-focused-final.json`: **23/23 passed**, method-name capture filter (includes existing capture tests).
- `artifacts/w1-captured-sql-final.json`: **4/4 passed**, SQL snapshot tests.
- `artifacts/w1-captured-reads-transactions.json`: **301/301 passed**, transaction filter at maximum parallelism 16.
- `artifacts/w1-captured-reads-unit.json`: **2,165/2,165 passed**, complete unit suite at CI parallelism 16.
- `artifacts/w1-captured-reads-sqlite-file.json` and `artifacts/w1-captured-reads-sqlite-memory.json`: **519/519 passed each**, compliance anchor shards at maximum parallelism 8. These are regression checks, not native async evidence.
- `artifacts/w1-captured-reads-build-final.log`, `w1-captured-reads-core-build.log`, and `w1-captured-reads-compliance-build.log`: successful unit/dependency, core .NET 8/9/10 and compliance builds, zero warnings/errors.

Earlier attempts are retained, not counted as passing evidence. The first two build logs report fixture API/property naming errors. The first focused run (`w1-captured-reads-focused.json`, **21 passed / 2 failed**) exposed a fixture counter increment inside a null-conditional callback; fixing it also corrected the first-row cleanup assertion. That test now awaits pending cleanup in its finally block even if an assertion fails. The first SQL run (`w1-captured-reads-sql.json`, **3 passed / 1 failed**) assumed SqliteParameter was cloneable; the final test records explicit rejection instead of claiming unsupported native capture.

Exact-head CI is recorded in the PR. These are development checks, not frozen release evidence. Planning pages are excluded from DocFX; relative links and whitespace are checked separately.

## Remaining Work

S4 is not closed: fluent model/cache execution, primary/foreign-key grouping, scalar terminals and raw model/string entry points still need captured execution. Expression and prepared-query invocation/backend integration, model/provider-key normalization, and all remaining mutation/relation/metadata/root/telemetry/performance work stay on the W1 audit. Native command/connection factories and parameter policies belong to W2; public surface and consumer evidence belong to W3. No SQLite limitation, compatibility baseline (0.9.2), or benchmark runtime (.NET 10) decision changes.
