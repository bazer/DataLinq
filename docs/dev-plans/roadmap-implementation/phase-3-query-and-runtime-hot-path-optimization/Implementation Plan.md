> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Phase 3 Implementation Plan: Query and Runtime Hot Path Optimization

**Status:** Planning.

## Purpose

This document turns the Phase 3 goals from [Roadmap.md](../../Roadmap.md) into an execution plan that can actually be worked.

The point of this phase is not to make the query layer look clever.

The point is to make repeated query execution cheaper in the places that are currently visible in code and measurable with the benchmark harness: SQL text generation, parameter binding, command creation, query translation object churn, and the runtime path around materialization and cache lookup.

Phase 1 gave DataLinq measurement tools.

Phase 2 reduced avoidable metadata and factory dynamism.

Phase 3 should use both of those facts and work on the query/runtime path with discipline.

## Current Baseline

Several important things are true in the current code:

- `Sql` is a mutable wrapper around `StringBuilder` plus `List<IDataParameter>`.
- SQLite and MySQL providers create concrete provider parameters during SQL rendering through `GetParameter`.
- `ToDbCommand` calls `query.ToSql()`, then copies `sql.Parameters` into a new provider command.
- parameter names are currently generated while rendering `Where`, `Insert`, and `Update` SQL.
- `Select<T>.ToSql` uses LINQ-heavy string construction for selected columns and ordering.
- `SqlQuery<T>`, `WhereGroup<T>`, `Where<T>`, `Operand`, `Join<T>`, and `OrderBy` form the main SQL shape object graph.
- `QueryExecutor` rebuilds a `SqlQuery<T>` from Remotion `QueryModel` body clauses for each LINQ execution.
- `QueryBuilder<T>` and `WhereVisitor<T>` allocate comparison/operand/group objects while translating expression trees.
- `TryGetSimplePrimaryKey` already shortcuts simple primary-key lookups into cache access, so the warm primary-key path may hide SQL-generation costs.
- the benchmark harness has stable and Phase 2 watchpoint categories, but no dedicated Phase 3 lane for SQL generation, command creation, or repeated same-shape bindings.

That baseline points to a blunt conclusion: the first Phase 3 target should be the `Sql`/provider parameter boundary, not result-set caching and not a giant LINQ provider rewrite.

## Phase Objective

By the end of this phase, DataLinq should be able to answer four questions honestly:

1. Do we know how much SQL generation, command construction, and query translation allocate on the benchmarked hot paths?
2. Are provider-specific parameter objects created at the execution boundary rather than during provider-neutral SQL rendering?
3. Do common repeated query shapes reuse SQL structure where it is clearly worth doing?
4. Did each optimization reduce allocations or runtime on a measured scenario without weakening SQL correctness?

If we cannot answer those questions with benchmark output and tests, this phase is not done.

## Design Stance

This phase should be conservative and evidence-driven.

The right stance is:

- measure the query/runtime path before and after every meaningful slice
- optimize the concrete `Sql` and provider-command boundary first
- keep public query behavior stable
- keep SQLite and MySQL behavior aligned
- prefer internal seams that can be backed out if benchmark data disagrees
- treat struct conversion as a tool, not a strategy
- treat result-set caching as a later product/runtime feature, not as Phase 3 scope

The wrong stance would be:

- rewriting the LINQ provider because the current one is imperfect
- converting every query component into a struct without allocation evidence
- adding a global SQL-template cache before defining bounded keys and invalidation behavior
- claiming wins from one noisy benchmark run
- mixing dependency-tracked result-set caching into the same phase as basic SQL-generation cleanup

## Primary Outcomes

This phase should optimize for a small set of outcomes that matter:

- lower allocated bytes per operation for common query execution paths
- lower command-construction overhead when query shape repeats but values change
- a cleaner provider-neutral representation for SQL text plus raw parameter bindings
- targeted template/binding reuse for proven repeated query shapes
- benchmark categories and telemetry that make query hot-path regressions visible
- no broad public API churn

## Planned Deliverables

### 1. Phase 3 benchmark lane

The existing harness is good enough to extend, but it does not yet isolate this phase.

Deliverables:

- a Phase 3 benchmark category or CLI switch covering query/runtime hot paths
- scenarios that separate cold translation from warm repeated execution where practical
- at least one repeated same-shape query with changing values
- at least one command-construction or SQL-generation-focused scenario that is not hidden by warm row-cache hits
- summary/history rows that preserve the same tracking style introduced during Phase 2

The goal is not to create a perfect microbenchmark museum. The goal is to make the next code changes falsifiable.

### 2. Provider-neutral parameter bindings

`Sql` should stop owning provider-specific `IDataParameter` objects as its primary parameter representation.

Deliverables:

- introduce an internal provider-neutral parameter binding shape such as name plus raw value
- keep parameter ordering deterministic
- preserve null handling and provider-specific normalization rules, including SQLite `Guid` normalization
- move `SqliteParameter` and `MySqlParameter` construction into provider command creation
- keep literal SQL support compatible with existing `IDataParameter` usage or migrate it deliberately
- update tests to prove generated SQL text and parameter values remain correct

This is the most concrete Phase 3 starting point because it directly matches the roadmap goal of moving parameter handling closer to execution time.

### 3. SQL writer allocation reduction

After the parameter boundary is cleaner, reduce SQL text generation allocations in the hot rendering path.

Deliverables:

- audit `Select<T>.ToSql`, `SqlQuery<T>.GetOrderBy`, `Where<T>.AddCommandString`, `WhereGroup<T>.AddCommandString`, and provider formatting helpers
- remove avoidable LINQ/string-join/interpolation churn from measured hot paths
- consider a small internal SQL writer or `ValueStringBuilder`-style helper only where measurement justifies it
- keep the existing string-returning boundary available until the replacement is proven
- preserve formatting-sensitive SQL tests across SQLite and MySQL

This should start with the obvious local allocations before introducing a custom writer abstraction. A custom writer is justified if it removes real churn; otherwise it is just ornamental machinery.

### 4. Query shape and binding split

Once raw bindings exist, split reusable SQL shape from per-execution values where the same shape repeats.

Deliverables:

- define an internal representation for SQL template text plus binding slots
- define a query-shape key for a narrow first slice
- start with simple equality predicates and primary-key-style lookups before handling arbitrary expression trees
- bind values per execution without rebuilding provider-specific parameters during SQL rendering
- scope any cache by provider/table/model rather than using an unbounded global cache
- prove the cache is a win with the Phase 3 benchmark lane before expanding it

This work should be staged. A small template cache that wins on one repeated query shape is valuable. A large structural hash system that is not benchmark-proven is a liability.

### 5. Runtime object churn pruning

After the higher-leverage seams are handled, inspect query object allocation.

Deliverables:

- measure allocations from `QueryExecutor`, `QueryBuilder`, visitors, `WhereGroup<T>`, `Where<T>`, `Operand`, `Comparison`, `Join<T>`, and `OrderBy`
- convert tiny immutable query components to `readonly struct` or `readonly record struct` only where it reduces allocations without boxing
- avoid interface-heavy struct paths that accidentally box and make things worse
- consider pooling or reusing temporary collections only where ownership is clear
- keep expression translation correctness tests as the guardrail

This is intentionally later in the phase. The current query component graph is allocation-heavy, but changing value/reference semantics broadly is risky and often less valuable than fixing parameter and SQL rendering first.

### 6. Runtime/telemetry feedback

The query path already has telemetry for entity/scalar executions. Phase 3 may need more detail, but only if it stays cheap.

Deliverables:

- decide whether SQL generation and command creation need counters or timing spans
- if added, keep them low-cardinality and off the critical path where possible
- expose enough benchmark telemetry to explain whether wins came from fewer queries, less SQL work, cache hits, or materialization changes
- avoid noisy per-query text logging as a benchmark mechanism

## Moved Out of Scope

The `Result set caching` plan is deliberately not a Phase 3 deliverable.

Dependency-tracked result-set caching is a semantic product/cache feature. It needs row freshness/versioning, dependency fingerprints, invalidation semantics, and cache observability. That belongs in a later roadmap phase after cache and invalidation foundations exist.

## Workstreams

## Workstream A: Query Hot Path Audit and Measurement

### Goals

- establish a Phase 3 benchmark lane
- distinguish query translation, SQL rendering, command construction, provider execution, cache lookup, and materialization costs
- avoid optimizing whichever code merely looks ugliest

### Tasks

1. Add Phase 3 benchmark tracking around repeated query execution.
2. Add scenarios for simple primary-key lookup, non-primary-key equality lookup, `IN` predicates, ordering, and scalar `Any`/`Count` where practical.
3. Include a scenario where row-cache warmth does not erase SQL-generation work from the measurement.
4. Capture allocated bytes per operation and existing telemetry deltas in history output.
5. Decide whether additional cheap counters are needed for SQL generation or command construction.

### Explicit non-goal

Do not claim a performance win from local intuition. Phase 3 starts by making the target visible.

## Workstream B: Parameter Binding Boundary

### Goals

- make `Sql` provider-neutral with respect to parameter values
- create concrete provider parameters only when building a provider command
- preserve existing SQL injection safety and provider behavior

### Tasks

1. Inventory all current uses of `Sql.Parameters`, `IDataParameter`, and provider `GetParameter` methods.
2. Introduce an internal raw binding representation.
3. Update SQLite and MySQL providers so `ToDbCommand` materializes provider parameters from raw bindings.
4. Preserve provider-specific value normalization in the provider layer.
5. Handle `Literal` explicitly so raw SQL with manually supplied parameters does not regress.
6. Add focused tests for parameter names, values, nulls, `IN` lists, insert/update parameters, and literal SQL compatibility.
7. Run unit/compliance/provider tests for the touched providers.

### Explicit non-goal

Do not change how users write parameterized queries. This is an internal boundary cleanup, not a query API redesign.

## Workstream C: SQL Writer Allocation Reduction

### Goals

- reduce allocation in SQL text construction
- avoid premature abstraction
- keep generated SQL stable

### Tasks

1. Measure the allocation profile after Workstream B.
2. Replace obvious LINQ/string-join/interpolation allocations in hot rendering methods.
3. Evaluate whether `Sql` should expose append helpers for identifiers, parameter names, and comma-separated lists.
4. Consider a stack-friendly or pooled writer only after the local cleanup has numbers.
5. Keep SQL output tests focused on behavior, not incidental whitespace unless the current tests require exact text.

### Explicit non-goal

Do not introduce a fancy writer that makes the code harder to reason about before the simple changes have been measured.

## Workstream D: Template and Binding Reuse

### Goals

- reuse SQL shape for repeated query patterns
- bind values cheaply per execution
- keep cache scope and invalidation boring

### Tasks

1. Define the smallest query shape worth caching.
2. Start with a narrow repeated equality predicate or primary-key-adjacent path.
3. Represent template slots independently from runtime values.
4. Add a bounded provider/table-scoped template cache.
5. Prove the cache improves the Phase 3 benchmark lane.
6. Expand only if the first slice pays for itself.

### Explicit non-goal

Do not design a universal SQL structural hash up front. Build the narrow version first, then let evidence decide whether it deserves to grow.

## Workstream E: Query Object Churn Cleanup

### Goals

- reduce temporary query object allocation where it remains meaningful
- avoid value-type changes that secretly allocate through boxing or interfaces
- preserve LINQ translation behavior

### Tasks

1. Profile allocations in `QueryExecutor`, `QueryBuilder`, `WhereVisitor`, and query component types.
2. Convert `OrderBy`, `Comparison`, or operand shapes only if they show up in measured allocation costs.
3. Replace temporary arrays and LINQ enumeration with direct loops where it matters.
4. Keep the expression translation tests broad enough to catch precedence, negation, nullable bool, `Contains`, and string/date function regressions.

### Explicit non-goal

Do not struct-ify the whole query tree because a design note suggested it. That is how performance work becomes cosplay.

## Proposed Execution Order

1. Add the Phase 3 benchmark lane and run a baseline.
2. Refactor the parameter binding boundary so `Sql` carries raw bindings.
3. Re-run benchmarks and provider tests.
4. Reduce obvious SQL writer allocations.
5. Re-run benchmarks and compare against the baseline.
6. Implement a narrow template/binding reuse slice if the benchmark lane shows repeated-shape cost worth attacking.
7. Profile remaining object churn and apply targeted cleanup.
8. Close the phase with benchmark interpretation, follow-up notes, and roadmap updates.

## Verification Plan

At minimum, each implementation slice should run the focused tests it touches.

Before closing the phase, run:

- `DataLinq.Tests.Unit`
- `DataLinq.Tests.Compliance`
- provider-specific SQLite tests
- provider-specific MySQL/MariaDB tests where available locally
- `DataLinq.Generators.Tests` only if generator/runtime hooks are touched
- the Phase 3 benchmark lane in smoke mode
- the Phase 3 benchmark lane in the default profile for SQLite in-memory before claiming a performance result

If local MySQL/MariaDB infrastructure is unavailable, record that honestly rather than pretending SQLite proves provider-neutral behavior.

## Exit Criteria

Phase 3 is complete when:

- a Phase 3 benchmark lane exists and has a usable baseline/history workflow
- provider parameter objects are created at command construction time for the main SQLite and MySQL query paths
- SQL generation allocation has been reduced on at least one measured hot path
- repeated query shape/template reuse has either landed for a narrow proven case or been explicitly rejected with benchmark evidence
- query object churn cleanup has been attempted only where measurements justify it
- relevant tests pass
- the implementation summary records what improved, what did not, and what remains noisy

## Non-Goals

- broad LINQ provider rewrite
- public query API redesign
- async query API work
- new provider work
- schema validation or migrations
- dependency-tracked result-set caching
- global unbounded SQL template caches
- full query component struct conversion without allocation evidence
- benchmark claims based on a single noisy run

## Risks

- SQL correctness regressions from changing parameter naming or binding order
- SQLite/MySQL divergence hidden by testing only one provider
- accidental boxing from value-type query components
- cache growth or stale template reuse if query-shape keys are too loose
- optimizing warm primary-key cache paths that no longer exercise SQL generation
- making telemetry expensive enough to distort the benchmark being measured
- preserving literal SQL compatibility while changing internal parameter representation

## First Implementation Slice

The first slice should be Workstream A.

Concrete first step:

1. add a Phase 3 benchmark category or CLI switch
2. add repeated same-shape query scenarios that keep SQL generation/command construction visible
3. run a smoke baseline
4. only then start the parameter binding refactor

That order is not bureaucracy. It is the guardrail that keeps this phase from becoming a vibes-based optimization pass.
