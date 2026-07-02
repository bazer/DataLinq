> [!WARNING]
> This folder contains rough roadmap material for the DataLinq 0.9 development line. It is not normative product documentation, and it should not be treated as a shipped support claim.

# DataLinq 0.9 Rough Roadmap

**Status:** Draft.

**Created:** 2026-07-03.

## Theme

0.8 replaced the production LINQ parser, removed `Remotion.Linq` from the runtime path, hardened the generated SQLite constrained-platform story, and proved that `DataLinqQueryPlan` is a real semantic boundary.

0.9 should test whether that boundary is actually good architecture.

The proposed 0.9 theme is:

> Make DataLinq query plans backend-executable, then prove the design with an in-memory backend and an experimental JSON-backed store.

The important word is "prove". The goal is not to announce broad new production backends before the execution model has earned it. The goal is to force the parser, plan model, value conversion, projection, mutation, cache, and diagnostics layers to survive outside the SQL renderer.

## Opinionated Priority

The strongest 0.9 sequence is:

1. Introduce a backend-neutral query execution boundary.
2. Split query-plan structure from runtime invocation values.
3. Continue decomposing the LINQ parser where the architecture review found real pressure.
4. Build an in-memory backend as the semantic oracle.
5. Add scalar conversion/provider-value infrastructure before JSON gets stringly.
6. Build a JSON backend as a persistence proof, not as a pretend database.

In-memory should come before JSON. It has fewer moving parts and will tell us whether the query plan can be executed without SQL. JSON should come after the value-conversion boundary is clearer, because JSON persistence will immediately expose enum, date/time, nullable, typed-id, and provider/model representation problems.

## Candidate Phases

The exact phase count can change. This is the rough ordering.

### Phase 1: Query Backend Contract

Define the boundary between `DataLinqQueryPlan` and backend execution.

Work should include:

- a backend query executor interface
- backend capability metadata
- explicit capability validation before execution
- shared diagnostics for unsupported backend/query combinations
- a SQL backend adapter around the existing `QueryPlanSqlBuilder`

The SQL path should become one backend implementation, not the implicit center of the query pipeline.

Exit signal:

- existing SQL behavior still passes
- unsupported backend capabilities fail with DataLinq-owned diagnostics
- public docs still describe only existing shipped SQL provider behavior

### Phase 2: Plan Template and Invocation Split

Separate structural query shape from runtime captured values.

0.8 made bindings immutable at the plan boundary. 0.9 should continue that into a cacheable model:

- `QueryPlanTemplate` or equivalent structural plan
- invocation-time scalar and local-sequence values
- stable binding declarations
- cache-key rules that exclude ordinary scalar values
- allocation measurements for repeated query shapes

This phase prepares query-shape caching without needing to ship broad caching behavior immediately.

Exit signal:

- repeated queries can reuse structural plan data in focused tests or prototypes
- runtime captured values remain isolated per execution
- no supported query behavior changes

### Phase 3: Parser Decomposition and Normalization

Split the current parser only where it reduces real complexity.

Candidate seams from the architecture review:

- query method parsing
- source binding and transparent identifiers
- value translation
- predicate translation
- projection translation
- relation traversal planning
- plan normalization and pushdown
- backend capability validation

This should not be a vanity refactor. It should happen alongside Phase 1 and Phase 2 pressure so the extracted components are shaped by real use.

Exit signal:

- parser responsibilities are easier to test in isolation
- backend-specific restrictions move out of expression parsing where possible
- snapshots and compliance tests remain stable

### Phase 4: In-Memory Backend Foundation

Build a real DataLinq in-memory backend, not SQLite in-memory and not a loose fake.

Initial scope should be deliberately narrow:

- generated models only
- metadata-driven tables
- primary-key indexed row storage
- immutable row materialization compatible with existing cache expectations
- basic seeding/loading API for tests
- read-only query execution for a small documented subset

The in-memory backend should execute `DataLinqQueryPlan` directly. If it has to re-parse expressions or route through SQL-shaped strings, the architecture failed.

Exit signal:

- primary-key lookup and simple `Where`/`OrderBy`/`Take`/projection shapes execute without SQL
- behavior can be compared against SQLite in compliance-style tests
- relation traversal and cache interactions have a clear design, even if not complete

### Phase 5: In-Memory Mutation and Test Utility

Decide how far the in-memory backend goes beyond read queries.

Useful scope:

- insert/update/delete against generated mutable models
- transaction-like isolation if practical, or explicit documentation that it is not transactional
- relation/index invalidation behavior
- deterministic test fixtures
- optional testing helpers that are clearly distinct from SQLite in-memory

This phase is where the in-memory backend can become genuinely useful for user tests. It should still be honest about durability and concurrency.

Exit signal:

- users can seed an in-memory database and exercise common read/mutation workflows
- unsupported ACID/durability semantics are documented instead of implied

### Phase 6: Scalar Conversion and Provider Values

Build the conversion boundary JSON will need.

This should cover:

- model value vs provider value representation
- enum conversion
- `DateOnly`, `TimeOnly`, `DateTime`, and nullable value shapes
- typed-id/scalar converter seams
- query constants and local sequences
- mutation values
- cache keys and relation keys

This does not need to ship every typed-id ergonomic dream. It does need to stop backend work from inventing one-off conversion rules.

Exit signal:

- conversion rules are centralized
- SQL and in-memory paths keep passing
- JSON design can use the same conversion boundary

### Phase 7: JSON Store Foundation

Add an experimental JSON-backed store using `System.Text.Json`.

Start with a boring, inspectable storage contract:

- one database directory or file set
- one JSON file per table, unless a better simple layout wins
- deterministic formatting for human review
- generated models only
- primary-key indexed load path
- explicit load/save lifecycle
- AOT-aware serialization strategy where practical

Do not call this a database too early. JSON is a persistence format. Query and mutation semantics still belong to DataLinq.

Exit signal:

- generated model rows can round-trip through JSON
- primary-key lookup works after reload
- errors for malformed JSON, duplicate keys, missing required values, and unknown schema shape are actionable

### Phase 8: JSON Query and Mutation Proof

Execute a subset of `DataLinqQueryPlan` against the JSON store.

Useful scope:

- direct primary-key lookup
- simple filters
- ordering/paging
- direct projection rows
- basic mutation and persistence
- focused parity tests against in-memory and SQLite

This should remain experimental unless concurrency, durability, schema evolution, and performance have enough evidence to support stronger wording.

Exit signal:

- JSON backend proves the backend-neutral query path can persist data
- docs label it accurately as experimental or supported according to evidence
- no SQL support claims are weakened

## Release Boundary

The ideal 0.9 release claim would be narrow:

> DataLinq 0.9 introduces a backend-neutral query execution boundary, an in-memory backend for generated models, and an experimental JSON-backed store that prove the DataLinq query plan outside SQL.

That wording can strengthen only if evidence justifies it.

Possible stronger claims, if earned:

- in-memory backend supports common generated-model read and mutation workflows
- JSON backend supports deterministic local persistence for generated models
- repeated query shapes allocate less through plan-template reuse

Claims to avoid unless proven:

- "full backend abstraction"
- "any backend can run DataLinq queries"
- "JSON database"
- "in-memory database with SQL parity"
- "arbitrary LINQ over JSON"
- "production-grade JSON persistence"

## Cross-Cutting Requirements

- Existing SQLite, MySQL, and MariaDB behavior must stay green.
- Backend-neutral work must not weaken the documented LINQ subset.
- Unsupported backend/query shapes must fail clearly.
- AOT-sensitive code paths should avoid `Expression.Compile()` and broad reflection fallback.
- Benchmarks should distinguish allocation evidence from latency claims.
- Public docs must separate production SQL providers, in-memory testing support, and experimental JSON persistence.

## Explicit Non-Goals

- broad arbitrary LINQ support
- materialized `IGrouping<TKey,TElement>` support
- left joins as part of backend work unless separately planned
- result-set caching as the headline 0.9 feature
- DataLinq.Store execution
- distributed cache coordination
- OPFS/browser JSON storage
- a migration engine for JSON
- replacing SQLite as the constrained-platform proof path

## Open Questions

- Should in-memory use the same public provider surface as SQL providers, or a separate testing-first factory?
- How much transaction behavior should in-memory emulate before it becomes misleading?
- Should JSON use one file per table, one file per database, or a segmented layout?
- How should schema evolution work for JSON rows generated from changing model metadata?
- Should query-plan templates be public diagnostics, internal cache entries, or both?
- Where should backend capability validation live so parser logic stays backend-neutral?

## Likely Follow-Up After 0.9

If 0.9 proves the backend boundary, the next good candidates are:

- dependency-tracked result-set caching
- typed IDs and richer scalar converter ergonomics
- broader relation-aware join APIs
- non-SQL backend experiments beyond JSON
- production-grade JSON persistence only if the experimental store earns it
