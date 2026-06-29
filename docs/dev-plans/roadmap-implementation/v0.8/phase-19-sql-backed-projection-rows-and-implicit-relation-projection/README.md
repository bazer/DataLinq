> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 19: SQL-Backed Projection Rows and Implicit Relation Projection

**Status:** Implemented for direct source-slot projection rows and supported implicit singular relation member projection.

## Purpose

Phase 19 turns direct projection rows into an explicit SQL-backed result shape instead of forcing every non-entity projection through post-materialization row-local evaluation.

The immediate user-facing win is simple implicit singular relation projection:

```csharp
var rows = db.Query().DepartmentEmployees
    .Select(row => new
    {
        row.emp_no,
        DepartmentName = row.departments.Name
    })
    .ToList();
```

The important rule is that this must not become lazy relation loading hidden inside `Select(...)`. If `row.departments.Name` is accepted in provider projection, it must bind to a planned implicit join source and read the projected value from the SQL result row. A projection feature that quietly performs N+1 relation loads would be worse than a clean rejection.

## Scope

In scope:

- direct SQL-backed scalar projection values from root source slots
- direct SQL-backed anonymous projection members from root source slots
- direct SQL-backed projection values from already planned implicit singular relation source slots
- scalar `Select(row => row.SingularRelation.Member)` for supported singular relations
- anonymous `new { ... }` projections made only of bindable source-slot values
- stable SQL aliases and reader-based projection-row materialization
- read-only and transaction-root parity
- focused diagnostics for relation objects, collection relations, nested database projections, and row-local client expressions

Out of scope:

- broad SQL `SELECT` expression translation for arbitrary computed projections
- row-local computed projections such as string concatenation; those should remain post-materialization unless a later phase deliberately SQL-backs them
- relation object projection or eager loading disguised as projection
- collection relation projection or hidden row multiplication
- multi-hop implicit relation traversal
- nullable left-join projection semantics
- nested database subqueries inside provider `Select(...)`
- client-side fallback when a projection member cannot bind to SQL

## Design Requirements

Projection needs a real row-result boundary:

- the query plan should distinguish SQL-backed projection rows from row-local projection expressions
- projected members should carry names, CLR result types, and `QueryPlanValue` bindings
- relation member projection should reuse the implicit singular relation source-slot machinery from Phase 15
- SQL aliases should be stable enough for snapshot tests and data-reader materialization
- execution should construct the projected row from `IDataLinqDataReader`, not from materialized relation properties
- existing row-local projection behavior should remain available only for the documented post-materialization projection surface

The parser should be conservative. If a projection member would require relation loading, client method invocation, nested query execution, or collection expansion, reject it with `QueryTranslationException`.

## Verification

Implemented tests:

- plan snapshots for SQL-backed scalar and anonymous projection rows
- plan snapshots proving singular relation projection records an implicit join source
- SQL-shape tests proving selected aliases come from the expected root or relation source slots
- behavior tests comparing projected rows with in-memory LINQ across SQLite, MySQL, and MariaDB
- transaction-root parity for SQL-backed projection rows
- unsupported diagnostics for relation object projection, collection projection, nested database projection, and row-local client expressions
- docs/support-matrix updates only for the tested direct projection shapes

Focused verification passed:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesProjectionTranslationTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesImplicitRelationJoinTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build`

## Exit Criteria

Phase 19 is done when:

- direct source-slot projection rows materialize from SQL result rows
- supported implicit singular relation projections are SQL-backed and do not lazy-load relations
- unsupported projection shapes keep focused `QueryTranslationException` diagnostics
- supported projection rows work from both `db.Query()` and `transaction.Query()`
- existing row-local projection support remains documented as post-materialization behavior
- public docs and the support matrix describe only the tested SQL-backed projection row shapes
