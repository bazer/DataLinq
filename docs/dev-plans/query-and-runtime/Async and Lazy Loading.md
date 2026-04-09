> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: Async and Lazy Loading

**Status:** Draft  
**Goal:** Introduce real async I/O support and define how lazy loading should behave without turning DataLinq into a magical, hard-to-reason-about API.

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
