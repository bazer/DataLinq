# DataLinq Roadmap

This page is the public roadmap snapshot. It describes direction, not shipped behavior. For current product behavior, use the usage docs, support matrices, and changelog.

## Current 0.7.1 Baseline

DataLinq is currently a source-generated, immutable-first ORM for MySQL, MariaDB, and SQLite. The stable public shape is:

- generated immutable and mutable model classes
- cache-aware reads and relation traversal
- explicit mutation and transaction workflows
- schema validation through `datalinq validate`
- conservative schema diff scripts through `datalinq diff`
- a documented LINQ subset with tests behind the support matrix
- explicit cache clearing and external invalidation APIs
- estimated cache-memory accounting and memory-pressure cleanup on supported runtimes
- a narrow generated SQLite Native AOT, trimmed publish, and Blazor WebAssembly AOT smoke boundary

The important non-claims are just as important:

- DataLinq does not ship full migration execution yet.
- DataLinq does not translate arbitrary LINQ.
- DataLinq is not broadly AOT-compatible across every provider and query shape.
- DataLinq does not ship distributed CDC or message-bus integrations.
- SQLite browser/WebAssembly support is limited to the documented generated AOT smoke path.

For release-level detail, see the [changelog](../CHANGELOG.md).

## Near-Term Direction

### Query Plan and Remotion Isolation

The 0.8 branch pulls the query-parser boundary forward as the next major theme:

- introduce a DataLinq-owned query plan behind the current Remotion parser
- move SQL generation and query diagnostics behind that plan
- build a supported-subset expression parser over `System.Linq.Expressions`
- dual-run parser parity against the documented LINQ support matrix
- remove `Remotion.Linq` from the main product dependency graph

This should not become a general LINQ-provider rewrite. The parser should target the documented supported subset first, preserve current tested behavior where practical, and reject unsupported shapes with specific diagnostics.

The internal execution plan starts over at 0.8 Phase 1 instead of continuing the old global roadmap numbering. That keeps the release work sequential: baseline the query contract, add the plan and Remotion adapter, move SQL generation, add the new parser, prove parity, then remove Remotion as a dependency.

### Explicit Multi-Join Composition

After the query plan exists, the next broad query priority is standard explicit inner-join composition:

- C# query-syntax joins as a documented path
- multiple explicit inner joins
- filtering, ordering, paging, and result operators over joined row shapes
- joined materialization that keeps using provider-key components

The first shipped join support is intentionally narrow. The next step is to make explicit joins useful without hiding complexity behind relation-aware syntax too early.

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
