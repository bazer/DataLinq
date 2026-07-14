> [!WARNING]
> This document is roadmap implementation material for the DataLinq 0.9 development line. It is not normative product documentation and should not be treated as a shipped support claim.

# 0.9 Read-Only Memory Backend Implementation Plan

**Status:** Accepted.

**Created:** 2026-07-03.

**Reframed:** 2026-07-10.

**Target:** DataLinq 0.9 experimental preview.

**F7/W8 spike progress (2026-07-14):** The separate non-packable runtime and TUnit projects now exist. Dense `CanonicalProviderValueRow` instances sit behind per-table primary-key ordinal maps, while materialized immutable identities remain in separate existing `RowCache` instances. Direct neutral primary-key lookup and pass-through root entity plans execute without SQL. Repeated direct `Where` equality remains limited to an exact non-nullable `Int32` root column and exact non-null `Int32` scalar binding; one ascending or descending direct non-nullable converter-free `Int32` single-column-primary-key ordering may be followed by one final exact nonnegative `Int32` scalar-binding `Take`. The exhaustive profile remains 31 tokens and admits one final `ScalarMember` sequence over a direct non-nullable converter-free model/provider `Int32` root column with an exact `Int32` result; the selected column may be the primary key or a non-key column. Selectorless `Any` and `Count` reduce either root entities or that exact scalar projection through the same canonical-row cursor; predicate overloads remain bounded to the admitted root-column equality shape. Empty reductions return `false` and `0`, unordered `Any` short-circuits, and `Count` exhausts the selected cursor with checked `Int32` arithmetic. Ordered reductions inherit full-match buffering, and neither reduction materializes an entity, reads the projected cell, nor touches `RowCache`. Terminal operations after paging remain parser pushdowns and reject before row access. The scalar-projection shape still rejects string, nullable, widened, boxed, converter-backed, typed-ID, `Guid`, and non-root alternatives. Natural self-join, grouped-aggregate, and captured row-local projection plans freeze deterministic first capability failures as `SourceCount:Multiple` at `sources`, `Operation:GroupBy` at `operations[0]`, and `Projection:ComputedRowLocalExpression` at `projection`. All three reject before any store/cache diagnostic changes, and the captured projection value is absent from the error. One canonical-row cursor owns scan accounting, equality filtering, ordering, `Take`, cancellation, and disposal. Entity sequences materialize selected identities through `RowCache`; scalar sequences apply the shared scalar materializer/result adapter to the selected canonical cell and perform zero entity-cache work. Unordered projection streams lazily without a stable-order promise, while ordered projection retains full-match buffering and the existing total primary-key order. `Take(0)` performs no scan, `Take` limits above cardinality return all ordered rows, and public count arguments are snapshotted at query construction. Canonical seed publication and source-local identity remain proven, while one shared SQL/memory registry lock serializes resolution through generated binding; a gated unit test proves a concurrent caller cannot observe the winner before binding completes, and a 32-way cold start proves convergence on that graph. The seed API remains internal and provider-valued. Bounded step-8 parity uses the same generated metadata and five adversarial exact-`Int32` rows in SQLite and memory. Every paired shape is parsed once into one `QueryPlanInvocation` and that same invocation executes through both sources. Hard-coded expected values cover every representative current 31-token dimension: unordered root and repeated equality, ascending/descending ordering, zero/bounded/over-cardinality `Take`, key/non-key scalar projection, composed and empty selections, and entity/projected `Any`/`Count`. Unordered outputs are normalized only after execution; ordered outputs are compared as returned. One separate invocation makes the SQLite-only post-`Take` terminal behavior explicit: SQLite succeeds, while memory rejects `Operation:Pushdown` before diagnostics change. Focused memory and capability evidence passes `33/33` and `25/25`; integrated gates remain `1138/1138` unit, `57/57` generator, and `795/795` SQLite file/memory compliance tests. The parity test host intentionally carries SQLite and native assets; the memory runtime remains non-packable, builds cleanly for net8/net9/net10 with zero warnings or errors, and its resolved runtime graphs remain free of SQL-provider and native-database packages. Pre-cancellation and cancellation between entity, filtered, ordered, and projected rows plus scalar-reduction pre-cancellation are proven through the internal spike surface; generated public LINQ supplies `CancellationToken.None`. This is an internal exact-`Int32` semantics, representative-diagnostics, and focused parity proof, not support for other scalar types, broader projections, projected-value predicate overloads, post-projection composition beyond selectorless `Any`/`Count`, `LongCount`, other scalar reductions, element terminals, general ordering, `ThenBy`, `Skip`, stable ties, performance, or general SQL equivalence. W8 steps 6, 7, and 8 are complete at this bounded checkpoint; `M0`, `M1`, `M2`, `F7`, and W8 remain incomplete because model-valued seed conversion, the remaining query subset, wider semantics and parity, typed IDs/`Guid`, public API/package design, concurrent cache maintenance, and constrained-runtime smokes remain open.

## Decision

DataLinq 0.9 should keep the memory backend, but only as a read-only experimental preview.

The preview exists to prove that the runtime can start from generated metadata and execute a deliberately small `DataLinqQueryPlan` subset without SQL. It does not need mutation, transactions, durability, fixture-forking, or broad query parity to prove that point.

The 0.9 claim should be no stronger than:

> DataLinq 0.9 includes an experimental, read-only memory backend for generated models. It supports seeding, primary-key lookup, and a documented query subset through DataLinq query plans, including browser/WebAssembly and strict AOT smoke coverage.

Anything beyond that claim is a separate feature with separate evidence.

## Why This Boundary

The previous plan combined four architecture projects:

- a backend-neutral provider/source and execution boundary
- a read-only memory query engine
- an in-process transactional database
- a persistence and replay system

That is too much for one release and makes the dependency graph circular. A read-only provider is enough to expose whether query plans, materialization, provider values, capabilities, generated metadata, AOT, and browser execution are genuinely backend-neutral. Mutation can follow after those seams have survived real use.

## Ownership And Dependencies

The 0.9 workstreams use local identifiers (`M0` through `M3`) rather than reusing release-wide phase numbers.

| Concern | Owning workstream | Memory dependency |
| --- | --- | --- |
| Backend-neutral provider/source, row-reading, cache, and materialization boundaries | 0.9 query/runtime foundation | Must exist before `M0` is complete |
| Backend-neutral execution boundary, capabilities, and self-contained execution request | 0.9 query/runtime foundation | Must exist before `M1`; supported projection data must not depend on the original expression |
| Model-to-canonical-provider conversion, including typed IDs | Scalar-converter work | Must exist before `M0` is complete |
| Canonical-provider-to-physical UUID encoding | UUID work | Owned by SQL providers; memory must not copy it into row storage |
| Shared provider-value row-buffer type and materializer contract | 0.9 query/runtime foundation | Memory consumes these contracts; it does not define a second row representation |
| Memory tables, indexes, seeding, and execution over shared provider-value buffers | This plan | Owned here |
| Model-to-canonical and canonical-to-model scalar conversion | Scalar-converter work | Memory invokes the shared conversion boundary; it does not own conversion policy |
| Model-valued `RowData` materialization | 0.9 query/runtime foundation plus scalar conversion | Must be proven through the memory adapter in `M0` |
| JSON snapshot codec prototype | Optional JSON stretch plan | Starts only after `M3`; never blocks this plan |

The dependency direction is deliberately one way:

```text
query/runtime foundation + scalar/provider conversion
                         |
                         v
                 read-only memory preview
                         |
                         v
             optional JSON snapshot prototype
```

Memory must consume the shared conversion system. It must not invent a second conversion layer merely to unblock itself. JSON must consume an already-working memory store. It must not be a prerequisite for memory.

## Explicit 0.9 Scope

The preview includes:

- generated-metadata startup with no runtime schema discovery
- explicit seed loading
- canonical provider-value row storage by column ordinal
- primary-key indexes and direct primary-key lookup
- conversion from provider-value buffers to model-valued `RowData` before generated model materialization
- a small, capability-gated query subset
- clear diagnostics for unsupported operations and plan nodes
- no raw SQL path
- strict AOT and browser/WebAssembly proof

The preview does not include:

- insert, update, delete, or `Save`
- transactions, isolation, rollback, or conflict handling
- generated-key allocation or mutation-time defaults
- store forks, reset APIs, named snapshots, or failure injection
- canonical commit batches or change receipts
- persistence, automatic loading, automatic flushing, or durability
- commit logs, replay, or compaction
- broad relation, join, or grouping support
- a claim of SQL semantic equivalence
- a claim that memory is the default substitute for provider-backed tests

## M0: Generated Store And Seed Foundation

Work:

- continue from the separate, initially non-packable `DataLinq.Memory` project created by the `F7` vertical spike
- continue using a separate TUnit `DataLinq.Tests.Memory` project and add it as a distinct local Testing CLI suite before the preview gate
- after the spike passes, promote `DataLinq.Memory` to a preview NuGet package; if the spike fails, stop and re-scope rather than moving the backend into the core package
- start the store exclusively from generated/frozen DataLinq metadata
- store memory rows in the shared compact canonical-provider-value buffer defined by foundation workstream `F3`
- normalize seed model values through the shared scalar-converter pipeline to canonical provider CLR values
- build primary-key identities from canonical provider values
- reject duplicate primary keys and malformed seed values with table, column, and row context
- build the minimum primary-key index needed for direct lookup
- expose raw SQL as an unsupported capability with a DataLinq-owned diagnostic
- avoid native dependencies, runtime schema discovery, runtime code generation, and `Expression.Compile()`

The representation boundary is mandatory:

```text
seed/model value
    -> canonical provider CLR value
    -> memory provider-value buffer
    -> provider-to-model conversion
    -> model-valued RowData
    -> generated immutable model
```

Existing `RowData` and model indexer behavior must remain model-valued. Storing provider values internally does not authorize changing that public/runtime contract.

Exit signal:

- a generated database starts without a SQL provider or live database
- representative rows, typed IDs, and canonical `Guid` values seed successfully through shared conversion
- configured UUID physical formats do not change the canonical value stored by memory
- primary-key lookup returns a correctly materialized generated model
- cache/materialization integration does not expose provider values through `RowData`
- raw SQL fails before any parsing or accidental SQL-provider access

## M1: Capability-Gated Query Subset

Implement the smallest useful subset that exercises query-plan execution:

- direct entity enumeration from one table
- `Where` equality and ordered comparisons over supported scalar columns
- boolean `&&`, `||`, and `!`
- local scalar `Contains(...)` membership
- `OrderBy`, `ThenBy`, `Skip`, and `Take`
- `Any`, `Count`, `First`, `FirstOrDefault`, `Single`, and `SingleOrDefault`
- direct scalar projection from one source
- direct anonymous projection from one source only when the plan contains all information needed to execute it without re-reading the original expression tree

Every supported node must be represented in explicit memory-backend capability metadata. Unsupported joins, relation traversals, grouping, aggregates, projection forms, methods, and result operators must fail capability validation with a diagnostic that names the unsupported shape.

The executor must not:

- compile expression trees
- silently switch to unrestricted LINQ-to-Objects
- generate or parse SQL
- accept a query merely because SQLite accepts it
- re-extract executable projection behavior from the original expression after planning

Exit signal:

- the documented subset executes directly over memory row buffers
- supported projection execution is driven by the execution request/plan, not a hidden copy of the source expression
- unsupported shapes fail predictably before partial execution
- repeated query invocation values do not leak into reusable backend state

Bounded W8 step-7 evidence freezes one parser-valid representative for every named diagnostic family without widening the profile: a self-join rejects as `SourceCount:Multiple` at `sources`, grouped aggregation rejects as `Operation:GroupBy` at `operations[0]`, and a captured computed row projection rejects as `Projection:ComputedRowLocalExpression` at `projection`. Each failure precedes store, cache, predicate, and materialization work, and the captured value is redacted. This completes the spike's representative diagnostic proof, not the broader M1 operator matrix.

## M2: Semantics And Materialization Contract

Before calling any operator supported, document and test its semantics:

- null equality and ordering
- string equality, ordering, and case sensitivity
- numeric comparison and coercion boundaries
- date/time comparison
- enum and typed-ID comparison through canonical provider values
- canonical `Guid` comparison without applying provider-specific UUID byte layouts
- membership with null and empty local sequences
- deterministic paging only when ordering is sufficient
- `First` and `Single` error/default behavior

These are DataLinq memory semantics, not proof of every SQL provider's semantics. Parity tests against SQLite are useful regression pressure, but a matching result for a small sample is not evidence of general SQL equivalence.

The bounded W8 step-8 matrix is intentionally stronger than merely comparing two independently written queries and intentionally weaker than a general parity claim. Each query is built from the generated memory model, parsed exactly once, and the resulting `QueryPlanInvocation` is sent unchanged to the memory and SQLite sources. Both stores receive the same five logical rows with adversarial `Int32` keys (`Int32.MinValue`, `-11`, `0`, `17`, and `Int32.MaxValue`), repeated group values, and distinct names. Hard-coded expected observations cover root and repeated-equality entity results, both key orders, `Take(0)`, bounded and over-cardinality `Take`, key and non-key scalar projections, composed filter/order/page/projection, empty selection, and entity/projected `Any` and `Count`. Unordered entity observations are normalized only after execution because neither backend promises their order; ordered observations are never normalized. The matrix also preserves the deliberate boundary instead of forcing false agreement: a shared post-`Take` terminal invocation returns `true` from SQLite but rejects as memory `Operation:Pushdown` before row or diagnostic work. SQLite/native dependencies belong to the test host only; the `DataLinq.Memory` runtime graph remains clean. These two tests close bounded W8 step 8 for the current 31-token exact-`Int32` island, while wider `M2`/D6 semantics, typed IDs, canonical `Guid`, and general SQL parity remain open.

The bounded `Int32` ordering/`Take` checkpoint intentionally proves only a total-order island. One direct root ordering is accepted when its column is non-nullable, converter-free, model/provider `Int32`, and the table's entire primary key; ascending and descending are both defined by canonical `Int32` order without subtraction. There can be no admitted key ties, so this slice defines no stable-tie or seed-order behavior. Zero or more already-supported pure equality filters may appear before the ordering or between the ordering and final `Take`; the executor evaluates them before sorting because filtering preserves that ordered subsequence and the predicates cannot observe evaluation order. Nothing moves across `Take`, `Skip`, pushdown, a post-projection operator, or a future user-defined evaluation boundary.

`Take` is accepted only once, after that sufficient ordering, with a direct nonnegative `Int32` scalar binding. Unordered, repeated, negative, converted, null, overflowed, and other count shapes reject before store access. `Take(0)` returns empty without scanning; a count above cardinality returns every matching row in key order. `Queryable.Take(local)` receives the value when the query object is built, so re-enumerating the same query keeps that count and rebuilding the query is required to observe a changed local. Ordered execution buffers all canonical matches and is not a top-N or streaming-performance claim; only selected rows cross the materialization/cache boundary. Cancellation checks exist at bounded scan, merge, selection, and yield points only through the internal spike execution surface.

The bounded scalar-projection checkpoint admits only a final `ScalarMember` sequence over exactly one root source. The selected primary-key or non-key column, its model/provider types, and the declared sequence result must all be non-nullable converter-free `Int32`; string, nullable, widened, boxed, converter-backed, typed-ID, `Guid`, anonymous/constructed/local, joined, relation, grouped, aggregate, and element-terminal alternatives reject before store access. Projection composes with the already-admitted equality filters, optional exact primary-key ordering, and optional final `Take`; row selection always completes before the cell is read. The projection cursor applies shared scalar materialization and result adaptation directly to the canonical cell, never materializes an entity, and never touches `RowCache`. Unordered projection is lazy but defines no general stable-order contract; ordered projection retains full-match buffering, and `Take(0)` still performs no scan. Pre-cancellation and cancellation between projected rows are proven only through the internal token-aware execution surface.

Selectorless `Any` and `Count` reduce the admitted entity or exact scalar-projection row island directly through the canonical-row cursor. They intentionally do not materialize entities or projected cells: existence and cardinality depend on selected rows, not selected values. Empty input yields `false` or `0`; unordered `Any` stops after its first selected row, while `Count` exhausts the cursor with checked `Int32` arithmetic. Ordering remains semantically valid but currently buffers every match before reduction, so this is not a performance claim. A predicate overload is supported only when it becomes the existing exact root-column equality operation. Terminal operations after `Take` remain unsupported because the parser represents them as `Pushdown`, and projected-value predicates, `LongCount`, other scalar reductions, element terminals, general SQL parity, and public cancellation remain open.

Cancellation for the bounded spike is checked before execution and at bounded scan, filter, buffered-ordering, materialization, projection, and reduction points. Focused tests prove the original token on pre-cancellation, cancellation between root and filtered entity rows, after buffered ordering between entity yields, between scalar-projection rows, and before `Any`/`Count`. This is internal execution-surface evidence only: generated public LINQ still supplies `CancellationToken.None`, and no general asynchronous or public cancellation API is claimed.

Materialization tests must prove:

- memory stores provider values internally
- provider values are converted back to model values exactly once at the materialization boundary
- `RowData`, model properties, keys, relations, and cache identity receive the representation they expect
- configured scalar converters do not leak provider values into model-facing APIs
- UUID physical codecs remain outside the memory row/materialization boundary

Exit signal:

- each advertised operator has an explicit semantics test matrix
- semantic differences from SQLite/MySQL/MariaDB are documented rather than hidden
- provider/model value separation is enforced by focused tests

## M3: Release Evidence

Required evidence:

- unit tests for row buffers, seed conversion, primary-key normalization, and duplicate-key diagnostics
- memory-provider tests for startup, seed, lookup, supported queries, materialization, and unsupported capabilities
- typed-ID and canonical-`Guid` tests through seed, lookup, predicate, membership, and projection paths where applicable
- regressions proving configured UUID physical formats do not leak byte arrays/text encodings into memory rows
- focused cross-provider tests against SQLite for the intentionally shared subset
- strict AOT smoke using generated models
- browser/WebAssembly smoke with no native SQLite or filesystem dependency
- package and target-framework verification for the new preview surface
- allocation measurements for startup, primary-key lookup, and repeated simple queries; these inform follow-up work and are not arbitrary release thresholds

The browser smoke should prove:

1. generated-provider startup
2. seed loading
3. primary-key lookup
4. one filtered query
5. one ordered/paged query
6. one supported projection
7. one unsupported-query diagnostic
8. typed-ID or canonical-`Guid` behavior when those features are part of the 0.9 claim

No browser persistence is required. Browser execution is the proof.

## Release Boundary

The experimental preview may ship only when:

- generated startup, seed loading, and primary-key lookup work without SQL
- the provider-value-buffer-to-model-`RowData` boundary is correct
- the supported query matrix is small, explicit, and capability-gated
- raw SQL and unsupported queries fail clearly
- strict AOT and browser/WebAssembly smokes pass
- public wording says `experimental` and `read-only`
- documentation explicitly warns that memory is neither SQL semantic proof nor a general replacement for provider-backed integration tests

If any of those conditions fail, cut the preview rather than quietly weakening its architecture.

## Testing Position

The memory backend is useful for:

- testing DataLinq query-plan and materialization behavior inside its documented subset
- fast application tests whose assertions do not depend on provider-specific SQL, collation, type affinity, constraints, or transaction behavior
- examples, demos, and transient browser state

It is not sufficient for:

- SQL translation validation
- migration or schema validation
- provider collation/null/date behavior
- server constraint and concurrency behavior
- transaction, rollback, locking, or durability tests
- deciding that a query works on SQLite, MySQL, or MariaDB

Provider-backed compliance and integration suites remain authoritative for provider behavior.

## Deferred Until After 0.9

The next memory design stage may consider:

1. provider-neutral mutation and transaction boundaries
2. insert/update/delete with trustworthy mutable-instance lifecycle semantics
3. atomic root replacement, rollback, and documented conflict handling
4. constraints, generated values, and relation/index invalidation
5. provider-neutral committed-change receipts or canonical commit batches
6. store forks, reset helpers, and richer fixture APIs
7. persistence integration, commit logs, replay, and compaction
8. broader query capabilities based on demonstrated demand

Those items remain valid design directions. They are not hidden 0.9 stretch goals.

## Claims To Avoid

- "SQL-compatible in-memory database"
- "drop-in replacement for every provider"
- "all LINQ works in memory"
- "default database test replacement"
- "full ACID database"
- "transactional memory database"
- "browser persistence"
- "durable store"

## Links

- [0.9 Implementation Order And Integration Plan](Implementation%20Order%20and%20Integration%20Plan.md)
- [Release Evidence And Closeout Implementation Plan](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md)
- [Memory Backend Design Notes](../../backends/memory/README.md)
- [Memory Backend Architecture](../../backends/memory/Architecture.md)
- [0.9 Memory JSON Snapshot Prototype](Memory%20JSON%20Persistence%20Implementation%20Plan.md)
- [DataLinq 0.9 Roadmap](README.md)
