> [!WARNING]
> This document is incubating architecture decision material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store Accepted High-Level Decisions

**Status:** Accepted planning decisions.

## Purpose

This document records the high-level decisions that should constrain the DataLinq.Store incubation work.

These are not implementation details. They are the guardrails that keep the first implementation small, defensible, and aligned with the rest of DataLinq.

## 1. Contract-First APIs

**Decision:** DataLinq.Store APIs are defined through Store contracts: modules, commands, and client actions. ASP.NET adapters are generated from those contracts.

Do not start from arbitrary ASP.NET action scraping. Controllers, minimal APIs, SignalR hubs, and WebSocket endpoints can host generated adapters, but they are not the source of truth.

## 2. Single Store Contract IR

**Decision:** Store module, command, client action, binding, serializer, protocol, and TypeScript generation all consume one Store Contract IR.

The generator should parse developer-authored C# into one normalized contract descriptor. Generated artifacts should not independently rediscover module shape or command shape from source symbols.

## 3. Analyzable Module Definitions

**Decision:** State modules are authored in a source-generator-recognized subset. The supported direction is a hybrid of attributed C# module/node types, projection-first roots/collections, and a constrained functional builder for data sources, edges, paging, and policies.

Projection-heavy modules must not require per-field hand mapping in the module declaration. A projected node type can define the contract fields, while the module binds a root or collection to the DataLinq query that materializes that type.

Arbitrary imperative code is allowed in command handlers, query helpers, selectors, and client actions, but not in the contract-critical module shape unless the generator can analyze it.

## 4. Separate Modules, Selectors, Commands, And Client Actions

**Decision:** A module is the server-authorized sync unit. A selector is a client-side query over already-loaded module graph state. A command is a server-authorized mutation request. A client action is C# code running in WASM that orchestrates Store behavior.

A client selector must not silently become a server query.

## 5. Module-Level Authorization Only

**Decision:** Authorization applies to the whole module instance. If the user is authorized, they can receive the complete module contract. If they are not authorized, they receive none of it.

DataLinq.Store will not support field-level, node-level, or edge-level authorization inside one module contract. Different visibility should be modeled as different modules or different module contracts.

This makes module snapshots, patches, generated TypeScript, cache keys, and persisted state dramatically easier to reason about. It also avoids the dangerous middle ground where the system appears to support partial visibility but accidentally leaks hidden graph shape.

## 6. Authorization Is Part Of Module Identity

**Decision:** Module cache identity includes authorization context whenever authorization can affect whether a module instance is visible.

If two users may not both access the same module instance, they must not share the same cached module snapshot unless the module explicitly proves the authorization context is equivalent.

## 7. Module-Scoped Graph Storage First

**Decision:** The first client store keeps graph state module-scoped. Cross-module node deduplication is deferred.

The same underlying database row can appear in multiple modules with different fields, key policies, authorization ownership, lifetimes, and retention policies. Module-scoped storage is less clever and much safer for the first implementation.

## 8. Online-First With Stale Hydration

**Decision:** V1 is online-first with optional stale hydration. Offline command queues are out of scope for the first implementation.

Persisted modules can improve startup and read UX. Mutations require server confirmation. Offline mutation queues bring conflict handling, authorization expiry, durable command replay, and merge semantics; those should not be part of the first slice.

## 9. Full Module Replacement Before Incremental Patches

**Decision:** The first mutation/sync loop supports full module replacement or invalidation/refetch. Incremental field, node, and edge patches come after the coarse loop is correct.

The patch metadata should be shaped so incremental patches can be added later, but the first proof should prefer correctness over clever patch precision.

## 10. Paged Edges Are First-Class

**Decision:** Modules support eager roots and declared lazy, paged, or windowed edges. The runtime must not default to one-row-at-a-time fetching for large graph edges.

Laziness must mean batched fragments, pages, or windows. Accidental network N+1 behavior is not an acceptable default.

## 11. Generated C# Client Is Primary

**Decision:** The generated C# client proxy is the primary client API. Blazor calls it directly. JavaScript and TypeScript call generated wrappers over the WebAssembly C# facade.

This keeps state logic in the generated Store runtime and avoids JS bypassing command status, optimistic overlays, module sequences, and patch/invalidation behavior.

## 12. Explicit Compatibility Failure

**Decision:** Schema, protocol, module, or command version mismatch fails explicitly and early.

The first implementation should not attempt fuzzy compatibility or automatic downgrade. Stale browser bundles, stale persisted modules, and mismatched generated bindings should fail with clear errors and refetch/reload guidance.

## 13. Application-Owned Command Handlers

**Decision:** Commands are Store contracts, but command execution remains application-owned server code.

DataLinq.Store should orchestrate generated bindings, dispatch, validation hooks, command status, mutation impact analysis, and sync. It should not become a business-logic framework.

## 14. Security Beats Reuse

**Decision:** No cache reuse, cross-module node deduplication, persisted hydration, or patch optimization may cross an authorization boundary unless proven safe.

When security and reuse conflict, security wins.

## Consequences

These decisions imply:

- no field-level authorization in generated module contracts
- no independent generators that rediscover Store contract shape separately
- no arbitrary imperative module contract shape in V1
- no projection-heavy authoring model that requires repeated per-field mapping when a projected node type already defines the contract
- no client selector that silently becomes a server query
- no arbitrary ASP.NET action generation as the first API surface
- no cross-module node deduplication in V1
- no offline command queue in V1
- no incremental patch requirement in the first proof
- no one-row-at-a-time lazy edge loading
- no silent schema/protocol compatibility drift

They also make the first useful implementation smaller:

1. define one module contract
2. authorize the whole module instance
3. generate server and C# client bindings
4. generate JS/TS wrappers over the WASM facade
5. subscribe and hydrate a full module snapshot
6. dispatch one command
7. invalidate/refetch or replace the module snapshot after commit
8. reject stale/incompatible clients explicitly
