> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.
# Phase 6: LINQ Translation Coverage and Query Composition

**Status:** In progress; Workstreams A and B complete.

## Scope

This folder tracks the execution plan for the sixth roadmap phase described in [Roadmap.md](../../Roadmap.md).

Phase 6 is about making ordinary application query shapes reliable:

1. document the tested LINQ support boundary
2. fix chained `Where(...)` composition before adding more translation surface
3. support projected local `Contains(...)` and local object-list `Any(predicate)` where they can safely become `IN` predicates
4. make fixed true/false predicate handling explicit and regression-tested
5. replace common raw `NotImplementedException` translation failures with actionable query diagnostics

## Starting Stance

This is not the query-pipeline-abstraction phase.

The current Remotion-based SQL translator is narrow but useful. The right move is to harden the supported predicate surface, not to start a broad IR rewrite before the existing behavior is classified.

## Current Baseline

Relevant implementation files:

- `src/DataLinq/Linq/QueryExecutor.cs`
- `src/DataLinq/Linq/Visitors/WhereVisitor.cs`
- `src/DataLinq/Linq/QueryBuilder.cs`
- `src/DataLinq/Query/WhereGroup.cs`
- `src/DataLinq/Query/Where.cs`

Relevant test files:

- `src/DataLinq.Tests.Compliance/Translation/EmployeesBooleanLogicTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesContainsTranslationTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesEmptyListQueryTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesNullableBooleanTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesStringMemberTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesDateTimeMemberTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/CharPredicateTranslationTests.cs`

Relevant docs:

- [`../../query-and-runtime/LINQ Translation Support.md`](../../query-and-runtime/LINQ%20Translation%20Support.md)
- [`../../../Supported LINQ Queries.md`](../../../Supported%20LINQ%20Queries.md)
- [`../../../Query Translator.md`](../../../Query%20Translator.md)

## Documents

- `Implementation Plan.md`
- `Support Matrix.md`

## Related Plans

- [`../../query-and-runtime/LINQ Translation Support.md`](../../query-and-runtime/LINQ%20Translation%20Support.md)
- [`../../query-and-runtime/Query Pipeline Abstraction.md`](../../query-and-runtime/Query%20Pipeline%20Abstraction.md)
- [`../../Roadmap.md`](../../Roadmap.md)
