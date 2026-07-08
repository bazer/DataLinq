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

### 0.9 Backend And Provider-Value Roadmap

The next draft roadmap is 0.9. Its theme is to make DataLinq query plans backend-executable, make provider values first-class, and then prove that architecture outside the SQL renderer.

The useful 0.9 shape is:

- backend-neutral query execution and capability diagnostics
- query-plan template/invocation separation for repeated query shapes
- scalar converters and typed-ID support through provider-value normalization
- bounded SQL-backed multi-join and grouped-query continuation
- a generated-model memory backend that executes `DataLinqQueryPlan` directly
- memory mutation, deterministic test utility, and provider-value commit batches
- experimental JSON persistence for memory stores through DataLinq-owned snapshot and optional commit-log formats

The key discipline is not to turn every good idea into a 0.9 claim. JSON persistence is storage for `DataLinq.Memory`, not a JSON query backend. Typed IDs are scalar converters over single provider values, not arbitrary value-object query translation. Multi-join and grouped-query work should extend the source-slot query plan, not imply general LINQ support.

### Query Plan and Remotion Removal

The 0.8 parser-removal track is implemented. The production query boundary is now DataLinq-owned:

- `Queryable<T>` roots use `ExpressionQueryPlanProvider`
- `ExpressionQueryPlanParser` parses supported `System.Linq.Expressions` trees into `DataLinqQueryPlan`
- `QueryPlanSqlBuilder` renders accepted predicates, ordering, paging, scalar result shapes, relation-existence predicates, grouped aggregate rows, implicit singular relation joins, and supported explicit/query-syntax join shapes from that plan
- direct source-slot projections can execute as SQL-backed projection rows, while computed projections execute after materialization through DataLinq projection binding
- `Remotion.Linq` is no longer a main product runtime dependency

That is not the same thing as a general LINQ-provider rewrite. The support boundary is still the documented tested subset, and unsupported shapes should fail with specific `QueryTranslationException` diagnostics instead of falling back to silent client-side filtering.

The detailed implementation phase record is intentionally not duplicated here. Use the changelog for release boundaries and `docs/dev-plans` for internal phase history.

### Query Composition, Grouped Aggregates, and Join Continuation

The 0.8 query-runtime slices implemented query composition, SQL-shaped grouped aggregate rows, source-slot joins, implicit singular relation traversal, SQL-backed projection rows, single C# query-syntax inner joins, and joined post-paging pushdown.

The next honest query work is narrower than "all joins":

- multiple explicit inner joins
- filtering, ordering, paging, and result operators over supported multi-join row shapes
- grouping over supported multi-join source-slot projection rows
- provider-value normalized join keys, including typed IDs where scalar converters are configured
- relation-aware `JoinBy(...)` and `JoinMany(...)` only after explicit multi-join composition is stable
- left joins only as a later/stretch claim with real nullability semantics

Materialized `IGrouping<TKey,TElement>`, `GroupJoin(...)`, opaque transparent identifiers, hidden collection expansion, and broad client fallback should remain unsupported unless specific tests and docs say otherwise.

### Scalar Converters and Typed Keys

The cache and metadata layers now distinguish provider-key identity from model-facing values. That makes scalar converters a 0.9 foundation, not just a later ergonomic feature:

- explicit converter metadata
- model-to-provider normalization for reads, writes, query constants, local sequences, keys, joins, relations, memory rows, mutation values, and JSON payloads
- typed-ID equality, local membership, primary-key lookup, relation lookup, and explicit join keys
- schema validation based on provider storage types, not only model CLR types
- clear rejection of unsupported value-object member queries

## Later Work

Dependency-tracked result-set caching remains deferred until provider-value normalization, joins, projection semantics, invalidation, freshness vocabulary, and DataLinq.Store module contracts are stronger. A cached result-set feature without a boring correctness story would be clever in the worst way.

Full migration execution also remains future work. `validate` and `diff` are real product features today; `add-migration`, `update-database`, migration history tracking, and runtime migration APIs are not. SQL JSON path querying, broad DI/hosting integration, generated typed-key output, and production-grade JSON persistence also need their own evidence before they become public claims.
