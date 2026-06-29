# 0.8 Phase 15 Review Findings: Relation-Aware and Implicit Joins

**Review date:** 2026-06-28.

**Reviewed scope:** Phase 15 docs, implicit singular relation parser and SQL rendering, support docs, and focused implicit relation join tests in the `v0.8` branch through `a978d85a`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** Resolved after the Phase 19-21 roadmap split clarified the deferred projection/query-syntax/joined-pushdown work. No runtime implicit-relation finding was identified in this pass.

## Findings

### P2: Phase 15 README overclaims the shipped join scope

The Phase 15 README status line says the phase is implemented for the implicit singular relation predicate/ordering slice, but the body still describes the broader relation-aware join plan as Phase 15 scope:

- `README.md:11` lists `JoinBy(...)`, `JoinMany(...)`, `on:` predicates, left joins, and standard `Queryable.LeftJoin(...)`.
- `README.md:94` repeats those broader items in the exit criteria.

The implementation plan is much more accurate. It explicitly scopes Phase 15 to implicit singular relation traversal in `Where(...)` and ordering, and puts `JoinBy(...)`, `JoinMany(...)`, `LeftJoinBy(...)`, `LeftJoinMany(...)`, standard `Queryable.LeftJoin(...)`, implicit relation projection, collection traversal, multi-hop traversal, and nullable left-join semantics out of scope (`Implementation Plan.md:35`).

Public docs also correctly list fluent relation-aware joins and standard `Queryable.LeftJoin(...)` as unsupported (`docs/Supported LINQ Queries.md:265`).

This matters because the README reads like a completed broad relation-aware/left-join phase unless the reader cross-checks the implementation plan. That is dangerous follow-up fuel.

Expected fix: rewrite the Phase 15 README purpose, scope, recommended order, and exit criteria around the shipped implicit singular predicate/ordering slice. Keep the broader relation-aware API list as deferred follow-up, not as completed Phase 15 scope.

Resolution note: the Phase 15 README now describes the shipped implicit singular predicate/ordering slice and routes implicit relation projection to Phase 19 while keeping fluent relation-aware APIs and left joins deferred.

## Review Notes

- The implemented implicit relation resolver is intentionally narrow and source-slot based.
- Repeated access to the same singular relation reuses one implicit join source.
- Unsupported implicit projection and collection traversal have focused diagnostics.
- Transaction-rooted implicit relation predicates/orderings have active provider-matrix coverage.

## Verification

Focused delegated verification passed across active provider batches (`sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`):

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesImplicitRelationJoinTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesRelationPredicateTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build
```
