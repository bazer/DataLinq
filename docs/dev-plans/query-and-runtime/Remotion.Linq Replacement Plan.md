> [!WARNING]
> This document is roadmap and engineering planning material. It is not normative product documentation and should not be treated as a shipped support claim.
# Remotion.Linq Replacement Plan

**Status:** Draft follow-up plan after Phase 8.

**Created:** 2026-05-05.

## Purpose

Phase 8 proved that generated SQLite models can run under Native AOT, trimmed publish, and Blazor WebAssembly AOT. It also made the remaining query-pipeline problem impossible to politely ignore: `Remotion.Linq` still sits in the runtime package and still emits Native AOT and trimming warnings.

The Phase 8 compatibility report is blunt about the boundary:

- generated SQLite Native AOT publish runs, but `Remotion.Linq` emits `IL3053` and `IL2104`
- trimmed publish runs, but `Remotion.Linq` emits `IL2104`
- Blazor WebAssembly AOT runs, but the support claim is still narrow and payload-sensitive
- hot-path `Expression.Compile()` was removed from the checked LINQ and instance paths, leaving Remotion as a warning-producing dependency boundary

See [Compatibility Results](../roadmap-implementation/phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md) and [Practical AOT and Size Plan](../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md).

This plan describes how DataLinq should replace `Remotion.Linq` without detonating the query support gained in Phases 6 and 7.

## Opinionated Summary

Replacing Remotion is the right direction. Suppressing the warnings is not a product strategy unless the dependency is deeply audited and the exact warning paths are proven unreachable. That would still leave DataLinq's query semantics shaped by a third-party query model that was not designed for generated metadata, AOT, query-plan caching, or non-SQL backends.

The correct move is not "write a full LINQ provider." That is how projects wander into a swamp wearing nice shoes.

The correct move is:

1. define a DataLinq-owned query plan
2. translate today's Remotion `QueryModel` into that plan
3. move SQL generation, projection, diagnostics, and plan caching behind that plan
4. build a supported-subset expression parser that emits the same plan
5. dual-run Remotion and the new parser until the supported matrix matches
6. remove or isolate Remotion from the practical AOT support boundary

This is a strangler migration. It is slower than a rewrite on day one and much less likely to embarrass us on day ninety.

## Current Dependency Shape

The dependency is not isolated to `Queryable<T>`.

Current Remotion touch points include:

- `src/DataLinq/Linq/Queryable.cs`
  Creates the default Remotion parser through `QueryParser.CreateDefault()` and inherits `QueryableBase<T>`.
- `src/DataLinq/Linq/QueryExecutor.cs`
  Implements `IQueryExecutor`, consumes `QueryModel`, handles Remotion body clauses, result operators, joins, selectors, and query-source mapping.
- `src/DataLinq/Linq/Visitors/WhereVisitor.cs`
  Handles Remotion `WhereClause`, `SubQueryExpression`, `QuerySourceReferenceExpression`, and result operators such as `ContainsResultOperator` and `AnyResultOperator`.
- `src/DataLinq/Linq/QueryBuilder.cs`
  Resolves query-source references, relation subqueries, relation counts, local values, and SQL functions while assuming Remotion expression nodes.
- `src/DataLinq/Linq/LocalSequenceExtractor.cs`
  Evaluates Remotion `QueryModel` and `MainFromClause` shapes for local sequences.
- `src/DataLinq/Linq/ProjectionExpressionEvaluator.cs`
  Uses Remotion query-source identities to bind materialized row values to projection expressions.
- `src/DataLinq/Query/SqlQuery.cs`
  Accepts Remotion `WhereClause` and `OrderByClause` directly.
- compliance tests
  Some tests still parse through `QueryParser.CreateDefault()` and reflect into `ParseQueryModel`.

So this is not a package swap. It is the query IR boundary.

## What "AOT-Friendly" Actually Means

Normal `IQueryable<T>` cannot honestly mean "no reflection exists anywhere." Expression trees carry `MemberInfo`, `MethodInfo`, and `ConstructorInfo`. A parser must inspect those objects to understand `x => x.Id == id`.

The realistic AOT target is:

- no `Expression.Compile()` in supported query execution
- no reflection invocation on the query hot path
- no runtime metadata discovery for generated model binding
- no dynamic code generation
- no dependency that emits unresolved trim or Native AOT warnings in the supported path
- generated accessors for model values where practical
- explicit compatibility-only fallback paths when reflection remains

That wording matters. "No reflection" sounds clean and is mostly false. "No reflection invocation or dynamic code in the supported generated-model query path" is uglier and accurate.

## Strategic Goals

### AOT And Trim Cleanliness

The supported generated SQLite path should publish without `Remotion.Linq` warnings. Once Roslyn is also split from the runtime package, the practical AOT story becomes credible instead of technically passing with embarrassing baggage.

### DataLinq-Owned Query Semantics

The query support boundary should be defined by DataLinq's support matrix, not by Remotion's clause model. DataLinq already has a deliberately narrow but useful LINQ surface. The new parser should target that surface first.

### Lower Allocation Translation

A DataLinq plan can separate query shape from captured values. That enables structural query-plan caching, parameter rebinding, and fewer temporary Remotion objects per execution.

### Backend-Neutral Planning

SQL should become one execution target for a DataLinq plan. JSON, CSV, log files, in-memory collections, and cache-backed execution need an abstract plan. They should not impersonate `IDbCommand`.

### Better Diagnostics

Unsupported queries should fail with DataLinq-specific diagnostics:

- unsupported LINQ operator
- supported operator in an unsupported shape
- provider capability mismatch
- expression that would require client-side fallback
- translator bug

The current diagnostics improved in Phases 6 and 7. The new plan must preserve that bluntness.

## Non-Goals

- general LINQ-to-everything support
- silent client-side fallback for unsupported predicates
- preserving every Remotion query shape
- making relation loading behave like Entity Framework `Include`
- supporting arbitrary nested database subqueries in the first parser
- supporting `GroupBy` in the first parser
- using string matching over expression `ToString()` as a parser strategy
- warning suppression as the final AOT answer

If this work tries to be a complete LINQ provider, it will fail slowly and expensively.

## Recommended Architecture

### `DataLinqQueryable<T>`

Replace the Remotion `QueryableBase<T>` inheritance with a DataLinq-owned `IQueryable<T>` implementation.

The public query entry point should still look ordinary to users:

```csharp
db.Query().Employees
    .Where(x => x.emp_no == employeeNumber)
    .OrderBy(x => x.last_name)
    .Take(10)
    .ToList();
```

Internally, the provider owns expression creation and execution:

```csharp
internal sealed class DataLinqQueryable<T> : IQueryable<T>
{
    public Type ElementType => typeof(T);
    public Expression Expression { get; }
    public IQueryProvider Provider { get; }
}

internal sealed class DataLinqQueryProvider : IQueryProvider
{
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression);
    public TResult Execute<TResult>(Expression expression);
}
```

The provider parses the final expression tree directly into a DataLinq plan.

### `DataLinqQueryPlan`

The core object should represent query intent, not SQL text:

```text
DataLinqQueryPlan
  Root: SourceSlot
  Sources: SourceSlot[]
  Joins: JoinNode[]
  Predicate: PredicateNode?
  Orderings: OrderingNode[]
  Offset: ValueNode?
  Limit: ValueNode?
  Projection: ProjectionNode
  Result: Sequence | Single | First | Last | Count | Any | Sum | Min | Max | Average
```

The plan should be immutable after parsing. Translators can then safely cache and reuse it with different captured values.

### Source Slots

Every query source needs a stable source slot:

```text
SourceSlot
  Id: int
  Table: TableDefinition
  Alias: string
  ElementType: Type
```

Single-source queries get one slot. Joins and future relation-aware joins get more.

This is the abstraction currently missing from `WhereVisitor` and `OrderByVisitor`, which assume one root table except for the narrow joined collection path.

### Predicate Nodes

Predicates should be explicit:

```text
PredicateNode
  And(PredicateNode[])
  Or(PredicateNode[])
  Not(PredicateNode)
  Compare(left, operator, right)
  In(item, values)
  Exists(RelationSubqueryPlan)
  FixedTrue
  FixedFalse
```

Operands should also be explicit:

```text
ValueNode
  Column(SourceSlot, ColumnDefinition)
  Constant(value)
  CapturedValue(binding)
  LocalSequence(binding)
  Function(SqlFunctionType or DataLinqFunction, args)
```

This preserves the fixed-condition behavior added in Phase 6 and makes empty local sequence semantics testable without relying on visitor state.

### Projection Nodes

Projection should not require expression compilation.

First-slice projection nodes:

```text
ProjectionNode
  Entity(SourceSlot)
  Column(SourceSlot, ColumnDefinition)
  AnonymousObject(Constructor, MemberBindings)
  ComputedClientExpression(Expression, BoundSources)
```

The last one is the danger zone. Current projection support includes row-local computed projection materialization. Under AOT, that must use an interpreter or generated accessor strategy, not `Expression.Compile()`.

The first implementation can keep a small interpreter for supported expression shapes, but reflection invocation should be treated as debt:

- property read: prefer generated accessor by table metadata
- string operations: explicit interpreter cases
- arithmetic and conversion: explicit interpreter cases
- anonymous object construction: acceptable only if trim/AOT analysis is clean, otherwise generated projector or narrower projection support

### Captured Values

The parser should separate query shape from values:

```text
Where(x => x.emp_no == employeeNumber)

Shape:
  Compare(Column(t0.emp_no), Equal, CapturedValue(0))

Bindings:
  0 -> employeeNumber
```

This is the basis for low-allocation repeated execution. The shape can be cached; the value changes.

### Provider Translators

SQL translation becomes one backend:

```csharp
internal interface IQueryPlanExecutor
{
    TResult Execute<TResult>(DataLinqQueryPlan plan, QueryBindingFrame bindings);
}
```

SQL providers would translate the plan into the existing `SqlQuery<T>` or a future lower-allocation SQL renderer.

Non-SQL backends would execute the same plan differently:

- in-memory/cache executor over `RowData`
- JSON file executor over generated serializers and indexed fields
- CSV executor over typed rows or column spans
- logfile executor over streaming parsed records

This is the point where "other backends" becomes real. Without this separation, JSON/CSV/logfiles become a fake SQL provider or a pile of special cases.

## Replacement Options

### Option 1: Suppress Remotion Warnings

This is the smallest change and the weakest answer.

Pros:

- fast
- low implementation risk
- buys time

Cons:

- leaves Remotion in the runtime graph
- leaves third-party query semantics as the core IR
- does not help non-SQL backends
- still requires an audit to be defensible

This is acceptable only as a temporary measurement aid.

### Option 2: Direct Parser Rewrite

Build the DataLinq parser and delete Remotion in one major implementation.

Pros:

- fastest route to a clean dependency graph if it works
- no temporary adapter layer

Cons:

- high regression risk
- hard to compare behavior while changing everything
- large blast radius across joins, relation predicates, local sequences, projections, and scalar operators

This is attractive in the way cliffs are attractive from a distance.

### Option 3: DataLinq Plan With Remotion Adapter

Introduce `DataLinqQueryPlan`, translate existing Remotion output into it, then add the new parser beside it.

Pros:

- preserves current behavior while creating the new boundary
- enables plan-level tests before parser replacement
- lets SQL generation move before parser replacement
- supports dual-run parity checks
- gives non-SQL backends a real target

Cons:

- more total code during transition
- Remotion remains temporarily
- requires discipline to avoid building two permanent systems

This is the recommended path.

### Option 4: Compatibility Package Split

Move Remotion-backed query support to a legacy or compatibility package.

Pros:

- keeps advanced/legacy shapes available for users who need them
- allows the main generated/AOT runtime package to stay clean

Cons:

- package complexity
- two support surfaces
- easy to confuse users unless naming is extremely clear

This may be useful later, after the internal parser covers the documented generated/AOT subset.

## Migration Workstreams

### Workstream A: Query Support Baseline

Before changing parser behavior, lock down the current supported query matrix.

Tasks:

1. Treat [LINQ Translation Support Matrix](../../support-matrices/LINQ%20Translation%20Support%20Matrix.md) as the parity contract.
2. Add missing SQL-shape assertions for high-risk composition cases.
3. Ensure tests cover:
   - chained `Where`
   - boolean grouping and negation
   - local `Contains`
   - projected local `Contains`
   - local object-list `Any(predicate)`
   - fixed true/false conditions
   - string/date/nullable functions
   - scalar aggregates
   - single-source projections
   - explicit join baseline
   - relation `Any` and existence-equivalent `Count`
4. Classify anything without tests as unsupported or undocumented.

Exit criteria:

- the current Remotion-backed behavior has a clear executable baseline
- unsupported diagnostics are covered for common failure shapes

### Workstream B: DataLinq Query Plan

Introduce the plan model without removing Remotion.

Tasks:

1. Add plan node types for source, predicate, ordering, projection, join, local sequence, result operator, and aggregate.
2. Add a `RemotionQueryPlanAdapter`.
3. Convert existing `QueryExecutor.BuildSqlQuery` flow to consume `DataLinqQueryPlan`.
4. Keep the existing Remotion path as the producer.
5. Add plan snapshot tests for representative queries.

Exit criteria:

- SQL execution still passes through the existing support matrix
- `SqlQuery<T>.Where(WhereClause)` and `OrderBy(OrderByClause)` are no longer the main translation boundary
- generated SQL remains equivalent for supported single-source queries

### Workstream C: Predicate And Ordering Translators

Move the valuable logic from `WhereVisitor`, `OrderByVisitor`, and `QueryBuilder` into plan builders.

Tasks:

1. Replace visitor state counters for negation and OR with explicit predicate nodes.
2. Resolve columns through source slots.
3. Preserve nullable comparison semantics.
4. Preserve fixed-condition semantics.
5. Preserve provider-specific function mapping through abstract function nodes.
6. Add a source-slot-aware ordering translator.

Exit criteria:

- single-source `Where` and `OrderBy` no longer require Remotion clause types after the plan is built
- joined-query predicate work has a path to reuse the same source-slot resolver

### Workstream D: Local Sequence Handling

Rebuild local sequence extraction against normal expression trees and plan bindings.

Tasks:

1. Parse local constants, arrays, lists, sets, and supported spans.
2. Parse local `Select(...)` projections that do not depend on query sources.
3. Support local object-list equality membership for `Any(predicate)`.
4. Preserve empty-sequence fixed-condition behavior.
5. Reject complex local predicates explicitly.

Exit criteria:

- local sequence support no longer requires Remotion `QueryModel`
- supported local collection shapes produce plan nodes, not immediate SQL mutations

### Workstream E: Projection Interpreter

Remove reflection-heavy projection execution from the generated/AOT path.

Tasks:

1. Inventory every `MethodInfo.Invoke`, `PropertyInfo.GetValue`, `FieldInfo.GetValue`, and constructor invocation in query projection/local evaluation.
2. Replace row-member reads with generated table accessors where possible.
3. Implement explicit interpreter cases for supported projection operations.
4. Decide whether anonymous projections need generated projectors for clean AOT.
5. Keep compatibility fallback paths separate and clearly marked.

Exit criteria:

- generated SQLite AOT smoke uses no reflection invocation for supported projection shapes
- unsupported projection expressions fail with `QueryTranslationException`
- trim/AOT analyzer output stays clean for the supported path

### Workstream F: DataLinq Expression Parser

Build the Remotion replacement parser over `System.Linq.Expressions`.

First-slice method support:

- `Queryable.Where`
- `Queryable.OrderBy`, `ThenBy`, and descending variants
- `Queryable.Select`
- `Queryable.Skip`
- `Queryable.Take`
- `Queryable.Any`
- `Queryable.Count`
- `Queryable.Single`, `SingleOrDefault`
- `Queryable.First`, `FirstOrDefault`
- `Queryable.Last`, `LastOrDefault`
- scalar aggregates already documented in the support matrix
- narrow explicit `Join` baseline

First-slice predicate support should match the support matrix, not general LINQ.

Tasks:

1. Parse method-call chains from the final query expression.
2. Build source slots from DataLinq query roots.
3. Parse lambdas into plan predicates, ordering nodes, projection nodes, and result operators.
4. Reuse local sequence and projection interpreters.
5. Produce diagnostics that name the unsupported method or expression shape.

Exit criteria:

- the new parser can execute the generated SQLite smoke path
- the new parser passes the documented single-source support matrix
- join/relation features are either supported or explicitly left on Remotion until their workstream lands

### Workstream G: Dual-Run Parity

Run both parsers for supported queries before flipping the default.

Tasks:

1. Add a test mode that parses with Remotion and DataLinq parser.
2. Compare normalized `DataLinqQueryPlan` output.
3. Compare generated SQL templates where relevant.
4. Compare query results across SQLite, MySQL, and MariaDB data sources where available.
5. Record intentional differences.

Exit criteria:

- parity is boring for the supported matrix
- intentional differences are documented and covered
- the new parser is enabled for generated SQLite AOT smoke

### Workstream H: AOT Boundary Switch

Move constrained-platform support to the new parser.

Tasks:

1. Route generated/AOT smoke projects through the DataLinq parser.
2. Remove `Remotion.Linq` roots from AOT and trim smoke projects.
3. Verify Native AOT publish has no Remotion warnings.
4. Verify trimmed publish has no Remotion warnings.
5. Verify Blazor WASM AOT browser smoke still passes.

Exit criteria:

- practical AOT support boundary no longer depends on Remotion
- compatibility-only Remotion path, if kept, is outside the documented AOT support boundary

### Workstream I: Remove Or Isolate Remotion

After parity, decide whether Remotion disappears completely or moves to a compatibility package.

Preferred end state:

- main runtime package has no `Remotion.Linq` dependency
- generated/AOT support path uses only DataLinq parser
- compatibility package exists only if a real user need justifies it

Exit criteria:

- `src/DataLinq/DataLinq.csproj` no longer references `Remotion.Linq`
- `Directory.Packages.props` removes Remotion unless another package owns it
- package dependency inspection confirms Remotion is absent from runtime dependency groups

### Workstream J: Backend-Neutral Execution

Once the plan exists, use it for more than SQL.

Tasks:

1. Define provider capabilities against plan nodes.
2. Add an in-memory/cache executor first.
3. Add read-only JSON or CSV proof after the in-memory executor is stable.
4. Keep backend support narrow and documented.

Exit criteria:

- a non-SQL executor consumes `DataLinqQueryPlan`
- unsupported plan nodes fail through provider capability diagnostics
- JSON/CSV/logfile work does not require SQL builder hacks

## Backend Implications

Replacing Remotion is necessary but not sufficient for JSON, CSV, or logfile backends.

The current provider contract is still SQL-shaped:

- `IDbCommand`
- `ToDbCommand(IQuery)`
- SQL operator rendering
- SQL function rendering
- table-name rendering
- limit/offset rendering

Those are correct for SQLite/MySQL/MariaDB. They are wrong as the universal query abstraction.

The plan should eventually split:

```text
DataLinqQueryPlan
  -> SqlQueryPlanExecutor
  -> InMemoryQueryPlanExecutor
  -> JsonQueryPlanExecutor
  -> CsvQueryPlanExecutor
  -> LogFileQueryPlanExecutor
```

Each executor advertises capabilities. A CSV executor may support column comparisons, ordering, and paging. It probably should not claim relation `EXISTS` until indexing and relationship resolution are designed.

## Risk Register

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Regression in supported LINQ shapes | High | baseline support matrix, dual-run plan parity, provider matrix tests |
| Query semantics drift around nulls | High | dedicated nullable comparison and nullable bool tests |
| Empty local sequence behavior regresses | High | fixed-condition plan nodes and truth-table tests |
| Projection support becomes reflection-heavy again | High | generated/AOT smoke plus reflection invocation inventory |
| Plan model becomes SQL-shaped by accident | Medium | require at least one non-SQL executor proof before declaring backend abstraction done |
| Workstream gets too broad | High | first parser targets documented support matrix only |
| Remotion compatibility path becomes permanent | Medium | set explicit removal/isolation exit criteria |
| Warning suppression hides real AOT failure | High | suppress only after exact call path audit, preferably not at all |

## Effort Estimate

These are real engineering estimates, not wishful sprint names.

| Slice | Estimated effort |
| --- | ---: |
| Query support baseline and missing parity tests | 1-2 weeks |
| DataLinq query plan plus Remotion adapter | 2-4 weeks |
| Move SQL translation behind the plan | 2-4 weeks |
| Local sequence and predicate plan builders | 2-4 weeks |
| Projection interpreter cleanup for generated/AOT path | 2-4 weeks |
| First DataLinq expression parser for single-source queries | 3-6 weeks |
| Join and relation predicate parity | 3-6 weeks |
| Dual-run parity and provider matrix hardening | 2-4 weeks |
| Remove or isolate Remotion from runtime package | 1-2 weeks |
| First non-SQL executor proof | 3-6 weeks |

Practical total:

- Remotion removed from the generated/AOT support boundary: about 2-3 months of focused work.
- Remotion fully removed from the runtime package with current support parity: about 3-5 months.
- Backend-neutral query execution with a credible first JSON/CSV/logfile path: about 5-8 months.

Could this be done faster? Yes, by supporting less. That may be the correct trade if a major release explicitly narrows the query contract. It should not be done by accidentally regressing behavior and pretending nobody noticed.

## Recommended Order

1. Finish the runtime/Roslyn package split from [Practical AOT and Size Plan](../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md).
2. Lock down the LINQ support matrix as parser parity evidence.
3. Introduce `DataLinqQueryPlan` and a Remotion adapter.
4. Move SQL generation behind `DataLinqQueryPlan`.
5. Build the DataLinq expression parser for single-source queries.
6. Add dual-run parity tests.
7. Move generated SQLite AOT/trim/WASM smoke projects to the new parser.
8. Extend parser support to joins and relation predicates.
9. Remove or isolate Remotion from the main runtime package.
10. Build the first non-SQL plan executor.

Roslyn split comes first because it is the obvious payload problem. Remotion replacement comes next because it is the query/AOT warning problem. Non-SQL backends come after the plan exists; doing them before the plan is just duct tape with type parameters.

## Definition Of Done

Remotion replacement is complete when:

- main generated/AOT query path uses DataLinq's parser
- Native AOT smoke publishes and runs without Remotion warnings
- trimmed smoke publishes and runs without Remotion warnings
- Blazor WebAssembly AOT smoke still passes in browser
- supported LINQ matrix passes on the new parser
- unsupported query diagnostics remain specific
- main runtime package no longer depends on `Remotion.Linq`, or Remotion is isolated in a clearly named compatibility package
- query plan can be consumed without SQL-specific types
- current public docs are updated only after behavior ships

Until then, the accurate public statement remains narrow:

> DataLinq has a proven generated SQLite AOT/WASM AOT smoke path, but practical AOT support still depends on removing or isolating warning-producing runtime dependencies.

That is less sexy than "AOT-ready ORM." It is also true.
