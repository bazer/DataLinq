# Phase 3: Query and Runtime Hot Path Optimization

**Status:** In progress.

## Scope

This folder tracks the execution plan for the third roadmap phase described in [Roadmap.md](../../Roadmap.md).

The phase is about making the query/runtime hot path cheaper without turning the LINQ provider into a speculative rewrite:

1. reduce SQL generation allocations
2. move provider parameter object creation to the command execution boundary
3. separate reusable SQL shape from per-execution bindings where repeated query shapes prove worth caching
4. remove query/runtime object churn only when measurements say it matters

## Current Stance

The related SQL optimization notes are directionally right, but they are too broad to execute as-is.

Phase 3 should start with the concrete seams that exist today:

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

## Related Plans

- [`../../query-and-runtime/Sql Generation Optimization.md`](../../query-and-runtime/Sql%20Generation%20Optimization.md)
- [`../../Roadmap.md`](../../Roadmap.md)

Result-set caching is deliberately not listed as a Phase 3 related plan. It is a later semantic caching feature, not part of the SQL/query hot-path cleanup.
