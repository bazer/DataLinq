# Phase 3: Query and Runtime Hot Path Optimization

**Status:** Planning.

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

## Related Plans

- [`../../query-and-runtime/Sql Generation Optimization.md`](../../query-and-runtime/Sql%20Generation%20Optimization.md)
- [`../../query-and-runtime/Result set caching.md`](../../query-and-runtime/Result%20set%20caching.md)
- [`../../Roadmap.md`](../../Roadmap.md)
