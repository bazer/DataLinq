> [!WARNING]
> Internal 0.10 integration work. This is synchronous ownership groundwork, not public async support, complete W1 acceptance or W0-F1 closeout.

# W1 Query And Relation Ownership

**Recorded:** 2026-09-17. Continues W1.2/W1.4 after the [owned read integration](W1%20Owned%20Read%20Execution.md), merged in [PR #151](https://github.com/bazer/DataLinq/pull/151). The [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception) remains in force.

## Whole Synchronous Invocation Ownership

Protecting an individual reader was insufficient: an entity query can buffer keys, close its reader, hydrate models and then yield cached results. A deterministic warm-cache query reproduced the gap: transaction commit succeeded while its enumerator was still positioned on a row. The negative control is retained locally in `artifacts/w1-query-ownership-before.json` (one expected failure).

An outer transaction sequence now acquires admission on its first move and retains it through inner enumeration and cleanup, including time between buffered results. Private nested reads receive the existing ownership step explicitly. They neither acquire another lease nor release the enclosing one. Models, converters and callbacks retain the original ordinary transaction source; no ambient permission or privileged model source is introduced.

| Boundary | Implemented ownership |
| --- | --- |
| LINQ entity, SQL/direct/local/joined/grouped projections | First move through outer enumeration cleanup, including key buffering, hydration and local construction |
| Scalar and exact-primary-key terminals | SQL construction/cache lookup through result conversion, terminal semantics and cleanup |
| Fluent entity, key, grouped-key and row sequences | First move through outer cleanup; private nested loaders share the owner |
| Raw transaction query/command sequences | Outer iterator guarded before mutable iterator state is entered; supplied commands remain borrowed |
| Relation values and key/value enumeration | First move through snapshot enumeration and cleanup, including already cached snapshots |
| Materialized relation arrays/dictionaries and references | Synchronous access through lookup, construction and publication; ownership is released when the value returns |

Independent transaction reads, commit, rollback and caller disposal fail while an enumerator owns the transaction. Materialize or dispose the active sequence before starting another transaction operation, including relation navigation. An array or dictionary already returned to the caller does not itself keep the transaction occupied. Database-root reads do not acquire transaction admission.

## Enumeration And Source Capture

DataLinq's outer transaction enumerators reject overlapping or reentrant moves, current-value reads and disposal before touching iterator state. A rejected call cannot dispose the first call's reader, clear its current position or release its transaction slot. The per-call guard uses an atomic transition; no lock is held across provider or application code. Arbitrary third-party wrappers around these sequences are not made thread-safe.

Transaction-bound query and relation enumerator construction is cold. Relation enumeration previously materialized during `GetEnumerator`; it now captures its source then and loads on the first move. This is an intentional 0.10 behavior change. A captured transaction enumerator cannot silently switch to committed reads after transaction completion. Fresh relation access after confirmed completion retains the existing committed-source fallback. Relation invalidation/publication rules and transaction-local model identity remain intact.

Prepared-query cancellation is checked even while yielding buffered rows. Empty results, cancellation, provider failure, normal exhaustion and early disposal release admission. A concurrent second enumerator fails admission without disturbing the first. Ordinary expression binding and prepared argument capture retain their existing boundaries; complete immutable invocation snapshots across awaits remain pending.

## Development Evidence

Eleven new unit cases cover warm-cache ownership, reentrant LINQ/fluent/raw moves and disposal, buffered prepared-query cancellation, cold/repeatable construction and captured-source completion, cross-thread overlap, local projection callbacks, empty results, pre-cancellation and provider failure. Three new provider-compliance cases cover query shapes, warm/cold relations and transaction-local identity with pending inserts. Stable-identity fixtures stop background cache maintenance locally; existing invalidation/maintenance race tests remain active.

Local Release / .NET 10 checks (CI is recorded separately in the PR):

- `artifacts/w1-query-ownership-focused.json`: **130/130 passed**, transaction-focused tests.
- `artifacts/w1-query-ownership-unit.json`: **1,919/1,919 passed**, full unit suite.
- `artifacts/w1-query-ownership-sqlite-file.json` and `artifacts/w1-query-ownership-sqlite-memory.json`: **519/519 passed each**, compliance anchor shards at maximum parallelism 8.
- `artifacts/w1-query-ownership-build.log`, `artifacts/w1-query-ownership-compliance-build.log` and `artifacts/w1-query-ownership-core-build.log`: zero-warning/error builds; core targets .NET 8/9/10.

These are development checks from a modified checkout, not frozen release evidence. The full suite also required updating a reflection-based private-loader call for its explicit owner argument and scoping the projection test callback to its execution context, without adding a process-wide test scheduling exemption.

## Remaining Work

The next ownership slice is async reader/enumerator orchestration with controllable providers: acquisition and reader transfer across suspension, combined method/enumerator tokens, overlapping async-call rejection and cleanup on every exit. Native lazy initialization, helper callback admission/draining and full invocation capture remain open. Direct low-level `DatabaseAccess` still bypasses this managed orchestration.

W1.3 must establish interrupted-read versus write recovery, connection trust, completion certainty and primary/secondary failure composition. This slice preserves existing synchronous cleanup-failure precedence. Releasing a gate after cleanup failure does not prove a provider connection is safe to reuse.

Added scopes, service bundles, iterators and admission checks still require measurement against W0; no performance acceptance is claimed. Public declarations, provider dependencies and pooling settings are unchanged. The 0.9.2 compatibility baseline and .NET 10 benchmark target remain fixed. Passing SQLite development checks do not close W0-F1: adoption of a corrected official dependency and affected evidence are still required before SQLite acceptance, final provider feasibility/public API freeze and release approval.
