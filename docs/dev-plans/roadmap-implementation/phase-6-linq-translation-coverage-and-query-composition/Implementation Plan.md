> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Phase 6 Implementation Plan: LINQ Translation Coverage and Query Composition

**Status:** In progress. Workstream A has a support-matrix baseline in [`Support Matrix.md`](Support%20Matrix.md).

## Purpose

This document turns the Phase 6 goals from [Roadmap.md](../../Roadmap.md) into an execution plan.

The point of this phase is not to make DataLinq understand every LINQ operator.

The point is to make common SQL-backed query predicates predictable enough that developers can write ordinary application filters without guessing which expression-tree accident will work today.

If a query shape is supported, it should be tested, documented, and translated consistently across SQLite, MySQL, and MariaDB. If it is not supported, it should fail with an actionable translation error instead of leaking visitor internals.

## Starting Baseline

Several important things are already true:

- the query provider uses Remotion `QueryModel` parsing through `Queryable` and `QueryExecutor`
- `QueryExecutor.ParseQueryModel` builds `SqlQuery<T>` from body clauses and result operators
- `WhereVisitor<T>` handles the current supported predicate subset
- `QueryBuilder<T>` resolves columns, constants, SQL functions, comparisons, nullable bool special cases, and boolean group state
- `WhereGroup<T>` and `Where<T>` already model grouped predicates, fixed conditions, `IN`, `NOT IN`, `LIKE`, null checks, and comparison operators
- compliance tests already cover a substantial query surface in `src/DataLinq.Tests.Compliance/Translation`
- user-facing docs now have conservative pages for supported LINQ shapes and translator internals

The current risks are also concrete:

- `QueryExecutor.ParseQueryModel` returns immediately after parsing a nested query model, which risks dropping outer clauses in chained `Where(...)` and related composed query shapes
- local `Contains(...)` works for direct materialized collections but projected local collections often need manual `ToArray()` materialization
- local `Any(predicate)` over object lists only recognizes a narrow equality shape
- empty collections and fixed true/false conditions work in several cases but are not governed by one explicit truth table
- unsupported translation paths previously threw raw `NotImplementedException`

## Phase Objective

By the end of this phase, DataLinq should be able to answer five questions honestly:

1. What LINQ predicate, ordering, projection, and result-operator shapes are supported?
2. Does chained query composition preserve every predicate and apply ordering/paging in the correct phase?
3. Can local collection membership patterns translate without forcing manual materialization when the local pipeline is safe to evaluate?
4. Are empty collections, negation, `AND`, `OR`, and fixed conditions represented by explicit invariants?
5. Do unsupported query shapes fail with useful diagnostics instead of raw implementation exceptions?

## Design Stance

The right stance is:

- write regression tests before changing translation behavior
- preserve the current SQL-backed translator while tightening its supported surface
- share local-value extraction between `Contains(...)` and `Any(predicate)` instead of adding one-off visitor branches
- reject client-side fallback for unsupported predicates
- keep provider-specific SQL function differences behind the provider API
- update user-facing support docs only after tests prove the shape

The wrong stance would be:

- rewriting the query pipeline before the existing behavior is classified
- adding arbitrary `Enumerable` method support because one case looks easy
- silently evaluating database-dependent predicates on the client
- treating all expression-tree shapes that compile in C# as valid SQL
- fixing diagnostics so broadly that it masks real translator bugs

## Planned Deliverables

### 1. LINQ Support Matrix

Deliverables:

- audit current translation compliance tests
- map tested support into categories: predicates, local collections, member/method translation, result operators, ordering, projections, and unsupported operators
- update `docs/Supported LINQ Queries.md` only for behavior that is actively tested
- keep speculative work in dev-plan docs, not user-facing docs

### 2. Chained Query Composition

Deliverables:

- add regression tests for `Where(a).Where(b)` with `AND`-style composition
- add regression tests for chained `Where(...)` mixed with `OrderBy`, `Skip`, `Take`, and terminal result operators where the current parser is at risk
- fix `QueryExecutor.ParseQueryModel` so nested query models are parsed first and outer body clauses/result operators are then applied, instead of returning early
- verify generated behavior through compliance tests across active providers

### 3. Local Projection Evaluation for `Contains(...)`

Deliverables:

- support safe local pipelines such as `ids.Select(x => x.Id).Contains(row.Id)`
- support nullable `.Value` and conversion wrappers on the query-column side
- support arrays, lists, sets, and projected local sequences when the projection does not depend on the database query source
- preserve current empty-list fixed-condition behavior
- reject local projections that reference the database query source or require unsupported client methods

### 4. Local Object-List `Any(predicate)`

Deliverables:

- support `list.Any(item => item == row.Column)`
- support `list.Any(item => item.Member == row.Column)`
- support reversed equality: `list.Any(item => row.Column == item.Member)`
- unwrap nullable conversions on either side where the underlying column/value types are compatible
- translate the supported shapes to `IN` or `NOT IN` over extracted local values
- keep complex predicates such as ranges, compound conditions, and database-dependent local method calls unsupported until they have a separate design

### 5. Fixed Condition Invariants

Deliverables:

- write an explicit truth table for:
  - `empty.Contains(x)`
  - `!empty.Contains(x)`
  - `empty.Any()`
  - `!empty.Any()`
  - `empty.Any(predicate)`
  - `!empty.Any(predicate)`
- add nested `AND`, `OR`, and negation tests around fixed conditions
- ensure `WhereGroup<T>` rendering keeps parentheses and connection types stable
- ensure empty collection predicates do not visit unsupported predicate bodies when the collection itself determines the result

### 6. Translation Diagnostics

Deliverables:

- introduce or reuse a query translation exception type for unsupported expression shapes
- include the expression shape, operator/method name, and a short rewrite hint where practical
- replace the common user-facing raw `NotImplementedException` paths in `WhereVisitor<T>`, `QueryBuilder<T>`, and selector handling
- preserve bug visibility: internal impossible states should still fail loudly
- update troubleshooting docs after the diagnostic behavior is implemented

## Workstreams

## Workstream A: Translation Surface Audit

Goals:

- make the current supported surface explicit before widening it
- identify tests that already prove behavior versus docs that overstate or understate support

Tasks:

1. Inventory `src/DataLinq.Tests.Compliance/Translation/*.cs`.
2. Inventory query behavior tests that exercise ordering, paging, result operators, and projections.
3. Compare the test inventory against `docs/Supported LINQ Queries.md`.
4. Record unsupported but intentionally rejected operators such as `TakeLast`, `SkipLast`, `TakeWhile`, and `SkipWhile`.
5. Decide which gaps are Phase 6 targets and which remain future query-pipeline work.

Expected output:

- a support matrix section or child document: [`Support Matrix.md`](Support%20Matrix.md)
- a short list of first regression tests: recorded in [`Support Matrix.md`](Support%20Matrix.md#first-regression-tests-to-add)
- no production behavior changes yet

## Workstream B: Chained `Where(...)` Correctness

Status: Implemented in the first Workstream B slice for normal chained query composition over the same source.

Goals:

- fix the highest-risk correctness issue first
- prevent composed query predicates from being silently dropped

Tasks:

1. Add failing tests for `Where(a).Where(b)` using two predicates that each exclude different rows.
2. Add mixed composition tests with `Where(a).OrderBy(...).Where(b)` only if Remotion produces a shape the current parser claims to handle.
3. Refactor `QueryExecutor.ParseQueryModel` so nested query models produce a base `SqlQuery<T>` and the outer model still applies its own body clauses and result operators.
4. Verify `First`, `Single`, `Any`, `Count`, `Skip`, and `Take` still apply correctly.
5. Update internal query translator docs if the parse flow changes materially.

Implementation note:

- Do not try to solve arbitrary subqueries here. The target is nested query models created by normal fluent composition over the same source.

## Workstream C: Shared Local Sequence Extraction

Status: Implemented. Local sequence extraction now has a guarded helper used by existing collection translation paths, including projected local `Contains(...)` sequences.

Goals:

- stop duplicating `Contains(...)` and `Any(predicate)` local-value logic
- make safe local projection evaluation predictable

Tasks:

1. Add a helper that can evaluate a local sequence expression only when it does not depend on `QuerySourceReferenceExpression` or `SubQueryExpression`.
2. Normalize the result to `object[]`, preserving current array/list/set/span behavior.
3. Add projection support for local `Select(...)` pipelines that are fully client-local.
4. Normalize nullable `.Value`, `Convert`, and `ConvertChecked` wrappers around query-column member access.
5. Preserve existing constant-item fixed-condition handling.

Implementation note:

- This helper should live near the translator code first. Promote it only if multiple translator components genuinely need it.

## Workstream D: Local Collection Predicate Expansion

Status: Complete. Workstream D expanded local `Any(predicate)` membership translation for equality-only scalar and object-member shapes, reusing the guarded local sequence extraction path from Workstream C.

Goals:

- translate the ordinary local-membership shapes users actually write
- avoid broad `Enumerable` support creep

Tasks:

1. Rework direct `Contains(...)` translation to use the shared local sequence helper. Complete in Workstream C.
2. Add projected local `Contains(...)` tests. Complete in Workstream C.
3. Rework subquery/result-operator `Contains` translation to share the same column/value extraction path where possible. Complete in Workstream C.
4. Add local object-list `Any(predicate)` tests for item equality, item-member equality, reversed equality, and nullable conversion wrappers. Complete.
5. Translate only equality membership to `IN`/`NOT IN`. Complete.
6. Leave compound local predicates unsupported with a clear diagnostic unless the collection is empty and the truth value is already known. Non-empty compound predicates remain unsupported; Workstream F owns the diagnostic cleanup.

## Workstream E: Fixed Boolean Conditions

Status: Complete. Workstream E documented the fixed-condition truth table and added nested grouping regressions for empty `Contains(...)` and empty `Any(predicate)` paths, including tests that prove unsupported item/predicate expressions are not visited when the empty local sequence already determines the result.

Goals:

- make fixed conditions boring and correct
- avoid boolean grouping regressions while adding local collection support

Truth table:

| Query shape | Fixed condition | SQL predicate |
| --- | --- | --- |
| `empty.Contains(value)` | false | `1=0` |
| `!empty.Contains(value)` | true | `1=1` |
| `empty.Any()` | false | `1=0` |
| `!empty.Any()` | true | `1=1` |
| `empty.Any(predicate)` | false without visiting `predicate` | `1=0` |
| `!empty.Any(predicate)` | true without visiting `predicate` | `1=1` |
| constant `local.Contains(item)` when the item is present | true | `1=1` |
| constant `local.Contains(item)` when the item is absent | false | `1=0` |

Tasks:

1. Document the truth table in the implementation plan or a query-support child doc. Complete.
2. Add tests around empty `Contains` and empty `Any(predicate)` inside nested `AND`, `OR`, and negated groups. Complete.
3. Review `WhereGroup<T>.AddFixedCondition` and SQL rendering for parenthesis and connection-type stability. Complete; fixed conditions are stored as normal `Where<T>` children and therefore preserve each child's explicit connector inside parenthesized groups.
4. Confirm empty local sequence predicates do not evaluate unsupported predicate bodies. Complete.
5. Run the compliance translation lane across SQLite and the available server-backed targets. Complete for quick SQLite and MariaDB 11.8 in this slice.

## Workstream F: Unsupported Query Diagnostics

Status: Complete. Workstream F added `QueryTranslationException`, replaced the common raw `NotImplementedException` paths in LINQ predicate, selector, ordering, and result-operator translation, and added compliance tests for representative unsupported shapes.

Goals:

- make unsupported translation failures useful to developers
- distinguish unsupported user query shapes from translator bugs

Tasks:

1. Define `QueryTranslationException` or a similarly scoped exception if an existing exception is not a good fit. Complete.
2. Replace common raw `NotImplementedException` paths in predicate and selector translation. Complete.
3. Include method/operator names and expression text in the error message. Complete for the representative predicate, selector, and result-operator paths covered here.
4. Add tests for representative unsupported shapes. Complete.
5. Update `docs/Troubleshooting.md`, `docs/Supported LINQ Queries.md`, and `docs/Query Translator.md` after behavior lands. Complete.

## Proposed Execution Order

1. Audit and support matrix.
2. Add regression tests for chained `Where(...)`, projected `Contains(...)`, local object-list `Any(predicate)`, and fixed conditions.
3. Fix chained `Where(...)` composition.
4. Add shared local sequence/value extraction.
5. Implement projected `Contains(...)`.
6. Implement local object-list `Any(predicate)`.
7. Tighten fixed-condition invariants.
8. Add query translation diagnostics. Complete.
9. Update user-facing LINQ docs. Complete.

## Verification Plan

At minimum, each implementation slice should run the focused translation tests it touches.

Before closing the phase, run:

- `run --suite unit --alias quick --output failures --build`
- `run --suite compliance --alias quick --output failures --build`
- `run --suite compliance --targets mariadb-11.8 --output failures --build`
- `run --suite compliance --targets 'mariadb-10.11,mariadb-11.4,mariadb-11.8' --batch-size 2 --output failures --build` when the full MariaDB matrix is available
- `run --suite compliance --targets mysql-8.4 --output failures --build` only when the current local MySQL 8.4 host-port authentication issue is resolved

For documentation changes, also run `docfx docfx.json` and verify generated output when feasible.

## Exit Criteria

Phase 6 is complete when:

- the supported LINQ query matrix is grounded in active tests
- chained `Where(...)` preserves all predicates
- projected local `Contains(...)` works for safe local projections
- local object-list `Any(predicate)` works for the supported equality-membership shapes
- fixed true/false collection predicates have explicit tests for empty, negated, `AND`, and `OR` composition
- unsupported common query shapes fail with actionable query translation diagnostics
- user-facing docs describe supported behavior without claiming broader LINQ support than tests prove

## Non-Goals

- full query-provider rewrite
- query-pipeline abstraction or provider-independent IR
- arbitrary joins through LINQ; a narrow explicit join baseline is planned for Phase 7
- arbitrary subqueries against database tables
- `GroupBy(...)` and aggregate expansion; simple scalar aggregates are planned for Phase 7
- implicit relation-property query translation; relation-aware predicates are planned for Phase 7
- client-side fallback for unsupported predicates
- in-memory provider work
- Native AOT or WebAssembly readiness work
- dependency-tracked result-set caching

## Risks

- partial local evaluation can accidentally evaluate database-dependent expressions if the guard is too loose
- chained query composition fixes can change result-operator ordering if nested and outer models are merged carelessly
- `IN` generation for large local sequences can create unpleasant SQL; this phase should preserve behavior, not invent batching
- better diagnostics can hide bugs if every internal failure is relabeled as "unsupported"
- provider differences in boolean and string SQL can make a green SQLite lane misleading

## First Implementation Slice

The first implementation slice should be Workstream A plus the smallest useful part of Workstream B.

Concrete first step:

1. add a Phase 6 support matrix from the current compliance tests
2. add failing or proving regression tests for `Where(a).Where(b)`
3. fix `QueryExecutor.ParseQueryModel` only enough to preserve inner and outer predicates
4. run the quick compliance lane before touching local collection translation

That is the right first slice because losing a predicate is a correctness bug. Projected local collections and friendlier diagnostics matter, but they should build on a parser that composes clauses correctly.
