> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 1: Query Contract and Plan Baseline

**Status:** Complete.

## Execution Plan

- [Implementation Plan](Implementation%20Plan.md)
- [Query Contract Audit](Query%20Contract%20Audit.md)

## Purpose

Phase 1 locks down the behavior that the parser migration must preserve. The worst possible start is changing the parser boundary before the support matrix is executable enough to catch subtle regressions.

The existing Remotion-backed translator is the baseline. That does not mean Remotion defines the future semantics. It means current tested DataLinq behavior is the migration contract until we deliberately change it.

## Closeout

Phase 1 closed on 2026-06-27.

Delivered:

- added the [Query Contract Audit](Query%20Contract%20Audit.md) with the support inventory, Remotion dependency inventory, diagnostics inventory, and Phase 2 handoff
- centralized Remotion-backed SQL inspection in `CurrentQueryTranslationInspection`
- refactored the duplicated compliance-test `QueryParser`/`ParseQueryModel` reflection into that helper
- added SQL-shape coverage for empty fixed conditions, local equality-membership `Any(predicate)`, and relation `EXISTS`/`NOT EXISTS`
- added explicit unsupported coverage for `GroupBy(...)`
- changed post-paging filters/orderings from silent wrong-SQL risk to explicit `QueryTranslationException`
- updated the public LINQ docs and support matrix to describe the current paging boundary honestly

Verification:

- `DataLinq.Tests.Compliance.csproj` build passed
- focused compliance filters passed for `EmployeesContainsTranslationTests`, `EmployeesLocalAnyPredicateTests`, `EmployeesRelationPredicateTranslationTests`, `EmployeesUnsupportedQueryDiagnosticsTests`, `EmployeesQueryBehaviorTests`, and `CharPredicateTranslationTests` across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`
- compliance quick passed: 466/466 on `sqlite-file` and `sqlite-memory`
- MySQL lane passed: 152/152 on `mysql-8.4` and `mariadb-11.8`
- `docfx docfx.json` passed with only the pre-existing duplicate analyzer release-note warnings

## Scope

In scope:

- treat the LINQ translation support matrix as the parity checklist
- add missing tests for high-risk supported or semi-supported shapes
- add SQL-shape or future plan-shape assertions where result rows are too weak to catch translator mistakes
- classify untested behavior as unsupported or undocumented
- identify where current behavior is wrong, nondeterministic, or dependent on Remotion shape quirks

Out of scope:

- new parser implementation
- SQL generator migration
- broad join expansion
- new user-facing LINQ support

## High-Risk Shapes

Phase 1 should cover at least:

- chained `Where(...)`
- `Where(...).OrderBy(...).Where(...)`
- local `Contains(...)` across arrays, lists, sets, spans, and local projections
- empty local collection fixed true/false conditions
- equality-shaped local `Any(predicate)`
- nullable equality, inequality, `.HasValue`, and guarded `.Value`
- boolean grouping and negation
- scalar aggregates
- row-local projections
- relation `Any(...)` and existence-equivalent `Count()` predicates
- current narrow explicit `Join(...)`
- `Take(...)` or `Skip(...)` followed by later `OrderBy(...)`

The ordering/paging cases are especially important because SQL clause order is not LINQ operator order. If DataLinq currently lacks the correct subquery behavior, Phase 1 should document that as a known migration issue instead of accidentally baking in the wrong shape.

## Exit Criteria

- [x] support-matrix gaps relevant to the parser migration are documented
- [x] missing high-risk regression tests are added
- [x] unsupported or intentionally deferred shapes are named
- [x] the phase-start Remotion path has enough executable evidence to compare against a DataLinq-owned plan
