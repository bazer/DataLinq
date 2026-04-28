# Phase 3: Query and Runtime Hot Path Optimization

**Status:** Implemented.

## Scope

This folder tracks the execution plan for the third roadmap phase described in [Roadmap.md](../../Roadmap.md).

The phase is about making the query/runtime hot path cheaper without turning the LINQ provider into a speculative rewrite:

1. reduce SQL generation allocations
2. move provider parameter object creation to the command execution boundary
3. separate reusable SQL shape from per-execution bindings where repeated query shapes prove worth caching
4. remove query/runtime object churn only when measurements say it matters

## Starting Stance

The related SQL optimization notes are directionally right, but they are too broad to execute as-is.

Phase 3 started with the concrete seams that existed at the time:

- `Sql` stores text and concrete `IDataParameter` instances
- SQLite and MySQL providers create `SqliteParameter`/`MySqlParameter` during SQL rendering
- `QueryExecutor`, `QueryBuilder`, and the visitors rebuild query objects for each LINQ execution
- the benchmark harness has Phase 2 watchpoints, but not yet a dedicated Phase 3 query-generation lane

## Documents

- `Implementation Plan.md`

## Progress

- 2026-04-27: Started Workstream A by adding the `phase3-query-hotpath` benchmark lane, covering repeated non-primary-key equality fetches, repeated `IN` predicate fetches, and repeated scalar `Any` queries.
- 2026-04-27: Started Workstream B by moving generated SQL parameters to provider-neutral `SqlParameterBinding` values and materializing provider parameters in SQLite/MySQL command creation.
- 2026-04-28: Started Workstream C by replacing LINQ/string-join SQL rendering in selected-column, ordering, insert, and where-parameter paths with direct append loops.
- 2026-04-28: Continued Workstream C by removing additional formatting and fragment-string allocation in provider parameter rendering, `ORDER BY`, `WHERE` operand rendering, and `What(...)` selector handling.
- 2026-04-28: Started Workstream D with a bounded SQL template cache for narrow single-value equality SELECT shapes.
- 2026-04-28: Expanded the template cache to narrow AND-connected equality SELECT shapes so repeated scalar and multi-predicate queries can reuse SQL text without reusing parameter values.
- 2026-04-28: Expanded template keys to include predicate operator and parameter count, allowing fixed-slot `IN` predicates to reuse SQL text safely.
- 2026-04-28: Closed Workstream E by replacing `QueryExecutor` result-operator display-string matching with typed Remotion result-operator checks.
- 2026-04-28: Closed Phase 3. The benchmark lane shows repeatable allocation reductions on the measured query hot paths, while timing remains too noisy to use as the headline result.

## Outcome

Phase 3 landed the intended narrow optimization path:

- the benchmark harness now has a dedicated `phase3-query-hotpath` lane and baseline artifact
- generated SQL carries provider-neutral parameter bindings until provider command creation
- SQLite and MySQL materialize provider parameters at the command boundary
- SQL rendering avoids several LINQ/string-formatting allocation paths
- repeated equality and fixed-slot `IN` SELECT shapes can reuse bounded SQL templates
- `QueryExecutor` no longer allocates display strings to classify common result operators

The useful performance claim is allocation reduction on the measured hot paths. The local timing results stayed noisy enough that they should be treated as watchpoint data, not proof of a runtime win.

No extra SQL-generation counters or timing spans were added. The benchmark lane and existing query telemetry were enough for this phase, and adding per-query timing around SQL generation would risk measuring the observer instead of the path.

## Related Plans

- [`../../query-and-runtime/Sql Generation Optimization.md`](../../query-and-runtime/Sql%20Generation%20Optimization.md)
- [`../../Roadmap.md`](../../Roadmap.md)

Result-set caching is deliberately not listed as a Phase 3 related plan. It is a later semantic caching feature, not part of the SQL/query hot-path cleanup.
