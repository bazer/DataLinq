# 0.8 Phase 14 Review Findings: Source-Slot Join Composition

**Review date:** 2026-06-28.

**Reviewed scope:** Phase 14 docs, explicit join parser binding, joined SQL rendering, joined projection execution, support docs, and focused join compliance tests in the `v0.8` branch through `a978d85a`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** Resolved after the Phase 19-21 roadmap split clarified the deferred projection/query-syntax/joined-pushdown work. No runtime join-composition finding was identified in this pass.

## Findings

### P2: Phase 14 README claims joined Phase 13 pushdown semantics that are intentionally not shipped

`README.md:31` says Phase 14 preserves Phase 13 operator-order semantics when filtering, ordering, paging, or result operators are applied over joined row shapes. `README.md:53` repeats this as an exit criterion, explicitly including subquery pushdown.

That overstates the shipped slice. The implementation plan is more accurate: post-paging joined query pushdown is out of scope (`Implementation Plan.md:52`). The parser rejects `Join(...).Take(...).Where(...)`-style composition (`src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:1653`), and `EmployeesJoinTranslationTests.cs:308` asserts that rejection.

The actual implemented Phase 14 slice is useful and coherent: flat `Where`, ordering, paging, `Any`, and `Count` over explicit two-source joined projections whose projected members bind back to source-slot values. It does not include derived-source pushdown over joined rows after paging.

Expected fix: update the Phase 14 README scope and exit criteria to match the implementation plan and tests. Say flat filtering/ordering/paging and `Any`/`Count` over joined projections are supported, while post-paging joined pushdown remains deferred.

Resolution note: the Phase 14 README now describes the shipped explicit two-source join composition slice and routes query-syntax joins plus joined post-paging pushdown to later 0.8 phases.

## Review Notes

- Joined projection member binding is source-slot aware and rejects row-local client-expression members for provider-side filtering/order.
- Joined materialization buffers joined primary-key rows before cache hydration, avoiding nested reads while the joined key reader is open.
- Transaction-rooted composed explicit joins have active provider-matrix coverage.
- Public docs are more accurate than the phase README: they list post-paging joined composition and broader join APIs as unsupported.

## Verification

Focused delegated verification passed across active provider batches (`sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`):

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build
```
