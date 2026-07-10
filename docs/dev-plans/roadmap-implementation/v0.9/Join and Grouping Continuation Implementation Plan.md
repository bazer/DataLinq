> [!WARNING]
> This document is roadmap implementation material. It describes optional and later work, not shipped DataLinq behavior or a committed 0.9 feature set.

# Optional Join Expansion And Later Grouping Plan

**Status:** Proposed.

**Target:** Optional 0.9 stretch candidate only; not part of the baseline.

**Created:** 2026-07-04.

**Last reviewed:** 2026-07-10.

## Purpose

This document defines one bounded query-expansion option after the 0.9 architecture, scalar/UUID correctness, read-only memory preview, and release-evidence gates are secure. It also records the order of later join and grouping candidates without presenting them as 0.9 commitments.

Durable API discussion remains in [Relation-Aware Join API](../../query-and-runtime/Relation-Aware%20Join%20API.md). Shipped behavior belongs in [Supported LINQ Queries](../../../Supported%20LINQ%20Queries.md) and the [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md); this plan must not be used as a current support claim.

## Release Decision

Join expansion is not required to ship 0.9.

If the release has capacity for one query-language stretch after all baseline work is green, this plan recommends the following bounded slice:

1. practical multi-inner-join pipelines
2. composite equality join keys over those pipelines

That pair is more useful than another round of increasingly elaborate grouping shapes. It fills ordinary relational-query gaps while exercising the source-slot query plan in a controlled way.

The stretch should be selected explicitly. Starting it is not justified merely because another baseline workstream is temporarily waiting on review or provider infrastructure.

## Entry Gates

Do not start the optional 0.9 slice until:

- the backend-neutral query execution boundary is settled and SQL behavior has not regressed
- the scalar-converter baseline and bounded UUID codec slice are green
- the read-only memory capability set has reached its agreed release boundary
- active provider, AOT/WASM, packaging, and documentation evidence has no unresolved release blocker
- no other 0.9 stretch has been chosen instead

If those gates are reached too late for proper provider evidence, move this whole slice to the next release. A half-tested join engine is worse than a smaller honest support matrix.

## Optional 0.9 Boundary

In scope only if this stretch is selected:

- standard C# query syntax with two or more inner joins
- chained explicit `Queryable.Join(...)` beyond a single joined source
- deterministic source-slot and SQL alias handling for multi-join rows
- direct SQL-backed projection members from all joined source slots
- `Where(...)`, ordering, paging, `Any()`, and `Count()` over supported direct multi-join rows
- composite equality join keys expressed through supported anonymous/tuple-like key shapes
- component-wise scalar-converter and UUID normalization for join keys
- parity between read-only and transaction query roots
- focused diagnostics for opaque, incompatible, or unsupported key/projection shapes

Out of scope for the 0.9 stretch:

- grouping or aggregate continuation over multi-join rows
- materialized `IGrouping<TKey,TElement>` or grouped element enumeration
- `GroupJoin(...)`
- standard or pattern-based left/outer joins
- relation-aware `JoinBy(...)`, `JoinMany(...)`, or left-join variants
- implicit collection projection or hidden row multiplication
- row-local computed joined members used for later provider-side composition
- client-side fallback for unsupported SQL shapes
- claims of general or full LINQ join support

## 0.9 Stretch Workstreams

These IDs are local to this plan; they do not reuse global roadmap phase numbers.

### JOIN-1: Multi-Inner Source Slots

Work:

- add failing parser/plan tests for two and three explicit inner joins
- cover compiler-lowered transparent identifiers from normal query syntax
- generalize source-slot, alias, primary-key selection, and projection binding beyond two sources
- preserve deterministic slot identity through template/bound-plan execution
- keep read-only and transaction query roots on the same planner path

Exit signal:

- ordinary multi-table inner joins produce a stable source-slot plan
- direct members from every source slot materialize from deterministic SQL aliases
- opaque transparent-identifier shapes fail with a focused translation diagnostic

### JOIN-2: Composition Over Multi-Join Rows

Work:

- support `Where(...)`, `OrderBy(...)`, `ThenBy(...)`, `Skip(...)`, and `Take(...)` over direct source-slot members
- support `Any()` and `Count()` over the same bounded row shapes
- preserve operator order through derived joined sources when composition after paging requires a SQL boundary
- reject row-local computed members when later operators would require unavailable provider expressions

Exit signal:

- supported multi-join rows compose consistently across active SQL providers
- post-paging composition does not flatten away required query boundaries
- unsupported computed-row continuation fails during translation, not during materialization

### JOIN-3: Composite Equality Join Keys

Work:

- represent composite join keys as ordered component bindings in the query plan
- lower supported anonymous/tuple-like equality keys to component-wise SQL equality joined with `AND`
- validate component count, nullability, and canonical provider-type compatibility
- normalize typed IDs and UUID physical values per target column without losing component metadata
- cover composite keys in second and later joins, not only the first join pair

Exit signal:

- two- and multi-component inner-join keys work through ordinary query syntax
- every key component uses the same normalization rules as scalar predicates
- mismatched arity, incompatible provider types, and unsupported computed components produce actionable diagnostics

## Verification Gates For The Optional Slice

If selected, the stretch is not complete until these are green:

- expression-parser and plan snapshot tests for two- and three-source joins
- SQL-shape tests proving deterministic aliases and component-wise composite predicates
- behavior tests across SQLite, MySQL, and MariaDB
- read-only and transaction-root parity tests
- scalar-converted and UUID join-component tests
- composition tests before and after paging
- unsupported-shape tests for `GroupJoin`, left joins, opaque identifiers, computed joined continuation, and incompatible composite keys
- support-matrix and user-documentation updates that list only the earned shapes

The release claim, if all gates pass, should be no broader than:

> DataLinq supports documented multi-inner-join pipelines and composite equality join keys across its active SQL providers.

## Ordered Later Candidates

These are not part of the 0.9 baseline or the bounded stretch above.

### JOIN-LJ1: Narrow .NET 10 `Queryable.LeftJoin`

The next join candidate should be the standard .NET 10 `Queryable.LeftJoin` expression shape, restricted initially to direct keys and direct SQL-backed projection members. A standard BCL operator is preferable to inventing DataLinq-specific left-join sugar first.

Before committing it:

- decide the multi-targeting/API story for targets where `Queryable.LeftJoin` does not exist
- define nullable-inner materialization and missing-row identity
- prove SQL null semantics across active providers
- keep `GroupJoin(...).SelectMany(...DefaultIfEmpty())` pattern recognition separate unless it is deliberately implemented and tested

### JOIN-G1: Grouping Continuation Over Multi-Join Rows

Grouping over multi-join source slots can reuse the grouped aggregate-row model after multi-join planning is stable. It should remain a separate feature with its own evidence, not ride along with inner-join work.

Potential bounded scope:

- keys bound directly to source-slot values or already-supported SQL functions
- grouped `Count()`, direct numeric `Sum(...)`, `Min(...)`, `Max(...)`, and `Average(...)`
- grouped-row filtering, ordering, paging, `Any()`, and `Count()` where existing grouped-row rules apply

Materialized groupings and grouped element enumeration remain separate problems.

### JOIN-R1: Relation-Aware Join Sugar

`JoinBy(...)` and `JoinMany(...)` should wait until explicit multi-join, composite-key, and left-join semantics are boring. They add API-design and relation-resolution surface but little new relational capability.

If pursued later, they should lower to the same explicit join plan rather than introduce a second join engine.

## Claims To Avoid

- "full join support"
- "LINQ `GroupJoin` support"
- "left join support" before the narrow standard operator is implemented
- "navigation-property query parity with EF Core"
- "general `GroupBy` support"
- treating `JoinBy(...)` or `JoinMany(...)` as 0.9 release requirements

## Links

- [Relation-Aware Join API](../../query-and-runtime/Relation-Aware%20Join%20API.md)
- [LINQ Parser Architecture Review](../../query-and-runtime/LINQ%20Parser%20Architecture%20Review.md)
- [Scalar Converters And Typed IDs Implementation Plan](Scalar%20Converters%20and%20Typed%20IDs%20Implementation%20Plan.md)
- [UUID Storage Format Support](../../providers-and-features/UUID%20Storage%20Format%20Support.md)
- [Supported LINQ Queries](../../../Supported%20LINQ%20Queries.md)
- [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)
- [DataLinq 0.9 Roadmap](README.md)
