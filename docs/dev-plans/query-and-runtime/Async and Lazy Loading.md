> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: Async and Lazy Loading

**Status:** Accepted.
**Release horizon:** DataLinq 0.10 for native async/cancellation; lazy-loading experiments remain later work.
**Last reviewed:** 2026-08-30.
**Dependency:** The shipped 0.9 execution foundation provides the backend/source boundary; the 0.10 release-local plan owns the exact async surface and evidence.
**Goal:** Introduce real async I/O support and define how lazy loading should behave without turning DataLinq into a magical, hard-to-reason-about API.

## Accepted 0.10 API Decisions

The release-local [Async Public API Decisions](../roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md) record is authoritative for the accepted public surface and remaining questions:

- `Transaction()` remains synchronous and performs no database I/O; the first operation requiring database access initializes through its chosen sync/async path.
- The same transaction supports sequential sync and async operations. `await using` selects asynchronous disposal, not asynchronous construction.
- Public async cancellation tokens are optional and last; local query construction and composition remain synchronous.
- Generated single-reference relation methods use the property name plus `Async`, for example `DepartmentAsync()`, without a `Load` prefix or synchronous getter evaluation. Collection handles stay synchronous (`employee.Salaries`) and expose async execution such as `FirstOrDefaultAsync`, `GetAsync`, and `ToListAsync`; no generated `SalariesAsync()` is planned.
- Collection relations keep their existing `IEnumerable<T>` surface. Relation query composition is excluded from 0.10 and retained separately in the unscheduled [Relation-Scoped Queries](Relation-Scoped%20Queries.md) backlog proposal.
- Existing synchronous lazy navigation remains supported for compatibility. Awaitable entities, task-valued navigation properties, and new hidden I/O mechanisms are outside 0.10.
- Async query terminals use familiar names such as `ToListAsync` and `ToArrayAsync`. Key lookups, direct single-reference loading, query/relation terminals, relation collection accessors, and disposal use `ValueTask`, aligning LINQ terminal results with framework async LINQ. Prepared scalar/row execution, mutations, transaction completion/callbacks, and other non-LINQ awaitable operations use `Task`, with generic result types as appropriate. This supersedes the earlier task-returning terminal proposal. Prepared sequence execution returns `IAsyncEnumerable<T>` directly.
- The approved 0.10 breaking correction removes the keyed `AsEnumerable()` instance member: standard LINQ `relation.AsEnumerable()` yields rows without loading at the call, while `AsKeyValuePairs()` retains explicit primary-key/row enumeration and may load synchronously. Update pair consumers and custom implementations and recompile; inferred calls may otherwise silently change element type.
- Use an explicit row-based `AsAsyncEnumerable()` view and local predicate delegates, without adding `IAsyncEnumerable<T>` inheritance or wrapping arbitrary synchronous iterators. `ValuesAsync`, `KeysAsync`, `ContainsKeyAsync`, and `ToFrozenDictionaryAsync` preserve their synchronous result shapes inside `ValueTask`.
- Standard async LINQ comes from the framework on .NET 10 and a conditional transitive `System.Linq.AsyncEnumerable` dependency on .NET 8/9. Package references are added with async-surface implementation and verified with packed consumers on all three targets.
- Relation async execution belongs on `IImmutableRelation<T>`, with overridable shared defaults over genuine async execution where semantics permit. Built-in concrete relations and testing helpers must expose the intended caller surface; unsupported custom implementations cannot silently fall back to synchronous database I/O.
- Generated single-reference async methods are callable through the public model base, with overridable implementations and focused collision diagnostics. Do not automatically add them to model interfaces also used by mutable models.
- Correct required-reference behavior in both synchronous and asynchronous navigation: required missing targets fail clearly, optional missing targets return `null`, and duplicate targets fail cardinality checks. This is an approved 0.10 compatibility correction, not a change to nullable key-lookup misses. Test helpers must obey the same rules.
- `AsAsyncEnumerable()` and prepared-sequence `ExecuteAsync(...)` return `IAsyncEnumerable<T>` without a task wrapper or universal streaming guarantee. Relation enumeration may buffer; explicit materializers return completed collections with owned readers already closed.
- Sequence and enumerator construction perform no DataLinq database I/O; first movement may execute. Ordinary query parameters bind at each `GetAsyncEnumerator()`, prepared invocation arguments bind at the `ExecuteAsync(...)` call, and materializing terminals capture bound parameters before their first suspension. Parameter snapshots do not freeze database contents.
- Sequential repeated enumeration is supported without permanent result caching: ordinary queries capture current parameters again, one prepared invocation retains its bound arguments, and relations may reuse a valid cache or reload. Retain a materialized collection for stable collection reuse.
- Honor both optional method tokens and `.WithCancellation(...)`; if different tokens are supplied, either can request cancellation. Observe cancellation during buffered iteration as well as loading, and release owned linked-token resources.
- Enumerators dispose owned execution resources on all exits, including early `break`, without committing or disposing a caller-owned transaction. Reject another execution operation while a reader remains active on that transaction. Preserve existing validated later relation transitions to committed reads, but never migrate an active reader between sources.
- Exact primitive/overload inventories and diagnostic types still require review. Cancellation/failure and cleanup precedence, initialization recovery, and wider transaction/cache coordination remain open in the decision record.

The broader options and phases below retain design rationale. They do not authorize interface-first model rewrites, strict sync-I/O modes, preload/batching features, hollow instances, or awaitable-entity experiments in 0.10. Provider APIs use native async where available; the documented Microsoft.Data.Sqlite synchronous-driver limitation is an explicit exception, not a promise of nonblocking SQLite I/O.

## 1. Why This Matters

DataLinq currently leans heavily into synchronous access patterns. That is workable for some local or cache-heavy scenarios, but it becomes a real limitation in modern server code and UI environments where blocking I/O is a bad fit.

The problem is not just "add `Async` suffixes everywhere."

The real problem is that relation access and lazy loading live right on the boundary between:

- convenient object navigation
- hidden network or disk I/O
- cache behavior
- N+1 query risk
- WebAssembly and UI-thread constraints

That boundary needs to be designed carefully, not papered over with clever syntax.

## 2. What Looks Solid

The previous async discussion produced a few ideas that are directionally strong.

### 2.1. Native Async Provider Pipeline

This part is not controversial. DataLinq should support native async provider execution end to end.

That means:

- provider APIs should have real async methods
- database access should use async ADO.NET calls where available
- async support should not be implemented by wrapping sync I/O in `Task.Run` or `.Result`

This is real engineering work, not syntactic decoration.

### 2.2. Interface-First Public Surface

Returning interfaces from generated models and relations has real advantages:

- easier mocking and testability
- less coupling to generated concrete types
- better separation between public shape and internal ORM machinery

This is attractive, but it also increases generator surface area and API complexity, so it should be adopted deliberately rather than romantically.

### 2.3. Hollow vs. Hydrated Instances

The idea of a lightweight identity-bearing instance that can later hydrate itself is plausible.

Used carefully, it could support:

- cheap relation placeholders
- cache-first relation traversal
- more controlled lazy-loading behavior

But this is only useful if the loading semantics are explicit enough that users still understand when I/O can happen.

## 3. What I Do Not Fully Buy Yet

### 3.1. Awaitable Entities Are Clever, But Risky

The idea of making entity instances awaitable is technically possible and genuinely clever.

It is also the kind of cleverness that can age badly.

Why:

- it hides an important behavior behind language sugar
- it makes entities feel partly like values and partly like asynchronous operations
- it can confuse debugging and code review because the I/O boundary is no longer obvious

This should be treated as an experiment, not as an immediate architectural commitment.

### 3.2. Sync Property Access That Triggers I/O Is Dangerous

Allowing `employee.Department.Name` to block and load implicitly is convenient, but it is also where ORM behavior turns from helpful to sneaky.

That pattern is especially risky in:

- ASP.NET request paths
- high-throughput services
- UI-thread contexts where blocking is toxic

If DataLinq supports sync fallback lazy loading, it should be:

- configurable
- observable
- easy to disable
- clearly treated as a compatibility/convenience path rather than the preferred model

### 3.3. Lazy Loading Is Not the Right Primary Fix for N+1

Lazy loading can be made less bad with batching and cache awareness.

That still does not make it the best default answer.

The primary answer to N+1 should remain explicit loading strategies such as:

- eager loading
- includes/preloads
- batch-aware relation loading

Lazy loading is a secondary convenience feature, not the main architectural victory.

## 4. Recommended Direction

The right direction is more conservative than the original "awaitable entity" proposal.

### 4.1. Make Async First-Class at the Query and Mutation Layer

DataLinq should have explicit async APIs for:

- query execution
- relation loading
- mutation and transaction flows

This is the safe, unsurprising part of the design.

### 4.2. Keep Sync/Async Boundaries Honest

If sync property access can trigger loading, DataLinq should expose a clear policy for that behavior.

Suggested policy:

- allow it where the environment is known and acceptable
- support a strict mode such as `ThrowOnSyncIo`
- add counters/logging so hidden sync loads are visible during development

### 4.3. Treat Awaitable Entities as a Design Spike

If the awaitable-entity idea is explored, it should be explored as a narrow experiment with explicit acceptance criteria:

- does it remain understandable in normal application code?
- does it create debugging or API confusion?
- does it actually outperform or out-ergonomics a simpler explicit method?

If the answer is not clearly yes, it should be dropped.

## 5. Recommended Roadmap

### Phase 1: Async Provider Foundations

1. Add async provider and access interfaces.
2. Implement native async execution paths in SQLite and MySQL/MariaDB providers.
3. Add explicit async query and mutation APIs.

### Phase 2: Observability and Safety

1. Add counters for sync lazy loads, async lazy loads, batched relation loads, and cache-assisted relation hits.
2. Add strict-mode protection for accidental sync I/O in sensitive environments.
3. Verify behavior in tests before expanding the public surface.

### Phase 3: Explicit Loading Improvements

1. Improve preload/include-style APIs.
2. Add batch-aware relation loading strategies.
3. Use those mechanisms as the main N+1 mitigation story.

### Phase 4: Experimental Lazy Loading Layer

1. Prototype hollow instances.
2. Evaluate awaitable relations or entities if still justified.
3. Only commit to the more magical API if the costs are defensibly low.

## 6. Bottom Line

Async support is important.

Native async providers and explicit async APIs are the obvious good part.

Awaitable entities and implicit blocking property loads are the dangerous part.

So the correct plan is:

- build the async foundation
- improve explicit loading first
- treat the clever lazy-loading model as experimental until it proves itself
