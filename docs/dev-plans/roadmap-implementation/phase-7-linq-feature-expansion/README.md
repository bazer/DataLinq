> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.
# Phase 7: LINQ Feature Expansion

**Status:** Implemented.

## Scope

This folder tracks the execution plan for the seventh roadmap phase described in [Roadmap.md](../../Roadmap.md).

Phase 7 expands the LINQ translator after Phase 6 made the existing surface explicit and reliable:

1. add simple scalar aggregates
2. broaden projection support without pretending arbitrary client code is SQL
3. make nullable predicate support a tested support boundary
4. add a narrow explicit `Join(...)` baseline
5. design relation-aware predicate translation over generated relation properties

## Starting Stance

This is not a full query-provider rewrite.

The right move is to extend the current translator in deliberately small steps, backed by compliance tests. Relation-aware translation is the hardest part and should get a design pass before implementation.

## Current Baseline

Relevant implementation files:

- `src/DataLinq/Linq/QueryExecutor.cs`
- `src/DataLinq/Linq/QueryBuilder.cs`
- `src/DataLinq/Linq/Visitors/WhereVisitor.cs`
- `src/DataLinq/Linq/Visitors/OrderByVisitor.cs`
- `src/DataLinq/Linq/LocalSequenceExtractor.cs`
- `src/DataLinq/Query/Join.cs`
- `src/DataLinq/Metadata/RelationDefinition.cs`

Relevant tests:

- `src/DataLinq.Tests.Compliance/Query/EmployeesQueryBehaviorTests.cs`
- `src/DataLinq.Tests.Compliance/Relations/EmployeesRelationAndThreadingTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesDateTimeMemberTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesNullableBooleanTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesUnsupportedQueryDiagnosticsTests.cs`

Relevant docs:

- [`../../query-and-runtime/LINQ Translation Support.md`](../../query-and-runtime/LINQ%20Translation%20Support.md)
- [`../../query-and-runtime/Query Pipeline Abstraction.md`](../../query-and-runtime/Query%20Pipeline%20Abstraction.md)
- [`../../../Supported LINQ Queries.md`](../../../Supported%20LINQ%20Queries.md)
- [`../../../Query Translator.md`](../../../Query%20Translator.md)

## Documents

- `Implementation Plan.md`
