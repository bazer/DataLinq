> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 18: Advanced GroupBy Keys and Joined Grouping

**Status:** Implemented for named SQL-renderable keys and supported joined grouping.

## Purpose

Phase 18 broadens the grouping source and key model once direct-key aggregate rows are boring.

This is where `GroupBy(...)` becomes useful for more realistic reporting:

```csharp
var departmentHiring = db.Query().DepartmentEmployees
    .GroupBy(row => new
    {
        row.dept_no,
        FromYear = row.from_date.Year
    })
    .Select(group => new
    {
        group.Key.dept_no,
        group.Key.FromYear,
        Count = group.Count()
    })
    .ToList();
```

It also covers grouping over joined row shapes after Phase 14 and Phase 15 have made joined source-slot binding stable.

Implemented joined grouping is still narrow: explicit joined projections and implicit singular relation traversal must bind to source-slot values. Row-local joined projection members and collection relations stay rejected.

## Scope

In scope:

- composite anonymous-object group keys
- SQL-renderable computed group keys using already-supported function values
- grouping over explicit joined row shapes
- grouping over supported implicit singular relation joins when the relation path is already SQL-backed
- projection binding for `group.Key.Member`
- enum, nullable numeric, and string-function key behavior with provider-matrix tests
- DTO and record-style grouped projections where constructor binding is explicit

Out of scope:

- arbitrary client-computed keys
- relation collection grouping that hides row multiplication
- grouped element enumeration
- full `IGrouping<TKey,TElement>` materialization
- broad nested database subqueries inside group keys
- non-SQL backend grouping semantics
- implicit client fallback when a key cannot be rendered to SQL
- whole composite `group.Key` object projection; project `group.Key.Member` values instead

## Design Requirements

Composite and computed keys need first-class plan structure:

- key members should have names, SQL values, and CLR types
- `group.Key.Member` must bind back to the planned key member, not to expression text
- computed keys should reuse existing SQL-renderable function values and reject anything else
- nullable and enum keys must use provider-normalized values consistently

Joined grouping needs source-slot discipline:

- aggregate selectors must know which source slot they read from
- group keys over joined projections should bind to source-slot values, not anonymous projection reflection
- grouping over implicit singular relations must not trigger relation loading or hidden N+1 behavior
- collection relations should remain explicit through supported join or `JoinMany(...)` shapes

## Verification

Implemented tests:

- plan snapshots for composite keys, computed keys, and joined grouping
- SQL-shape tests proving every grouped key expression appears in `GROUP BY`
- behavior tests across SQLite, MySQL, and MariaDB
- enum, nullable numeric, and string-function key grouping tests
- joined grouping tests from both `db.Query()` and `transaction.Query()`
- constructor-backed DTO or record projection tests
- unsupported diagnostics for non-renderable computed keys, client methods, collection relation grouping, and grouped element enumeration

Focused verification passed:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesImplicitRelationJoinTests/*" --output failures --build`

## Exit Criteria

Phase 18 is done when:

- composite and computed SQL-renderable keys work in grouped aggregate projections
- `group.Key.Member` projection is explicit and tested
- grouping over supported joined row shapes works without client fallback
- provider-specific enum, nullable numeric, and string-function grouping behavior is documented by tests
- unsupported advanced grouped shapes fail with DataLinq diagnostics
- public docs and the support matrix describe advanced grouped aggregate support without claiming provider-side `IGrouping` materialization
