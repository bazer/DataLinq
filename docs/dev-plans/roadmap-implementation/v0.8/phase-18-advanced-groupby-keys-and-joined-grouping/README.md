> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 18: Advanced GroupBy Keys and Joined Grouping

**Status:** Planned after grouped-row composition and HAVING.

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

## Scope

In scope:

- composite anonymous-object group keys
- SQL-renderable computed group keys using already-supported function values
- grouping over explicit joined row shapes
- grouping over supported implicit singular relation joins when the relation path is already SQL-backed
- projection binding for `group.Key.Member`
- enum, nullable, and string key behavior with provider-matrix tests
- provider-semantics tests for null grouping and collation-sensitive string grouping
- DTO and record-style grouped projections where constructor binding is explicit and AOT-safe

Out of scope:

- arbitrary client-computed keys
- relation collection grouping that hides row multiplication
- grouped element enumeration
- full `IGrouping<TKey,TElement>` materialization
- broad nested database subqueries inside group keys
- non-SQL backend grouping semantics
- implicit client fallback when a key cannot be rendered to SQL

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

Required tests:

- plan snapshots for composite keys, computed keys, and joined grouping
- SQL-shape tests proving every grouped key expression appears in `GROUP BY`
- behavior tests across SQLite, MySQL, and MariaDB
- null, enum, and string key grouping tests
- joined grouping tests from both `db.Query()` and `transaction.Query()`
- AOT/trimming-sensitive projection tests for supported DTO or record projections
- unsupported diagnostics for non-renderable computed keys, client methods, collection relation grouping, and grouped element enumeration

## Exit Criteria

Phase 18 is done when:

- composite and computed SQL-renderable keys work in grouped aggregate projections
- `group.Key.Member` projection is explicit and tested
- grouping over supported joined row shapes works without client fallback
- provider-specific null/string/enum grouping behavior is documented by tests
- unsupported advanced grouped shapes fail with DataLinq diagnostics
- public docs and the support matrix describe advanced grouped aggregate support without claiming provider-side `IGrouping` materialization
