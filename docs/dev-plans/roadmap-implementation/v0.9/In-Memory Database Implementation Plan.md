> [!WARNING]
> This document is roadmap implementation material for the DataLinq 0.9 development line. It is not normative product documentation and should not be treated as a shipped support claim.

# 0.9 Read-Only Memory Backend Implementation Plan

**Status:** Accepted.

**Created:** 2026-07-03.

**Reframed:** 2026-07-10.

**Target:** DataLinq 0.9 experimental preview.

**F7/W8 spike progress (2026-07-13):** The separate non-packable runtime and TUnit projects now exist. The first internal checkpoint stores dense `CanonicalProviderValueRow` instances behind per-table primary-key ordinal maps, keeps materialized immutable identities in separate existing `RowCache` instances, executes direct neutral primary-key lookup plus one pass-through root entity plan, and rejects an unsupported `Where` before memory enumeration. Canonical seed publication and source-local identity are proven, while a shared SQL/memory registry lock serializes resolution through generated binding; a gated unit test proves a concurrent caller cannot observe the winner before binding completes, and a 32-way cold start proves convergence on that graph. The seed API remains internal and provider-valued. Focused evidence is `6/6`; the integrated unit and generator gates pass `1129/1129` and `57/57`. The runtime builds for net8/net9/net10 and its package closure has no SQL provider or native database dependency. This is not `M0` or `M1` completion: model-valued seed conversion, the required query subset, documented semantics, parity, typed IDs/`Guid`, public API/package design, concurrent cache maintenance, and constrained-runtime smokes remain open.

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
