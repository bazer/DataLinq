> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 15 Implementation Plan: Implicit Singular Relation Joins

**Status:** In progress.

## Goal

Land the first relation-aware join slice without pretending the full fluent join API exists.

The implementable 0.8 slice is SQL-backed implicit traversal of generated singular relations in root-row predicates and ordering:

```csharp
var rows = db.Query().DepartmentEmployees
    .Where(row => row.departments.Name.StartsWith("S"))
    .OrderBy(row => row.departments.Name)
    .ThenBy(row => row.emp_no)
    .ToList();
```

The relation property supplies the join metadata. DataLinq should render an inner join to the related table, bind the related member access to the joined source alias, and still return root `DepartmentEmployees` rows.

## Scope

In scope:

- singular generated relation traversal from a root source in `Where(...)`
- singular generated relation traversal from a root source in `OrderBy(...)` and `ThenBy(...)`
- repeated traversal of the same relation reuses one implicit join source slot
- read-only and transaction-root parity
- focused diagnostics for unsupported collection relation traversal and implicit relation projection
- docs and support matrix updates for the shipped implicit singular predicate/ordering slice

Out of scope for this slice:

- `JoinBy(...)`
- `JoinMany(...)`
- join-local `on:` predicates
- `LeftJoinBy(...)` and `LeftJoinMany(...)`
- standard `Queryable.LeftJoin(...)`
- implicit relation projection in `Select(...)`
- implicit collection traversal beyond the existing documented `Any(...)` and existence-equivalent `Count(...)` predicates
- multi-hop implicit traversal
- nullable left-join semantics

Those features need API and nullability design. Shipping them as a side effect of member-access translation would be dishonest.

## Workstreams

### 1. Relation Resolver

- Detect `root.SingularRelation.Member` member access during value conversion.
- Resolve the relation through existing `RelationProperty` metadata.
- Require the relation side to be singular from the source row to the related row.
- Register or reuse an implicit join source slot for the relation.
- Add a join operation using the relation key columns.

### 2. Parser And SQL

- Allow root-row operators to continue after implicit join operations.
- Keep explicit joined-row composition rules from Phase 14 intact.
- Allow SQL rendering to treat implicit join sources like explicit inner join sources.
- Keep implicit relation traversal out of row-local projection until a projection design exists.

### 3. Tests

- Add provider behavior tests comparing implicit relation filters/orderings with in-memory relation traversal.
- Add transaction-root parity tests.
- Add SQL-shape tests showing `JOIN`, related alias use, and no lazy relation projection.
- Add unsupported diagnostics for implicit relation projection and collection traversal.

### 4. Documentation

- Update `Supported LINQ Queries`, the support matrix, and internals docs only for implicit singular relation predicates/orderings.
- Document `JoinBy`, `JoinMany`, left joins, and standard `Queryable.LeftJoin` as not shipped in this slice.

## Verification

Focused checks:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesImplicitRelationJoinTests/*|/*/*/QueryPlanSnapshotTests/*|/*/*/QueryPlanSqlParityTests/*|/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

## Exit Criteria

- [ ] Singular relation predicates are SQL-backed through implicit joins.
- [ ] Singular relation ordering is SQL-backed through implicit joins.
- [ ] Repeated access to the same relation reuses one implicit join source.
- [ ] Supported implicit relation traversal works from both `db.Query()` and `transaction.Query()`.
- [ ] Unsupported implicit projection and collection traversal fail with focused diagnostics.
- [ ] Docs and support matrix describe only the shipped implicit singular relation slice.
