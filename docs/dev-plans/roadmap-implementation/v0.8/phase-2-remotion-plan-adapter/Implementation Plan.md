> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 2 Implementation Plan: Remotion Plan Adapter

**Status:** Complete.

**Created:** 2026-06-27.

**Completed:** 2026-06-27.

## Purpose

Phase 2 introduces the DataLinq-owned query plan while Remotion still parses expressions.

This is the strangler step that makes the rest of 0.8 possible. Remotion should become one temporary producer of DataLinq plan nodes, not the semantic model that SQL generation, projection, diagnostics, local sequence handling, joins, and constrained-platform support depend on.

The hard requirement is not just "add some records named plan." The plan must be expressive enough to carry the Phase 1 query contract and strict enough to expose unsupported shapes before Phase 3 moves SQL generation behind it.

## Phase 1 Inputs

Phase 1 closed with a query contract audit and focused regression coverage. Phase 2 must consume that handoff directly:

- [Phase 1 Query Contract Audit](../phase-1-query-contract-and-plan-baseline/Query%20Contract%20Audit.md)
- [Phase 1 Implementation Plan](../phase-1-query-contract-and-plan-baseline/Implementation%20Plan.md)
- `src/DataLinq.Tests.Compliance/Translation/CurrentQueryTranslationInspection.cs`

Plan snapshots should start with the exact handoff shapes:

- chained `Where(a).Where(b)`
- `Where(a).OrderBy(...).Where(b)`
- empty fixed conditions rendering `1=0` and `1=1`
- local equality-membership `Any(predicate)` rendering `IN (...)`
- nullable inequality against non-null values
- relation `Any(...)` and negated `Any(...)` rendering correlated `EXISTS`
- existence-equivalent relation `Count()` comparisons
- scalar aggregates over direct numeric and nullable numeric members
- post-materialization projection over filtered, ordered, and paged rows
- the current narrow explicit inner join with direct member keys
- explicit rejection of post-paging filters/orderings until subquery pushdown exists

## Non-Goals

- no DataLinq expression parser
- no replacement of `Queryable<T>` or `IQueryProvider`
- no removal of `Remotion.Linq`
- no SQL generation migration
- no behavior expansion beyond the Phase 1 support contract
- no non-SQL executor
- no public query-plan API

Phase 2 can add internal infrastructure and tests. It should not change the public query behavior except for narrowly justified diagnostics needed to make plan conversion failures explicit.

## Proposed Code Layout

Prefer an internal planning namespace under the existing query/LINQ boundary:

```text
src/DataLinq/Linq/Planning/
  DataLinqQueryPlan.cs
  QueryPlanBindingFrame.cs
  QueryPlanSourceSlot.cs
  QueryPlanOperation.cs
  QueryPlanPredicate.cs
  QueryPlanValue.cs
  QueryPlanProjection.cs
  QueryPlanResult.cs
  QueryPlanDebugWriter.cs
  RemotionQueryPlanAdapter.cs
```

Rationale:

- `DataLinq.Query` already means SQL query building today.
- `DataLinq.Linq` is where the phase-start Remotion parser boundary lived.
- Keeping the first plan internal avoids accidentally freezing public APIs before Phase 3 proves the model.

The plan node files must not reference Remotion namespaces. Only `RemotionQueryPlanAdapter` and clearly marked migration helpers may import Remotion.

## Core Design

### Query Plan Root

Introduce an immutable root model shaped roughly as:

```text
DataLinqQueryPlan
  Sources: QueryPlanSourceSlot[]
  Operations: QueryPlanOperation[]
  Projection: QueryPlanProjection
  Result: QueryPlanResult
  Bindings: QueryPlanBindingFrame
```

`Operations` should remain ordered. A flat "final where/order/limit" object is tempting and wrong. Phase 1 explicitly called out operator-order risk around paging and later operators. Even while post-paging operators remain unsupported, the plan model should not paint Phase 3 into a corner.

### Source Slots

Source slots should be explicit:

```text
QueryPlanSourceSlot
  Id
  Alias
  Table
  ElementType
  Cardinality
  IsNullable
```

Minimum source kinds:

- root table source
- explicit join source
- relation subquery source

The first implementation can keep `Cardinality` and `IsNullable` simple, but they should exist because the later source-slot join follow-up and future left joins will need them.

### Operations

Represent operation order directly:

```text
Where(predicate)
OrderBy(orderings)
Skip(value)
Take(value)
Join(join)
```

Phase 2 does not need to implement subquery pushdown, but it must preserve enough structure that Phase 3 can choose either:

- reject unsupported post-paging operators with the current diagnostic, or
- later introduce nested plan/subquery nodes without redesigning the whole model.

### Predicate Nodes

Minimum predicate nodes:

- `And`
- `Or`
- `Not`
- `Compare`
- `In`
- `Exists`
- `FixedTrue`
- `FixedFalse`

The adapter must preserve Phase 1 truth-table behavior for empty local collections. Do not model empty `Contains(...)` as an `In` with zero values and expect SQL generation to guess. It should be `FixedFalse` or `FixedTrue` after negation.

### Value Nodes

Minimum value nodes:

- `Column(sourceSlot, column)`
- `Constant(value)`
- `CapturedValue(bindingId, type)`
- `LocalSequence(bindingId, elementType, count)`
- `Function(functionKind, args)`
- `Converted(value, targetType)`

Captured values must be separate from shape. For example:

```csharp
var id = 1001;
db.Query().Employees.Where(x => x.emp_no == id)
```

Plan shape should contain `CapturedValue(0, Int32)` or equivalent, while the binding frame contains `0 -> 1001`.

Local sequences are trickier. SQL template shape often depends on local sequence cardinality. It is acceptable for the shape node to include sequence count, but not the actual sequence values.

### Projection Nodes

Minimum projection nodes:

- entity/source projection
- scalar member projection
- anonymous projection
- computed row-local expression projection
- joined row-local projection

Phase 2 may keep expression payloads inside projection nodes for current post-materialization behavior. The important distinction is that projection nodes must say what kind of projection is happening and which source slots it binds to. Phase 5 will decide how much of that can be interpreted or generated for AOT-clean execution.

### Result Nodes

Minimum result nodes:

- sequence
- `Single`
- `SingleOrDefault`
- `First`
- `FirstOrDefault`
- `Last`
- `LastOrDefault`
- `Count`
- `Any`
- aggregate: `Sum`, `Min`, `Max`, `Average`

Do not store Remotion result-operator type names. Store DataLinq result semantics.

### Joins

Represent the current narrow explicit join baseline:

- inner join only
- two direct DataLinq query sources
- direct member equality keys
- nullable `.Value` key normalization
- row-local result projection

Unsupported join shapes should fail during adapter conversion with DataLinq-owned diagnostics:

- `GroupJoin`
- composite keys
- post-join filters/orderings/paging/result operators
- relation-property projection in join selector

## Workstream A: Plan Node Skeleton

Goal: introduce the internal plan model with no production behavior change.

Tasks:

1. Create the `DataLinq.Linq.Planning` namespace.
2. Add immutable plan/root/source/operation/predicate/value/projection/result records.
3. Keep constructors strict:
   - no null source slots
   - no comparison without left/right values
   - no `In` without a value node and sequence binding
   - no join without two source slots and key columns
4. Add enums for stable concepts:
   - predicate comparison operator
   - ordering direction
   - result kind
   - aggregate kind
   - scalar/function kind
5. Add XML comments only where they clarify migration-specific design choices.
6. Add unit tests for basic construction invariants if the records contain validation logic.

Exit criteria:

- plan nodes compile without Remotion imports
- plan nodes can represent the Phase 1 handoff shapes at a structural level

## Workstream B: Binding Frame

Goal: separate query shape from runtime values.

Tasks:

1. Add `QueryPlanBindingFrame` and binding records.
2. Support scalar captured values.
3. Support local sequence values with element type and count metadata.
4. Decide how to represent constants that are truly part of shape versus captured runtime values.
5. Add tests proving:
   - two queries with different scalar values produce the same normalized shape
   - binding frames carry different values
   - local sequence shape records cardinality but not element values

Design stance:

- Literal constants inside query expressions can initially be captured as bindings too. This is slightly less clever and much safer.
- Shape caching can get smarter later. Phase 2 should not overfit before SQL generation consumes the plan.

Exit criteria:

- normalized snapshots do not include actual captured scalar or sequence values
- binding tests prove values are still available for later execution

## Workstream C: Debug Writer and Snapshot Format

Goal: make plan shape reviewable and stable before SQL generation moves.

Tasks:

1. Add `QueryPlanDebugWriter` or equivalent.
2. Produce deterministic, text-based snapshots.
3. Include:
   - source slots and aliases
   - ordered operations
   - predicate tree
   - value-node kinds
   - projection kind
   - result kind
   - binding ids, types, and sequence cardinality
4. Exclude:
   - actual captured values
   - provider object instance identities
   - reflection-only object identity strings
   - Remotion class names
5. Add snapshot helper tests under compliance or unit tests.

Recommended format:

```text
sources:
  s0 t0 employees Employee
operations:
  where and(compare(column(s0.emp_no) == bind(p0:Int32)), fixed(true))
  order column(s0.emp_no) asc
  take bind(p1:Int32)
projection:
  entity(s0)
result:
  sequence
bindings:
  p0 Int32
  p1 Int32
```

This is intentionally not JSON unless the repo already has a snapshot convention that prefers JSON. Plain deterministic text is easier to review in failing tests.

Exit criteria:

- representative plan snapshots are readable in test failure output
- snapshots remain stable across providers unless provider metadata genuinely changes the plan

## Workstream D: Remotion Adapter - Single Source Core

Goal: convert simple Remotion `QueryModel` instances into DataLinq plans.

Tasks:

1. Add `RemotionQueryPlanAdapter`.
2. Convert main-from clause into root source slot.
3. Convert `WhereClause` into predicate operations for:
   - scalar equality and inequality
   - range comparisons
   - property-to-property comparisons
   - boolean grouping
   - `Not`
4. Convert `OrderByClause` into ordering operations.
5. Convert `Skip` and `Take` result operators into ordered operations.
6. Convert entity projection and scalar member projection.
7. Convert sequence/single/first/last/count/any result semantics.
8. Preserve current rejection for post-paging filters/orderings.

Exit criteria:

- snapshots exist for:
  - chained `Where(a).Where(b)`
  - `Where(a).OrderBy(...).Where(b)`
  - ordered `Skip(...).Take(...)`
  - `Count`, `Any`, `First`, and `SingleOrDefault`

## Workstream E: Local Collections and Fixed Conditions

Goal: represent current local collection semantics in plan nodes.

Tasks:

1. Convert local `Contains(...)` to `In` or fixed predicates.
2. Convert equality-shaped local `Any(predicate)` to membership nodes.
3. Convert projected local `Contains(...)` where Phase 1 supports it.
4. Convert empty local sequence cases to `FixedTrue` or `FixedFalse`.
5. Preserve unsupported diagnostics for compound non-empty local `Any(predicate)`.
6. Add snapshots for:
   - local array/list/set `Contains`
   - projected local `Contains`
   - empty fixed false
   - negated empty fixed true
   - local `Any(predicate)` membership

Exit criteria:

- no empty local sequence plan is represented as an invalid zero-item `In`
- snapshots show binding ids and sequence cardinality without element values

## Workstream F: Nullable, Function, and Aggregate Shapes

Goal: carry the trickiest single-source semantics into the plan.

Tasks:

1. Represent nullable equality and inequality explicitly enough to preserve C# lifted semantics.
2. Represent `.HasValue`, `!HasValue`, and guarded `.Value`.
3. Represent supported string/date/time member methods through function/value nodes.
4. Represent scalar aggregate result nodes and aggregate selector value nodes.
5. Preserve unsupported aggregate selector diagnostics.

Snapshots should include:

- nullable inequality against non-null value
- guarded nullable `.Value`
- string `StartsWith`, `Contains`, `Trim().Length`
- date/time member extraction
- `Sum`, `Min`, `Max`, `Average` over direct and nullable numeric selectors

Exit criteria:

- snapshots make nullable semantics visible rather than hiding them as raw expression strings
- aggregate snapshots use DataLinq aggregate names, not Remotion result-operator names

## Workstream G: Projection Shapes

Goal: record projection intent without changing post-materialization execution.

Tasks:

1. Represent entity projection.
2. Represent scalar member projection.
3. Represent anonymous projection with source-slot bindings.
4. Represent computed row-local projection as a bounded client projection node.
5. Preserve rejection for relation-property projection and nested database subquery projection.
6. Add snapshots for:
   - scalar projection
   - anonymous projection
   - computed anonymous projection
   - projection after filtering, ordering, and paging

Exit criteria:

- projection nodes say "post-materialization" or equivalent for computed/client expressions
- projection nodes identify source slots they read from
- unsupported projection shapes still fail with current diagnostics or better DataLinq-owned diagnostics

## Workstream H: Relation Predicate Shapes

Goal: represent current relation-existence support without SQL-specific hacks.

Tasks:

1. Convert relation `Any()` to `Exists`.
2. Convert relation `Any(predicate)` to `Exists` with relation predicate.
3. Convert negated relation `Any(predicate)` to `Not(Exists(...))`.
4. Convert existence-equivalent relation `Count()` comparisons to `Exists` or `Not(Exists(...))`.
5. Represent parent/child source slots and relation key mapping.
6. Preserve unsupported diagnostics for:
   - relation traversal from related row
   - unsupported `Count()` thresholds
   - relation projection
7. Add snapshots for:
   - relation `Any()`
   - relation `Any(child => child.Column == value)`
   - negated relation `Any`
   - `Count() == 0`
   - `Count() > 0`

Exit criteria:

- relation plans do not embed SQL text
- relation plans expose relation metadata and correlated source slots

## Workstream I: Explicit Join Baseline

Goal: represent the current narrow explicit inner join support.

Tasks:

1. Convert one explicit inner `Join(...)`.
2. Add source slots for root and joined table.
3. Represent direct member equality keys as column comparisons or join-key nodes.
4. Normalize nullable `.Value` keys.
5. Represent joined row-local projection over both source slots.
6. Preserve unsupported diagnostics for:
   - `GroupJoin`
   - composite keys
   - post-join filters/orderings/paging/result operators
   - relation-property projection in join result selector
7. Add snapshots for:
   - current narrow join success
   - nullable `.Value` join key
   - unsupported composite-key join failure

Exit criteria:

- current documented join baseline has a plan shape
- unsupported join shapes do not silently convert to partial plans

## Workstream J: Architecture Guardrails

Goal: prevent the plan layer from accidentally becoming Remotion-shaped or SQL-shaped.

Tasks:

1. Add an architecture/unit test that plan node types do not expose Remotion types in public/internal properties, fields, constructor parameters, or base types.
2. Add a focused test that plan debug snapshots do not contain:
   - `QueryModel`
   - `WhereClause`
   - `OrderByClause`
   - `ResultOperator`
   - `QuerySourceReferenceExpression`
3. Keep SQL-specific types out of plan nodes where possible:
   - no `SqlQuery`
   - no `WhereGroup`
   - no SQL text
4. Allow metadata types such as `TableDefinition`, `ColumnDefinition`, and relation metadata because those are DataLinq-owned semantics.

Exit criteria:

- only `RemotionQueryPlanAdapter` imports Remotion inside the new planning folder
- snapshot output is DataLinq-owned vocabulary

## Workstream K: Test Placement

Recommended tests:

- `src/DataLinq.Tests.Unit/Linq/QueryPlanNodeTests.cs`
  - constructor validation
  - debug writer formatting for hand-built tiny plans
  - guardrails against Remotion leakage
- `src/DataLinq.Tests.Compliance/Translation/QueryPlanSnapshotTests.cs`
  - Remotion-backed adapter snapshots using real generated employee metadata
  - provider-neutral shape assertions where possible
- `src/DataLinq.Tests.Compliance/Translation/QueryPlanAdapterUnsupportedShapeTests.cs`
  - unsupported post-paging operators
  - unsupported joins
  - unsupported projection/relation shapes

Use inline expected snapshots at first. File-based snapshots are not worth the maintenance cost until the plan stabilizes.

## Suggested Task Order

1. Add plan node skeleton and debug writer for hand-built plans.
2. Add guardrail tests proving plan nodes do not reference Remotion.
3. Add binding frame and shape/value separation tests.
4. Add `RemotionQueryPlanAdapter` for root source, entity projection, and sequence result.
5. Add single-source `Where`, `OrderBy`, `Skip`, and `Take` conversion.
6. Add predicate boolean/comparison conversion.
7. Add local collection/fixed-condition conversion.
8. Add result operator and aggregate conversion.
9. Add projection conversion.
10. Add relation `Exists` conversion.
11. Add explicit join baseline conversion.
12. Add unsupported-shape adapter tests.
13. Update Phase 2 README and closeout notes.

This order makes each slice reviewable. Do not start with relation predicates or joins; those are the correctness stress tests, not the foundation.

## Verification Plan

Focused unit tests:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/QueryPlanNodeTests/*" --output failures --build
```

Focused compliance snapshot tests:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanAdapterUnsupportedShapeTests/*" --output failures --build
```

Regression filters from Phase 1 handoff:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesQueryBehaviorTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesContainsTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesRelationPredicateTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build
```

Phase closeout:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite mysql --alias latest --output failures
docfx docfx.json
```

If server-backed sandbox connectivity fails, follow the repo guidance: use `DATALINQ_TEST_DB_HOST=127.0.0.1` for server-backed Testing CLI commands in the native Windows sandbox, and only escalate after a likely sandbox/network/cache failure.

## Risks

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Plan model mirrors Remotion too closely | High | Guardrail tests, DataLinq-owned node names, no Remotion types in plan nodes. |
| Plan model becomes SQL-shaped too early | High | Keep SQL text and `SqlQuery`/`WhereGroup` out of plan nodes. Represent intent, not rendering. |
| Captured values leak into shape snapshots | Medium | Debug writer excludes values; binding tests compare same shape with different values. |
| Local sequence cardinality is under-modeled | Medium | Include sequence count in shape metadata, but not values. |
| Relation predicates force SQL-specific design | High | Model relation metadata and `Exists`; defer SQL rendering to Phase 3. |
| Joins expand beyond current support | Medium | Convert only the documented narrow join baseline and preserve unsupported diagnostics. |
| Phase 2 starts changing execution behavior | High | Keep adapter unused by production SQL path until Phase 3. Tests inspect plans, not production routing. |

## Exit Criteria

Phase 2 is complete when:

- internal DataLinq query plan nodes exist and compile without Remotion imports
- `RemotionQueryPlanAdapter` converts the Phase 1 handoff shapes into plans
- normalized plan snapshots exist for representative supported queries
- captured scalar and local sequence values are separated from normalized shape
- relation-existence predicates and the current narrow explicit join baseline have plan snapshots
- unsupported post-paging operators, unsupported joins, unsupported relation/projection shapes, and unsupported aggregate selectors fail during adapter conversion with focused diagnostics
- architecture/guardrail tests prevent Remotion types from leaking into plan node contracts
- old production execution still runs through the existing Remotion-to-SQL path
- focused unit and compliance plan tests pass
- compliance quick, unit quick, MySQL/MariaDB verification, and docs build pass

The handoff to Phase 3 should identify exactly which plan nodes SQL generation must consume first and which Remotion-backed visitor paths can be retired slice by slice.
