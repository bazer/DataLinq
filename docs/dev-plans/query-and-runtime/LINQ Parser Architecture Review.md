> [!WARNING]
> This document is engineering review and planning material. It is not normative product documentation and should not be treated as a shipped support claim.

# LINQ Parser Architecture Review

**Status:** Review report and improvement backlog.

**Created:** 2026-06-29.

**Scope:** DataLinq's post-Remotion LINQ parser, query-plan model, SQL rendering path, local projection evaluation, and the architectural seams that affect maintainability, immutability, cacheability, allocations, AOT, and future non-SQL backends.

## Executive Summary

The current parser architecture is pointed in the right direction. Translating expression trees into a DataLinq-owned `DataLinqQueryPlan` before rendering SQL is the right foundation. It gives the project a real query intermediate representation instead of letting SQL strings or a third-party query model define DataLinq's semantics.

The blunt assessment:

- The architecture is sound enough to keep.
- The implementation is still too SQL-first to call backend-neutral.
- The parser is already too large and too responsible for its own long-term health.
- Immutability is mostly good in the plan nodes, but the binding frame is not fully frozen by type.
- Cacheability is close conceptually, but query shape and runtime values are still fused inside the plan object.
- The generic query path remains allocation-heavy unless query-shape caching is added.
- AOT support has the right instincts, especially avoiding `Expression.Compile()` in the supported path, but compatibility reflection is still the default in important evaluators.

The best next step is not a rewrite. The best next step is to finish the separation the current architecture already started: make the plan truly immutable, split structural query shape from invocation values, move SQL-specific decisions out of the expression parser, and introduce a backend execution boundary.

## Reviewed Areas

Primary files reviewed:

| Area | Files |
| --- | --- |
| Query root and execution | [`ExpressionPlanQueryable.cs`](../../../src/DataLinq/Linq/Planning/Expressions/ExpressionPlanQueryable.cs) |
| Expression parser | [`ExpressionQueryPlanParser.cs`](../../../src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs) |
| Local value evaluation | [`ExpressionLocalValueEvaluator.cs`](../../../src/DataLinq/Linq/Planning/Expressions/ExpressionLocalValueEvaluator.cs) |
| Row-local projection evaluation | [`ProjectionExpressionEvaluator.cs`](../../../src/DataLinq/Linq/ProjectionExpressionEvaluator.cs) |
| Query plan root | [`DataLinqQueryPlan.cs`](../../../src/DataLinq/Linq/Planning/DataLinqQueryPlan.cs) |
| Plan values | [`QueryPlanValue.cs`](../../../src/DataLinq/Linq/Planning/QueryPlanValue.cs) |
| Plan predicates | [`QueryPlanPredicate.cs`](../../../src/DataLinq/Linq/Planning/QueryPlanPredicate.cs) |
| Plan operations | [`QueryPlanOperation.cs`](../../../src/DataLinq/Linq/Planning/QueryPlanOperation.cs) |
| Plan projections | [`QueryPlanProjection.cs`](../../../src/DataLinq/Linq/Planning/QueryPlanProjection.cs) |
| Plan bindings | [`QueryPlanBindingFrame.cs`](../../../src/DataLinq/Linq/Planning/QueryPlanBindingFrame.cs) |
| Source slots | [`QueryPlanSourceSlot.cs`](../../../src/DataLinq/Linq/Planning/QueryPlanSourceSlot.cs) |
| SQL builder | [`QueryPlanSqlBuilder.cs`](../../../src/DataLinq/Linq/Planning/Sql/QueryPlanSqlBuilder.cs) |
| SQL value rendering | [`QueryPlanSqlValueRenderer.cs`](../../../src/DataLinq/Linq/Planning/Sql/QueryPlanSqlValueRenderer.cs) |
| SQL predicate rendering | [`QueryPlanSqlPredicateBuilder.cs`](../../../src/DataLinq/Linq/Planning/Sql/QueryPlanSqlPredicateBuilder.cs) |
| SQL source mapping | [`QueryPlanSqlSourceMap.cs`](../../../src/DataLinq/Linq/Planning/Sql/QueryPlanSqlSourceMap.cs) |
| Derived column mapping | [`QueryPlanDerivedColumnMap.cs`](../../../src/DataLinq/Linq/Planning/Sql/QueryPlanDerivedColumnMap.cs) |

Related design context:

- [`LINQ Parser Architecture`](../../internals/LINQ%20Parser%20Architecture.md)
- [`Remotion.Linq Replacement Plan`](Remotion.Linq%20Replacement%20Plan.md)
- [`Query Pipeline Abstraction`](Query%20Pipeline%20Abstraction.md)
- [`Allocation Reduction Audit`](../performance/Allocation%20Reduction%20Audit.md)
- [`Practical AOT and Size Plan`](../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md)

## Current Architecture

The current pipeline is:

```text
IQueryable expression tree
        |
        v
ExpressionQueryPlanParser
        |
        v
DataLinqQueryPlan
        |
        v
QueryPlanSqlBuilder / SQL renderers
        |
        v
SqlQuery / Select / provider execution
        |
        v
cache-aware materialization and optional row-local projection
```

This is the right broad shape. The query plan is the critical boundary. It means DataLinq can reason about query intent before choosing how to execute it.

The implementation currently has three important layers:

1. The expression parser recognizes supported `Queryable` method calls and expression shapes.
2. The query-plan model records sources, operations, predicates, values, projections, result shape, and bindings.
3. The SQL renderer turns that plan into the existing SQL query object model and execution path.

That is a meaningful improvement over directly translating expression trees into SQL. It also keeps the supported LINQ subset explicit instead of accidentally accepting whatever expression trees happen to be convenient.

## Overall Structure And Maintainability

### What Is Good

The architecture has the right core separation:

- expression-tree parsing is separate from SQL rendering;
- supported query semantics are expressed in DataLinq-owned records;
- source slots give joined and relation-backed queries stable identities;
- projection shape is explicit instead of hidden in ad hoc expression handling;
- unsupported query shapes can fail at parser or renderer boundaries with DataLinq-specific exceptions.

This is a maintainable direction. It is also much healthier than keeping Remotion in the runtime path or treating the old SQL builder as the query model.

### What Is Weak

The main maintainability problem is concentration of responsibility.

`ExpressionQueryPlanParser` is currently a large stateful one-shot parser. It owns:

- query method parsing;
- source slot registration;
- parameter and transparent-identifier binding;
- predicate conversion;
- value conversion;
- projection conversion;
- local value capture;
- implicit relation traversal;
- result operator parsing;
- pushdown and composition rules;
- several backend-shaped restrictions.

That is too much. The file is still workable, but this is exactly how a parser turns into a fragile god class. Adding more operators, relation shapes, backend capabilities, or AOT rules will make it harder to reason about correctness.

### Recommended Decomposition

The parser should be split along real responsibilities:

| Component | Responsibility |
| --- | --- |
| `QueryableMethodParser` | Recognize `Where`, `Select`, `Join`, `GroupBy`, ordering, paging, and terminal operators. |
| `QuerySourceBinder` | Manage source slots, lambda parameters, transparent identifiers, and binding frames. |
| `QueryValueTranslator` | Convert expression nodes into `QueryPlanValue`. |
| `QueryPredicateTranslator` | Convert boolean expressions into `QueryPlanPredicate`. |
| `QueryProjectionTranslator` | Convert entity, scalar, anonymous, member-init, SQL-backed, and row-local projection shapes. |
| `RelationTraversalPlanner` | Own singular relation traversal, relation-exists predicates, relation source creation, and relation-specific diagnostics. |
| `QueryPlanNormalizer` | Own pushdown, operator-order preservation, grouping rewrites, and SQL-independent plan normalization. |
| `BackendCapabilityValidator` | Decide whether a normalized plan can be executed by a specific backend. |

This should not be done as churn for its own sake. It becomes valuable once new query shapes or backends are added. The longer it waits, the more expensive it gets.

## Immutability

### What Is Good

The plan model uses mostly sealed record types and constructor-time collection freezing. `DataLinqQueryPlan` copies sources and operations into arrays and exposes read-only lists. The value, predicate, operation, and projection hierarchies mostly behave like immutable data.

Parser construction state is also isolated. A parser instance is created for a conversion, mutates its internal lists and dictionaries while parsing, then returns a plan. That is a reasonable builder pattern.

### What Is Not Fully Immutable

`QueryPlanBindingFrame` is the biggest hole. It owns a mutable `List<QueryPlanBinding>`. Its `Bindings` property returns a new `ReadOnlyCollection<QueryPlanBinding>` wrapper over that same list.

That means the plan is immutable by convention, not by type. Today that is probably fine because the parser stops mutating the frame after plan construction. But if plans are cached, shared, or reused across threads, convention is not enough.

There is a second, softer concern: plan nodes reference metadata objects such as table, column, and relation definitions. If the metadata graph is truly frozen after initialization, this is acceptable. If any part of metadata remains mutable after provider initialization, cached plans become risky.

### Recommendation

Split mutable builders from immutable snapshots:

```text
parser-time:
  QueryPlanBindingFrameBuilder
  List<QueryPlanBinding>

plan-time:
  QueryPlanBindings
  immutable array or ImmutableArray<QueryPlanBinding>
  indexed lookup by binding id
```

The same principle should apply to other plan collections. Prefer immutable arrays or stable frozen arrays over repeated `ReadOnlyCollection<T>` wrappers. `IReadOnlyList<T>` is a fine public shape, but the backing object should be a true immutable snapshot.

## Cacheability

### Current State

The architecture is close to cacheable because the plan gives DataLinq a structural representation of query intent. The binding model also gives names to captured values and local sequences.

But the current `DataLinqQueryPlan` still carries actual runtime binding values through the binding frame. That fuses query shape and query invocation.

Two queries with the same expression shape and different values should ideally share a plan template:

```csharp
db.Query().Employees.Where(x => x.Id == id1)
db.Query().Employees.Where(x => x.Id == id2)
```

Those should differ at invocation binding time, not at structural plan construction time.

### What A Cacheable Design Needs

The clean model is:

```text
QueryPlanTemplate
  structural sources
  operation tree
  predicate/value shape
  projection shape
  result shape
  binding slot declarations

QueryPlanInvocation
  scalar values
  local sequence values
  runtime options
```

The template can be cached by shape. The invocation is per execution.

Cache keys should include:

- model and table identity;
- operator sequence;
- source-slot structure;
- predicate/value expression structure;
- projection and result shape;
- aggregate and function kinds;
- ordering, paging, grouping, and pushdown shape;
- parameter types;
- local sequence element types;
- local sequence cardinality only where rendering strategy depends on it.

Cache keys should not include ordinary scalar values.

### Immediate Cacheability Problems

The most obvious rendering-time issue is binding lookup in `QueryPlanSqlValueRenderer`. Binding lookup currently enumerates `bindings.Bindings`, filters by id, takes two results, and materializes an array. That is allocation-heavy and it will stay visible in query rendering hot paths.

Binding lookup should be O(1) and allocation-free:

- use an array indexed by binding ordinal when ids are dense;
- or use a dictionary built once when ids are not dense;
- validate duplicate ids once during freeze;
- avoid LINQ in render-time lookup.

Local sequence values are also copied when rendered. If the consumer only needs to enumerate values, the immutable binding snapshot should expose a read-only sequence without forcing another array.

## Allocations

### Current Allocation Shape

The recent benchmark work has improved narrow hot paths, especially primary-key fetch and relation traversal fast paths. The generic parser and SQL rendering path remains object-heavy.

The main allocation sources are:

- parser-owned lists and dictionaries per parse;
- plan records per parse;
- read-only collection wrappers;
- LINQ enumerator and array allocations in binding lookup;
- local sequence array copies;
- SQL query object graph construction;
- derived-source and column-map construction for pushdown;
- joined-row projection arrays and lookup structures;
- row-local projection evaluator state;
- reflection metadata paths when compatibility projection/local evaluation is used.

Some of this is acceptable. First-time complex query parsing will allocate. The realistic target is not "zero allocations for arbitrary LINQ." That would be fantasy engineering.

The realistic target is:

1. near-zero allocation for recognized hot paths;
2. low allocation for cached query-shape execution;
3. acceptable first-use allocation for complex query parsing;
4. clear benchmark coverage separating parse, render, execute, materialize, and project phases.

### High-Value Allocation Reductions

The highest-value reductions are structural:

1. Replace binding lookup with pre-indexed storage.
2. Avoid per-access `ReadOnlyCollection<T>` wrappers.
3. Avoid repeated `ToArray()` when data is already immutable or invocation-local.
4. Use immutable arrays or frozen arrays for plan collections.
5. Split query shape from invocation values so parser and renderer work can be cached.
6. Cache rendered SQL templates by logical plan shape.
7. Treat joined/computed projection paths as separate benchmark lanes instead of hiding them inside end-to-end query timings.

Micro-optimizing the current one-shot parser is less important than avoiding the parser entirely for repeated query shapes.

### Recommended Benchmarks

Add benchmark lanes for:

- expression parse only;
- plan normalization only;
- SQL rendering only;
- binding-only execution against a cached plan/template;
- row-local projection evaluation;
- joined SQL-backed projection materialization;
- local `IN` sequence rendering at small, medium, and large cardinalities.

End-to-end database benchmarks are useful, but they hide where allocations come from.

## AOT

### What Is Good

The most important positive finding is that the main parser path does not appear to depend on `Expression.Compile()`. That matters. `Expression.Compile()` is exactly the sort of thing that makes Native AOT and trimming support look good in a demo and bad in a release gate.

The parser is also intentionally conservative about arbitrary local method invocation. That is the right instinct for correctness and AOT.

There are strict option types in local and projection evaluation paths, which shows the architecture is aware of the compatibility-vs-AOT distinction.

### What Is Still Risky

The default behavior still allows compatibility reflection in important places:

- local captured/member value evaluation can use reflection for fields and properties;
- projection evaluation can use reflection and constructor invocation for compatibility shapes;
- expression nodes naturally carry `MemberInfo`, `MethodInfo`, `ConstructorInfo`, and `Type` objects;
- projection plans can preserve constructor/member metadata that may not be safe under trimming unless rooted or generated.

This is not automatically wrong. Normal `IQueryable` expression parsing cannot avoid all reflection metadata. The real AOT bar is narrower and more honest:

- no dynamic code generation;
- no `Expression.Compile()` in the supported path;
- no reflection invocation in generated-model hot paths where practical;
- compatibility reflection is explicit and fenced;
- unsupported AOT shapes fail clearly instead of silently falling back.

At the moment, the code is closer to "AOT-aware" than "AOT-strict by default."

### Recommendation

Introduce an explicit AOT mode for the query pipeline:

```text
normal mode:
  compatibility reflection allowed where documented

AOT strict mode:
  no compatibility member reflection
  no compatibility constructor invocation
  no arbitrary local method invocation
  fail with AOT-specific diagnostics when a projection or local value requires unsupported reflection
```

Then add tests that assert strict mode rejects reflection-dependent shapes.

Longer-term, source generation should provide:

- generated local-value accessors for known closure/member shapes where feasible;
- generated projection constructors and member setters;
- generated model materializers;
- generated provider function tables or statically rooted function mappings;
- trim annotations for compatibility-only fallback APIs.

## Openness To Non-SQL Backends

### Current State

The plan model is a good start for non-SQL backends. It records real query intent: sources, predicates, values, ordering, paging, projections, result operators, and relation traversal.

But execution is still SQL-first. After parsing, the current provider path moves directly into `QueryPlanSqlBuilder` and the existing SQL query object model. SQL assumptions also appear in composition and rendering decisions:

- aliases;
- joins;
- derived sources;
- raw SQL operands;
- provider SQL function rendering;
- SQL escaping;
- SQL pushdown;
- SQL-style grouped projection;
- relation traversal through relational joins or exists predicates.

That does not make the design bad. It means the current design has a backend-neutral middle layer but not yet a backend-neutral execution architecture.

### What Non-SQL Backends Need

Do not try to make `QueryPlanSqlBuilder` generic. That would create an abstraction with SQL fingerprints all over it.

Introduce a backend boundary instead:

```csharp
interface IQueryPlanBackend
{
    QueryBackendCapabilities Capabilities { get; }

    bool CanExecute(
        DataLinqQueryPlan plan,
        out QueryPlanUnsupportedReason reason);

    object Execute(
        DataLinqQueryPlan plan,
        QueryPlanInvocation invocation);
}
```

The important part is not the exact interface. The important part is capability negotiation. A SQL backend, in-memory backend, document backend, key-value backend, graph backend, and search backend will not support the same shapes.

Capabilities should be explicit:

- filtering;
- ordering;
- paging;
- projection;
- aggregate results;
- grouped aggregate rows;
- joins;
- relation traversal;
- local sequence membership;
- string/date/time functions;
- server-side functions;
- client-side post-processing;
- stable ordering guarantees;
- transaction/snapshot consistency.

Unsupported query diagnostics should say whether the failure is:

- parser unsupported shape;
- normalized-plan unsupported shape;
- backend capability mismatch;
- AOT strict-mode rejection;
- renderer bug.

That separation will matter a lot once SQL is not the only execution target.

## SQL Coupling Boundaries

Some SQL coupling is fine and should remain in the SQL renderer. The issue is where it lives.

Appropriate SQL-specific layer:

- SQL aliases;
- SQL escape characters;
- provider function SQL;
- raw SQL operands;
- derived select rendering;
- SQL parameter materialization;
- SQL join rendering;
- SQL aggregate syntax.

Suspicious in the parser layer:

- pushdown rules expressed only in SQL terms;
- restrictions that are really renderer limitations;
- relation traversal decisions that assume joins are the only viable backend operation;
- grouping/projection restrictions that mix parse validity with SQL renderability.

The parser should decide whether the expression is a supported DataLinq query shape. A later normalization and backend validation phase should decide whether a backend can execute that shape.

## Recommended Work Plan

### Priority 1: Make The Existing Architecture Safer

1. Freeze `QueryPlanBindingFrame` into an immutable binding snapshot before constructing `DataLinqQueryPlan`.
2. Replace render-time binding lookup with O(1), allocation-free lookup.
3. Stop creating `ReadOnlyCollection<T>` wrappers on repeated access paths.
4. Add tests that prove query plans cannot be mutated after construction through exposed collection surfaces.
5. Document the parser architecture's real internal boundaries in dev docs, including what is parser responsibility versus backend responsibility.

### Priority 2: Prepare For Plan Caching

1. Split structural plan shape from invocation values.
2. Add a `QueryPlanTemplate` or equivalent shape object.
3. Add a `QueryPlanInvocation` or equivalent runtime binding object.
4. Define structural cache keys with tests.
5. Cache SQL rendering templates by structural plan shape.
6. Add parse-only and render-only allocation benchmarks.

### Priority 3: Control Parser Complexity

1. Extract value translation from `ExpressionQueryPlanParser`.
2. Extract predicate translation.
3. Extract projection translation.
4. Extract relation traversal planning.
5. Move pushdown and composition rewrites into a normalizer.
6. Keep unsupported-shape diagnostics precise after extraction.

### Priority 4: Harden AOT

1. Expose an AOT-strict query mode.
2. Wire strict local-evaluation and projection-evaluation options through the actual query pipeline.
3. Add strict-mode tests for reflection-dependent closures, member access, constructor projection, and compatibility fallbacks.
4. Add trim/AOT annotations around compatibility-only reflection APIs.
5. Generate projection and accessor helpers where reflection invocation remains on useful supported paths.

### Priority 5: Open The Backend Boundary

1. Define backend capabilities.
2. Add a backend validator phase after plan normalization.
3. Make SQL execution one backend implementation.
4. Add an in-memory backend prototype only after capabilities and diagnostics are explicit.
5. Keep client-side fallback opt-in and visible. Silent fallback is how LINQ providers lie to their users.

## What Not To Do

Do not broaden the parser by accepting arbitrary expression shapes and hoping SQL rendering rejects the bad ones later. The parser should stay conservative.

Do not turn unsupported backend behavior into silent client-side evaluation. That creates correctness and performance surprises.

Do not try to make every query allocation-free. Cache recurring shapes and optimize recognized hot paths instead.

Do not claim AOT support for compatibility reflection paths unless release gates prove the exact shape.

Do not build a "generic SQL builder" and call it backend-neutral. Non-SQL backends need a capability-driven execution boundary, not SQL with a fake mustache.

## Final Assessment

The new LINQ parser architecture is worth investing in. The core decision - expression tree to DataLinq plan to SQL renderer - is correct.

The current risks are not conceptual. They are engineering hygiene risks:

- one large parser class is accumulating too many concerns;
- immutable-looking binding state is not actually frozen by type;
- query-shape caching is blocked by value-bearing plan objects;
- SQL renderability is still too entangled with parsing decisions;
- AOT strictness exists as an option shape but is not yet the normal enforced contract;
- non-SQL backend support needs a real backend boundary and capability model.

The practical recommendation is to keep the architecture, tighten the boundaries, and make the next improvements boring and structural. The fastest path to a better parser is not more parser cleverness. It is fewer responsibilities per parser component, immutable plan snapshots, cached plan templates, and explicit backend capability checks.
