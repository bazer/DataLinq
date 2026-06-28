# DataLinq Roadmap

This page is the public roadmap snapshot. It describes direction, not shipped behavior. For current product behavior, use the usage docs, support matrices, and changelog.

## Current 0.8 Development Baseline

This page tracks the current repo documentation branch. If you are comparing against an already-published NuGet version, check the [changelog](../CHANGELOG.md) for the exact release boundary.

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
- a narrow generated SQLite Native AOT, trimmed publish, and Blazor WebAssembly AOT smoke boundary
- runtime package dependency groups without Roslyn/compiler assemblies or `Remotion.Linq`

The important non-claims are just as important:

- DataLinq does not ship full migration execution yet.
- DataLinq does not translate arbitrary LINQ.
- DataLinq is not broadly AOT-compatible across every provider and query shape.
- DataLinq does not ship distributed CDC or message-bus integrations.
- SQLite browser/WebAssembly support is limited to the documented generated AOT smoke path.

For release-level detail, see the [changelog](../CHANGELOG.md).

## Near-Term Direction

### 0.8 AOT Browser Release Hardening

The parser-removal track is implemented. The remaining 0.8 release priority is making the constrained-platform story real enough to ship:

- automate Blazor WebAssembly AOT browser smoke evidence instead of relying only on publish output
- resolve SQLitePCLRaw WebAssembly varargs warning disposition with exact call-path evidence
- re-test no-AOT browser behavior and either support it narrowly or keep it explicitly unsupported
- expand constrained-platform query coverage across the documented subset selected for 0.8
- keep AOT routes fenced away from reflection-heavy compatibility fallback
- keep Native AOT, trimmed, WASM, and WASM AOT payload reports green with sensible deploy-size thresholds

The target release claim should stay narrow: generated SQLite models, the documented query subset, Native AOT, trimmed publish, and Blazor WebAssembly AOT. Broad provider coverage, arbitrary LINQ, OPFS storage, and no-AOT browser support are separate claims unless they get their own evidence.

### Query Plan and Remotion Removal

The 0.8 parser-removal track is implemented in the current branch. The production query boundary is now DataLinq-owned:

- `Queryable<T>` roots use `ExpressionQueryPlanProvider`
- `ExpressionQueryPlanParser` parses supported `System.Linq.Expressions` trees into `DataLinqQueryPlan`
- `QueryPlanSqlBuilder` renders accepted predicates, ordering, paging, scalar result shapes, relation-existence predicates, and the narrow explicit join baseline from that plan
- row-local projections execute after materialization through DataLinq projection binding
- `Remotion.Linq` is no longer a main product runtime dependency

That is not the same thing as a general LINQ-provider rewrite. The support boundary is still the documented tested subset, and unsupported shapes should fail with specific `QueryTranslationException` diagnostics instead of falling back to silent client-side filtering.

The internal 0.8 execution record started over at Phase 1 instead of continuing the old global roadmap numbering. That sequence is now closed through Phase 7: query contract baseline, temporary Remotion adapter, SQL generation on `DataLinqQueryPlan`, supported-subset expression parser, projection/local-evaluation cleanup, parity and constrained-platform switch, and Remotion dependency removal. Phases 8 through 12 now own the AOT/browser release gates.

### Post-AOT Join Composition

Now that the query plan exists, the next broad query feature priority after the AOT/browser release gates is standard explicit inner-join composition:

- C# query-syntax joins as a documented path
- multiple explicit inner joins
- filtering, ordering, paging, and result operators over joined row shapes
- joined materialization that keeps using provider-key components

The first shipped join support is intentionally narrow. The next step is to make explicit joins useful without hiding complexity behind relation-aware syntax too early, but not before the 0.8 browser AOT story is release-grade.

### Relation-Aware Joins and Left Joins

After explicit joins are stronger, relation metadata can become a safer query-building input:

- `JoinBy(...)` and `JoinMany(...)`
- join-local predicates
- left joins with honest nullability behavior
- clear documentation for `ON` versus `WHERE` semantics

This should build on the explicit join engine, not replace it with a magical relation API.

### Scalar Converters and Typed Keys

The cache and metadata layers now distinguish provider-key identity from model-facing values. That gives scalar converters a credible place to land later:

- explicit converter metadata
- model-to-provider normalization for reads, writes, query constants, keys, joins, and relations
- typed-ID equality and membership queries
- schema validation based on provider storage types, not only model CLR types

## Later Work

Dependency-tracked result-set caching remains deferred until joins, projection semantics, invalidation, and freshness vocabulary are stronger. A cached result-set feature without a boring correctness story would be clever in the worst way.

Full migration execution also remains future work. `validate` and `diff` are real product features today; `add-migration`, `update-database`, migration history tracking, and runtime migration APIs are not.
