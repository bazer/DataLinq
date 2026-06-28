> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 1 Implementation Plan: Query Contract and Plan Baseline

**Status:** Complete.

**Created:** 2026-06-27.

**Closed:** 2026-06-27.

## Purpose

Phase 1 turned the phase-start Remotion-backed query behavior into an executable migration contract.

The blunt goal is not to make Remotion look good. The goal is to make it impossible for the DataLinq parser replacement to accidentally drop predicates, change null semantics, flatten operator order, weaken diagnostics, or lose documented query shapes while still passing happy-path tests.

The output of this phase should be boring but powerful:

- the supported query surface is mapped to active tests
- high-risk gaps have new regression coverage
- SQL-shape inspection exists where row results are too weak
- unsupported behavior is named instead of guessed
- migration-only Remotion test helpers are centralized and marked for removal

No parser replacement should start before this baseline is good enough to catch regressions.

## Closeout Summary

Phase 1 closed with a source-only query contract audit, centralized Remotion-backed SQL inspection, focused SQL-shape coverage for the highest-risk parser migration areas, explicit `GroupBy(...)` rejection, and explicit rejection of filters/orderings after `Skip(...)` or `Take(...)` until subquery pushdown exists.

The detailed support inventory, Remotion dependency inventory, diagnostics inventory, and Phase 2 handoff live in [Query Contract Audit](Query%20Contract%20Audit.md).

## Current Baseline

Current user-facing support is defined by:

- [Supported LINQ Queries](../../../../Supported%20LINQ%20Queries.md)
- [LINQ Translation Support Matrix](../../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)
- active compliance tests under:
  - `src/DataLinq.Tests.Compliance/Query/`
  - `src/DataLinq.Tests.Compliance/Translation/`

Current migration liabilities:

- SQL inspection is ad hoc and duplicated in `EmployeesQueryBehaviorTests` and `CharPredicateTranslationTests`.
- SQL inspection currently instantiates Remotion's `QueryParser` and reflects into `QueryExecutor.ParseQueryModel`.
- Some support-matrix rows are mostly row-result assertions; those can miss a wrong SQL shape if seed data happens to hide it.
- Some diagnostics currently leak Remotion terms such as result-operator or query-model type names.
- Ordering/paging operator order is known to be semantically dangerous and needs explicit treatment before a plan model is designed.

## Non-Goals

- no DataLinq parser implementation
- no `DataLinqQueryPlan` implementation
- no SQL generator migration
- no public LINQ feature expansion
- no broad join work
- no dependency removal
- no permanent Remotion test utility

If this phase changes production query behavior, that change should be a focused bug fix justified by a failing baseline test, not hidden inside the audit.

## Workstream A: Contract Inventory

Goal: make the migration contract explicit before adding tests.

Tasks:

1. Review the support matrix row by row and classify each row as:
   - **green:** sufficient active tests exist
   - **thin:** row-result tests exist, but SQL/shape assertions are weak
   - **missing:** public/support-matrix claim lacks enough executable evidence
   - **unsupported:** behavior should not be carried forward as a promise
   - **known issue:** current behavior is wrong or nondeterministic and must be documented before migration
2. Add a Phase 1 audit section to `docs/support-matrices/LINQ Translation Support Matrix.md` or create a local `Query Contract Audit.md` in this phase folder and link it from the support matrix.
3. Record every public query claim from `docs/Supported LINQ Queries.md` that must survive Remotion removal.
4. Record every active test that directly depends on Remotion APIs so Phase 7 has a cleanup checklist.

Minimum output:

- a table mapping support areas to evidence files and Phase 1 action
- a list of support claims that are allowed to contract only as an explicit breaking decision
- a list of Remotion-specific active test helpers to remove or rewrite later

## Workstream B: Central SQL/Shape Inspection Helper

Goal: make shape assertions cheap without spreading more Remotion-specific reflection through tests.

Current duplicated helpers:

- `EmployeesQueryBehaviorTests.BuildLinqSelect`
- `CharPredicateTranslationTests.BuildLinqSelect`

Tasks:

1. Add one internal compliance-test helper for translation inspection, probably under `src/DataLinq.Tests.Compliance/Translation/`.
2. Move the phase-start Remotion-backed `QueryParser` plus `QueryExecutor.ParseQueryModel` reflection into that helper.
3. Name the helper so its temporary nature is obvious, for example `RemotionTranslationInspection` or `CurrentQueryTranslationInspection`.
4. Add comments stating:
   - this helper is baseline scaffolding for 0.8 Phase 1
   - it must not become a permanent dependency
   - Phase 7 must remove or replace it
5. Expose only high-level inspection methods such as:
   - build current SQL for a LINQ query
   - assert parameter values/types
   - normalize SQL text enough for stable assertions where needed
6. Refactor the two existing duplicated helpers to use the centralized helper.

Design constraints:

- Do not introduce a new production API just for tests.
- Do not over-normalize SQL. Provider-specific SQL differences are real and should stay visible unless the assertion is intentionally provider-neutral.
- Do not assert exact parameter names unless the behavior is a supported contract. Prefer parameter count, value, type, and stable SQL fragments.

Exit criteria:

- only one active test helper directly instantiates Remotion's parser
- SQL/shape assertions can be added without copy-pasting reflection
- the helper is listed in Phase 7 cleanup tasks

## Workstream C: High-Risk Regression Tests

Goal: add targeted tests where the parser replacement is most likely to regress behavior.

### C1: Predicate Composition

Target files:

- `src/DataLinq.Tests.Compliance/Query/EmployeesQueryBehaviorTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesBooleanLogicTests.cs`

Add or strengthen coverage for:

- `Where(a).Where(b)` across `ToList`, `Count`, `Any`, `First`, `SingleOrDefault`, and paging
- `Where(a).OrderBy(...).Where(b)` preserving both predicates and order
- grouped `&&`, `||`, and `!` where each branch affects the result set
- SQL-shape assertion that both chained predicates appear in the generated predicate group

Phase 1 note:

The existing chained-where coverage is good. The important addition is making the SQL/shape evidence easier to reuse for later plan snapshots.

### C2: Local Collections and Fixed Conditions

Target files:

- `src/DataLinq.Tests.Compliance/Translation/EmployeesContainsTranslationTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesEmptyListQueryTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesLocalAnyPredicateTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesBooleanLogicTests.cs`

Add or strengthen coverage for:

- arrays, `List<T>`, `HashSet<T>`, and tested `ReadOnlySpan<T>` membership
- projected local `Contains(...)`
- empty `Contains(...)` fixed false
- negated empty `Contains(...)` fixed true
- empty `Any()` and `Any(predicate)` fixed conditions
- fixed conditions inside `AND`, `OR`, and negated groups
- non-empty equality-shaped local `Any(predicate)`
- unsupported compound non-empty local `Any(predicate)` still failing clearly

SQL-shape assertions should verify fixed conditions where practical:

- false fixed condition renders as a stable false predicate such as `1=0`
- true fixed condition renders as a stable true predicate such as `1=1`
- empty local sequences do not visit unsupported item/predicate bodies when the result is already determined

### C3: Nullable Semantics

Target files:

- `src/DataLinq.Tests.Compliance/Translation/EmployeesNullablePredicateTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesNullableBooleanTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesDateTimeMemberTests.cs`

Add or strengthen coverage for:

- nullable equals local non-null value
- nullable not-equals local non-null value, including null rows per C# lifted semantics
- `.HasValue` and `!HasValue`
- guarded `.Value`
- guarded nullable date/time member access
- nullable bool equality and inequality against `true`, `false`, and `null`

The new parser must not accidentally collapse SQL three-valued logic into naive `<>` behavior.

### C4: Scalar Results and Aggregates

Target files:

- `src/DataLinq.Tests.Compliance/Query/EmployeesQueryBehaviorTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesAggregateTranslationTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesUnsupportedQueryDiagnosticsTests.cs`

Add or strengthen coverage for:

- `Count()`
- `Count(predicate)`
- `Any()`
- `Any(predicate)`
- `Where(...).Any()`
- `Single`, `SingleOrDefault`, `First`, `FirstOrDefault`, `Last`, `LastOrDefault`
- `Sum`, `Min`, `Max`, `Average` over documented selector shapes
- empty aggregate behavior: `Sum` returns zero; nullable `Min`, `Max`, and `Average` return null where documented
- unsupported aggregate selector diagnostics

SQL-shape assertions should focus on scalar SQL generation where row materialization would hide a wrong execution path.

### C5: Projection Baseline

Target file:

- `src/DataLinq.Tests.Compliance/Translation/EmployeesProjectionTranslationTests.cs`

Add or strengthen coverage for:

- entity projection
- scalar member projection
- anonymous projection
- row-local computed projection
- computed anonymous projection using supported row-local member chains
- relation-property projection remains rejected
- nested database subquery projection remains rejected

Important distinction:

Phase 1 should document that current projection is post-materialization projection, not SQL `SELECT`-list projection. The new parser must preserve that distinction unless a later phase deliberately changes it.

### C6: Relation Predicate Baseline

Target files:

- `src/DataLinq.Tests.Compliance/Translation/EmployeesRelationPredicateTranslationTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesUnsupportedQueryDiagnosticsTests.cs`

Add or strengthen coverage for:

- one-to-many relation `Any()`
- one-to-many relation `Any(predicate)`
- negated relation `Any(predicate)`
- existence-equivalent `Count()` comparisons:
  - `> 0`
  - `>= 1`
  - `!= 0`
  - `== 0`
  - `<= 0`
  - `< 1`
- related-row direct member comparisons
- simple `&&` and `||` groups inside relation predicates
- unsupported relation traversal from related row
- unsupported count thresholds such as `Count() > 1`

SQL-shape assertions should verify correlated `EXISTS` where provider-stable enough. Do not require identical SQL text across providers.

### C7: Current Explicit Join Baseline

Target file:

- `src/DataLinq.Tests.Compliance/Translation/EmployeesJoinTranslationTests.cs`

Add or strengthen coverage for:

- one explicit inner `Join(...)`
- two direct DataLinq query sources
- direct member equality keys
- nullable `.Value` key normalization
- row-local result projection from both sides
- unsupported composite keys
- unsupported `GroupJoin(...)`
- unsupported filtering/ordering/paging/result operators over joined row shapes
- unsupported relation-property projection in join result selector

0.8 requirement:

The current narrow explicit join baseline is part of the documented support surface. The DataLinq parser must carry it unless the release explicitly records a breaking contraction before Phase 7.

### C8: Ordering and Paging Operator Order

Target files:

- `src/DataLinq.Tests.Compliance/Query/EmployeesQueryBehaviorTests.cs`
- optionally a new `EmployeesOrderingPagingTranslationTests.cs`

Add baseline coverage for:

- `OrderBy(...).Take(...)`
- `Take(...).OrderBy(...)`
- `Skip(...).OrderBy(...)`
- `OrderBy(...).Take(...).OrderBy(...)`

Decision policy:

- If current behavior is correct, add row and SQL-shape tests proving subquery pushdown where needed.
- If current behavior is wrong, do not pretend it is supported. Add the shape to the Phase 1 audit as a known issue and either:
  - add a skipped/explicitly documented test case if the test framework supports that cleanly, or
  - add unsupported-shape coverage if DataLinq rejects it, or
  - record the required Phase 2/3 plan-node behavior before parser replacement.

Do not silently flatten these cases into final SQL clause order. That is a semantic bug.

## Workstream D: Unsupported Diagnostics Baseline

Goal: ensure unsupported behavior remains explicit and DataLinq-owned.

Target file:

- `src/DataLinq.Tests.Compliance/Translation/EmployeesUnsupportedQueryDiagnosticsTests.cs`

Tasks:

1. Add coverage for unsupported operators that the public docs name:
   - `TakeLast`
   - `SkipLast`
   - `TakeWhile`
   - `SkipWhile`
2. Add or strengthen coverage for unsupported query shapes that are high-risk during parser replacement:
   - `GroupBy`
   - `GroupJoin`
   - composite-key join
   - relation-property projection
   - unsupported local `Any(predicate)`
   - unsupported aggregate selector
   - unsupported client method in predicate
3. Decide which failures should be `QueryTranslationException` and which legacy failures remain `NotSupportedException`.
4. Record any diagnostics that currently leak Remotion class names.

0.8 target:

By the time the new parser becomes default, unsupported diagnostics should name DataLinq operators/expression shapes rather than Remotion `ResultOperator` or `QueryModel` types.

## Workstream E: Remotion Dependency Inventory

Goal: make Phase 7 cleanup measurable before Phase 7 starts.

Tasks:

1. Add a Phase 1 inventory of active Remotion dependencies:
   - production source imports
   - test source imports
   - project/package references
   - AOT/trim roots
   - diagnostics that include Remotion type names
2. Separate the inventory into:
   - **temporary migration scaffolding**
   - **production dependency to replace**
   - **test dependency to rewrite**
   - **documentation/source-plan reference**
3. Add the inventory to the Phase 1 audit artifact.

Known starting points:

- `src/DataLinq/DataLinq.csproj`
- `src/Directory.Packages.props`
- `src/DataLinq.AotSmoke/DataLinq.AotSmoke.csproj`
- `src/DataLinq.TrimSmoke/DataLinq.TrimSmoke.csproj`
- `src/DataLinq/Linq/Queryable.cs`
- `src/DataLinq/Linq/QueryExecutor.cs`
- `src/DataLinq/Linq/QueryBuilder.cs`
- `src/DataLinq/Linq/LocalSequenceExtractor.cs`
- `src/DataLinq/Linq/ProjectionExpressionEvaluator.cs`
- `src/DataLinq/Linq/Evaluator.cs`
- `src/DataLinq/Linq/Visitors/WhereVisitor.cs`
- `src/DataLinq/Linq/Visitors/OrderByVisitor.cs`
- `src/DataLinq/Query/SqlQuery.cs`
- active compliance tests that instantiate `QueryParser`

## Workstream F: Phase Closeout Documentation

Goal: leave Phase 2 with a clean target.

Tasks:

1. Update the Phase 1 README with closeout status when complete.
2. Update `docs/support-matrices/LINQ Translation Support Matrix.md` if Phase 1 discovers a documented claim with weak, missing, or wrong evidence.
3. Update `docs/Supported LINQ Queries.md` only if the current public support statement is wrong today. Do not put future parser behavior in user docs.
4. Add a "Phase 2 handoff" section listing:
   - query shapes that must have plan snapshots first
   - known current behavior bugs
   - migration-only helpers that Phase 7 must delete
   - any support contractions that require explicit release-note treatment

## Suggested Task Order

1. Create the Phase 1 audit artifact and fill the first inventory pass.
2. Centralize Remotion-backed SQL inspection in one test helper.
3. Refactor existing SQL-shape tests to use the helper.
4. Add predicate-composition and local-collection SQL/shape assertions.
5. Add nullable and fixed-condition coverage gaps.
6. Add relation predicate and explicit join parity gaps.
7. Add ordering/paging operator-order baseline or known-issue documentation.
8. Add unsupported diagnostics coverage.
9. Run focused compliance filters.
10. Run broad compliance and provider lanes.
11. Update support matrix and phase closeout notes.

This order front-loads the reusable test harness, then adds the highest-regression-risk query shapes before broader verification.

## Verification Plan

Focused query/translation work should use TUnit tree-node filters through the Testing CLI, for example:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesQueryBehaviorTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesContainsTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesRelationPredicateTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*" --output failures --build
```

Phase closeout should run:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite mysql --alias latest --output failures
```

If server-backed sandbox connectivity fails, follow the repo guidance: use `DATALINQ_TEST_DB_HOST=127.0.0.1` for server-backed Testing CLI commands in the native Windows sandbox, and only escalate after a likely sandbox/network/cache failure.

Docs verification:

```powershell
docfx docfx.json
```

DocFX excludes `docs/dev-plans/**` from public content, so also run a lightweight local markdown-link check over edited dev-plan files or manually inspect links touched by the phase.

## Risks

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Baseline tests encode Remotion quirks as future semantics | High | Classify behavior as supported, unsupported, or known issue before adding assertions. Do not preserve wrong operator-order behavior. |
| SQL text assertions become too brittle across providers | Medium | Prefer provider-specific assertions or stable fragments/parameters. Use exact SQL only where provider scope is fixed. |
| SQL-shape helper spreads Remotion dependency further | Medium | Centralize it, label it migration-only, and list it in Phase 7 cleanup. |
| Row-result tests miss dropped predicates | High | Add SQL/shape assertions for high-risk composition and fixed-condition cases. |
| Unsupported diagnostics remain Remotion-shaped | Medium | Add diagnostic assertions now and refine wording before the new parser becomes default. |
| Phase 1 expands into parser implementation | High | Keep production behavior changes out unless fixing a focused baseline bug. |

## Exit Criteria

Phase 1 is complete when:

- the support matrix has an explicit Phase 1 audit or linked audit artifact
- every public query-support claim is mapped to active tests or marked for correction
- high-risk missing tests have been added
- SQL/shape inspection is centralized in one migration-only helper
- ordering/paging operator-order behavior is either tested as correct or documented as a known issue
- relation-existence predicates and the current narrow explicit join baseline are covered as parser parity requirements
- unsupported diagnostics have focused tests for common failure shapes
- active Remotion dependencies are inventoried for Phase 7 removal
- focused query filters pass
- compliance quick suite passes
- MySQL/MariaDB verification runs for changed translator tests when available
- docs build succeeds

The handoff to Phase 2 should be a short closeout note, not tribal memory.
