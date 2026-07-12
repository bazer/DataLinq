# DataLinq Roadmap

This page is the public roadmap snapshot. It describes direction, not shipped behavior. For current product behavior, use the usage docs, support matrices, and changelog.

## Current Development Baseline

This page tracks the current documentation snapshot. If you are comparing against an already-published NuGet version, check the [changelog](../CHANGELOG.md) for the exact release boundary.

DataLinq is currently a source-generated, immutable-first ORM for MySQL, MariaDB, and SQLite. The current public shape is:

- generated immutable and mutable model classes
- cache-aware reads and relation traversal
- explicit mutation and transaction workflows
- schema validation through `datalinq validate`
- conservative schema diff scripts through `datalinq diff`
- a documented LINQ subset with tests behind the support matrix
- a DataLinq-owned LINQ parser and query plan for the documented subset
- explicit cache clearing and external invalidation APIs
- estimated cache-memory accounting and memory-pressure cleanup on supported runtimes
- a narrow generated SQLite Native AOT, trimmed publish, and Blazor WebAssembly AOT smoke boundary, plus browser WebAssembly gate automation that keeps warning caveats visible
- runtime package dependency groups without Roslyn/compiler assemblies or `Remotion.Linq`

The important non-claims are just as important:

- DataLinq does not ship full migration execution yet.
- DataLinq does not translate arbitrary LINQ.
- DataLinq is not broadly AOT-compatible across every provider and query shape.
- DataLinq does not ship distributed CDC or message-bus integrations.
- SQLite browser/WebAssembly support is limited to the documented generated AOT smoke path.

For release-level detail, see the [changelog](../CHANGELOG.md).

## Near-Term Direction

### 0.9 Provider Values And Pluggable Read Execution

The next planned release is 0.9. Its job is narrower than the original backend-and-persistence proposal:

> Make query execution genuinely backend-selectable, make provider values explicit, and prove both with typed IDs, correct UUID storage, and a small read-only memory backend.

The required 0.9 outcomes are:

- a self-contained query execution request that does not need to rediscover supported projection behavior from the original expression tree
- backend-neutral source, row-loading, materialization, and capability-validation seams, with the existing SQL path adapted through them
- structural query shape separated from invocation values where correctness and backend execution require it, without promising a production query-plan cache
- scalar converters based on a clear model-value to canonical-provider-value boundary
- typed-ID support across SQL reads, writes, keys, relations, query values, and schema validation
- column-specific UUID storage codecs across canonical provider values and provider-specific physical representations
- an experimental read-only `DataLinq.Memory` preview for generated models, with seeding, primary-key lookup, a small documented query subset, and explicit semantics
- Native AOT, browser WebAssembly, package, benchmark, provider-regression, and documentation evidence for the exact release claim
- committed-visibility work for SQLite and trustworthy mutable-instance baseline rules for the existing SQL providers

The memory preview is an architectural proof and a useful transient read store. It is not SQL emulation, a promise that every SQL query behaves identically in memory, or a replacement for provider-backed tests.

Only one optional feature stretch should be selected after the required work is green:

- bounded SQL multi-join/composite-key continuation, or
- manual snapshot-only JSON import/export for memory stores

Neither stretch is part of the baseline release claim.

### Query Plan and Remotion Removal

The 0.8 parser-removal track is implemented. The production query boundary is now DataLinq-owned:

- `Queryable<T>` roots use `ExpressionQueryPlanProvider`
- `ExpressionQueryPlanParser` parses supported `System.Linq.Expressions` trees into `DataLinqQueryPlan`
- `QueryPlanSqlBuilder` renders accepted predicates, ordering, paging, scalar result shapes, relation-existence predicates, grouped aggregate rows, implicit singular relation joins, and supported explicit/query-syntax join shapes from that plan
- direct source-slot projections can execute as SQL-backed projection rows, while computed projections execute after materialization through DataLinq projection binding
- `Remotion.Linq` is no longer a main product runtime dependency

That is not the same thing as a general LINQ-provider rewrite. The support boundary is still the documented tested subset, and unsupported shapes should fail with specific `QueryTranslationException` diagnostics instead of falling back to silent client-side filtering.

The detailed implementation phase record is intentionally not duplicated here. Use the changelog for release boundaries and `docs/dev-plans` for internal phase history.

### Query Composition And Join Continuation

The 0.8 query-runtime slices implemented query composition, SQL-shaped grouped aggregate rows, source-slot joins, implicit singular relation traversal, SQL-backed projection rows, single C# query-syntax inner joins, and joined post-paging pushdown.

The next honest query work is narrower than "all joins" and is optional for 0.9:

- multiple explicit inner joins
- composite anonymous-object join keys over direct provider-normalizable members
- filtering, ordering, paging, and result operators over supported multi-join row shapes
- provider-value normalized join keys, including typed IDs where scalar converters are configured

Narrow `Queryable.LeftJoin(...)` support on .NET 10, grouped multi-join continuation, relation-aware `JoinBy(...)`/`JoinMany(...)`, materialized `IGrouping<TKey,TElement>`, `GroupJoin(...)`, opaque transparent identifiers, hidden collection expansion, and broad client fallback remain later work.

### Scalar Converters and Typed Keys

The cache and metadata layers now distinguish provider-key identity from model-facing values. That makes scalar converters a 0.9 foundation, not just a later ergonomic feature:

- explicit converter metadata
- model-to-canonical-provider normalization for reads, writes, query constants, local sequences, keys, joins, relations, and memory row buffers
- typed-ID equality, local membership, primary-key lookup, relation lookup, and explicit join keys
- schema validation based on provider storage types, not only model CLR types
- clear rejection of unsupported value-object member queries

UUID storage is adjacent but distinct. Scalar conversion maps domain values to a canonical provider CLR value such as `Guid`; a provider codec maps that `Guid` to a column's physical text, native UUID, or binary byte layout. Keeping those layers separate prevents a MySQL byte-order choice from leaking into memory rows, cache identity, or JSON snapshots.

Existing public model-facing `RowData` behavior should remain model-valued. Provider values belong in backend/internal buffers and should be converted when model rows are materialized.

### Correctness Gates

The 0.9 release also closes two existing correctness gaps before adding another mutable or persistent backend:

- DataLinq-owned SQLite paths now enforce committed visibility, with transaction-local state responsible for same-transaction reads, and generated file-backed connections now use private/default cache. Completing the remaining contention and diagnostic evidence remains 0.9 follow-through work.
- mutable instances should be reusable only while their baseline is trustworthy; rollback, failed writes, cross-provider reuse, and cross-transaction reuse need explicit invalidation rules.

These are current SQL-provider correctness requirements, not features invented for `DataLinq.Memory`.

## Later Work

The first post-0.9 priority should be an adoption-focused release: native async query/relation/mutation execution with cancellation, DI/hosting integration, startup schema validation, and testing helpers that clearly distinguish business-logic tests from provider-behavior tests.

Memory mutation, memory transactions, deterministic forks, canonical committed-change batches, JSON flush-on-commit durability, commit logs, replay, compaction, browser persistence adapters, and related CLI commands remain later work. They need a provider-neutral mutation contract and trustworthy transaction semantics first.

Dependency-tracked result-set caching remains deferred until provider-value normalization, joins, projection semantics, invalidation, freshness vocabulary, and DataLinq.Store module contracts are stronger. A cached result-set feature without a boring correctness story would be clever in the worst way.

Full migration execution also remains future work. `validate` and `diff` are real product features today; `add-migration`, `update-database`, migration history tracking, and runtime migration APIs are not. SQL JSON path querying, generated typed-key output, production-grade JSON persistence, distributed coordination, and DataLinq.Store execution also need their own evidence before they become public claims.
