> [!WARNING]
> This document is incubating product-planning material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store Incubation Plan

**Status:** Draft incubation plan.

## Purpose

DataLinq.Store is a proposed companion project that uses DataLinq's generated model and cache foundations to provide a client state manager for .NET, Blazor, browser WebAssembly, and JavaScript applications.

The long-term expectation is that this becomes its own repository. During planning, the documents live here so the design can stay aligned with DataLinq's generated metadata, cache invalidation, result-set caching, AOT, and WebAssembly work.

The product thesis is:

> DataLinq.Store should synchronize server-authorized state modules into a client state graph, then keep client views fresh through explicit module snapshots, patches, and invalidation messages.

This is intentionally not "Redux in C#." The valuable shape is a relational state manager:

- typed state modules
- state nodes and edges
- optional provider-key identity
- optional opaque client keys
- generated metadata
- immutable snapshots
- transactional client updates
- derived module queries
- server-authorized state sync
- coarse JavaScript and Blazor integration
- generated server, C# client, and JS/TS bindings

## Design Stance

The client should not treat database rows or opaque server cache objects as the source of truth. Server result caches validate computed state. DataLinq.Store should sync developer-defined state modules.

A state module is a versioned, queryable, syncable graph projection over DataLinq data. It defines which fields, keys, and relations may leave the server.

```text
Module:
  ProjectWorkspace(project:42)

Nodes:
  ProjectHeader:p_42
  TaskCard:t_1
  TaskCard:t_2
  UserChip:u_10

Edges:
  ProjectHeader:p_42.tasks => [TaskCard:t_1, TaskCard:t_2]
  TaskCard:t_1.assignee => UserChip:u_10
```

If a developer wants one module to represent the whole client state, that is valid. The important part is that even the whole-state module is explicit, versioned, authorized, and serializable.

## Relationship To Existing DataLinq Plans

DataLinq.Store depends on several DataLinq concepts that are either shipped or already planned:

- generated model metadata and generated factories
- provider-key identity and row cache behavior
- explicit cache invalidation envelopes
- dependency-tracked result-set caching
- distributed cache coordination and CDC concepts
- AOT and WebAssembly compatibility work

The most relevant existing planning documents are:

- [Accepted High-Level Decisions](Accepted%20High-Level%20Decisions.md)
- [Store Contract IR and Module Authoring Model](Store%20Contract%20IR%20and%20Module%20Authoring%20Model.md)
- [State Modules and Graph Cache](State%20Modules%20and%20Graph%20Cache.md)
- [Security and Authorization Model](Security%20and%20Authorization%20Model.md)
- [Identity, Versioning, and Protocol Compatibility](Identity%20Versioning%20and%20Protocol%20Compatibility.md)
- [Module Paging, Lifetimes, and Retention](Module%20Paging%20Lifetimes%20and%20Retention.md)
- [Server Subscription and Module Cache Architecture](Server%20Subscription%20and%20Module%20Cache%20Architecture.md)
- [Mutation and Invalidation Loop](Mutation%20and%20Invalidation%20Loop.md)
- [API and Binding Generation](API%20and%20Binding%20Generation.md)
- [Result set caching](../query-and-runtime/Result%20set%20caching.md)
- [Phase 16: Dependency-Tracked Result-Set Caching](../roadmap-implementation/phase-16-dependency-tracked-result-set-caching/README.md)
- [Distributed Cache Coordination and CDC](../architecture/Distributed%20Cache%20Coordination%20and%20CDC.md)
- [Practical AOT and Size Plan](../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md)

The accepted decisions currently include a single Store Contract IR, contract-first APIs, analyzable module definitions, separated modules/selectors/commands/client actions, module-level authorization only, module-scoped graph storage first, online-first stale hydration, full module replacement before incremental patches, first-class paged edges, generated C# clients as the primary client surface, explicit compatibility failure, application-owned command handlers, and security before reuse. The server-side cache stance is similarly conservative: subscriptions are lightweight, module snapshots are shared and evictable, and persistent server snapshot caches are optional adapters rather than correctness infrastructure.

DataLinq.Store should not broaden DataLinq's current AOT or WebAssembly support claims by implication. The Store design should define its own constrained-runtime support boundary and prove it with dedicated smoke projects later.

## Package Shape

The eventual package/repository shape should stay split by responsibility:

```text
DataLinq.Store
  Client state-module graph engine.

DataLinq.Store.Generators
  Store Contract IR generation plus generated module descriptors, accessors, node descriptors, edge descriptors, query descriptors, and serializer hints.

DataLinq.Store.Blazor
  Blazor service registration, component notification helpers, and renderer-friendly subscription APIs.

DataLinq.Store.Js
  Browser WebAssembly JS export facade and TypeScript declaration generation.

DataLinq.Store.Bindings.AspNetCore
  Generated ASP.NET endpoint adapters for Store modules and commands.

DataLinq.Store.Bindings.TypeScript
  Generated JavaScript wrappers and TypeScript declarations for the browser facade.

DataLinq.Sync.Abstractions
  Protocol contracts for module snapshots, patches, invalidation, subscriptions, freshness tokens, and schema versions.

DataLinq.Sync.Server.AspNetCore
  Server module subscription hub, authorization hooks, transport hosting, and DataLinq result-cache integration.

DataLinq.Sync.Client
  .NET client transport and reconnection logic.

DataLinq.Store.Persistence.IndexedDb
  Browser persistence adapter for hydrated client state.

DataLinq.Store.Persistence.Sqlite
  Optional SQLite/OPFS adapter after the in-memory store and sync contract are proven.
```

The names can change. The boundaries should not. Store is local state. Sync is server coordination. Persistence is optional storage.

## Core Requirements

The minimum credible Store runtime needs:

- module graph storage
- node storage by module/node type/key
- edge storage by module/source/edge name
- immutable read snapshots
- command-based mutations
- optimistic overlays
- transactional patch application
- module impact analysis
- generated C# client proxy
- generated JS/TS facade
- module-level and node-level subscriptions
- stale/loading/error state per subscription
- generated model metadata consumption
- generated module metadata consumption
- deterministic serialization and hydration
- authorization-aware module visibility
- lazy/paged edge loading
- retention and persistence policy
- diagnostics for module count, node count, edge count, subscription count, patch volume, and invalidation behavior

The minimum credible Sync layer needs:

- named module subscriptions
- stable module identifiers
- module version negotiation
- parameter serialization and hashing
- server authorization per subscription
- schema/model version negotiation
- protocol compatibility checks
- module snapshot messages
- module patch messages
- module invalidation messages
- command status messages
- generated server endpoint adapters
- reconnect and resync behavior
- explicit ordering and freshness semantics

## Non-Goals

Do not make the first version responsible for:

- arbitrary client-provided LINQ expression execution on the server
- shipping full database rows to the client by default
- transparent distributed consistency
- replacing the database as the source of truth
- full offline conflict-free replication
- one-row-at-a-time lazy loading as the default large-edge strategy
- SQLite/OPFS persistence as a required baseline
- a general-purpose JavaScript state manager for tiny apps
- query syntax that hides unsupported server behavior

The database remains authoritative. Server DataLinq queries and module definitions remain authoritative for access control and state shape. The client store is a synchronized local module graph.

## Product Expectations

DataLinq.Store should be judged against state-management expectations, not ORM expectations.

It should make these workflows boring:

- subscribe to a server-authorized module
- render the current module snapshot
- dispatch a command
- apply an optimistic overlay
- apply server patches transactionally
- update every affected local view from one module patch
- reconcile server acknowledgments
- invalidate and refetch when precision is unavailable
- hydrate after reload
- expose stable state to Blazor and JavaScript
- call server modules and commands through generated bindings

It should also make failures explicit:

- schema mismatch
- authorization revoked
- missed event gap
- stale module
- unsupported module query shape
- transport reconnect
- optimistic mutation rejected
- command conflict
- persisted state rejected
- schema or protocol incompatibility
- lazy edge fetch failed

## AOT And WebAssembly Position

DataLinq.Store should be designed as AOT-friendly from day one:

- no `Reflection.Emit`
- no hot-path `Expression.Compile()`
- no runtime model discovery in the supported browser path
- no arbitrary client query provider in the supported browser path
- no per-row reflection
- source-generated accessors and metadata for the supported path

Browser WebAssembly should be a first-class target, but the first Store proof should not require SQLite. In-memory synchronized state is the smallest useful proof. SQLite/OPFS belongs in a later persistence adapter.

## Recommended Incubation Order

1. Define the product vocabulary: store, module, node, edge, graph, command, overlay, snapshot, patch, subscription, freshness token.
2. Define the Store Contract IR and the supported module authoring subset.
3. Define the sync protocol DTOs independently of transport.
4. Design security, authorization, identity, versioning, and compatibility rules.
5. Design lazy/paged module edge loading and retention policy.
6. Design lightweight server subscriptions and shared evictable module cache behavior.
7. Design the client module graph store and patch application semantics.
8. Design the command, optimistic overlay, and module invalidation loop.
9. Design the contract-first API and binding generation layer.
10. Design server named-module subscriptions over DataLinq result-cache concepts.
11. Define AOT and WebAssembly constraints for the supported client path.
12. Build a small in-memory proof against generated test models.
13. Add Blazor integration.
14. Add browser WebAssembly JavaScript facade.
15. Add hydration/persistence after the runtime semantics are stable.
16. Only then evaluate SQLite/OPFS and broader local querying.

## Exit Criteria For Planning

This incubation plan is ready to move toward implementation when:

- the protocol messages are specified enough to write compatibility tests
- Store Contract IR semantics are specified enough that modules, commands, bindings, serializers, and TypeScript are generated from one descriptor model
- client store semantics distinguish modules, nodes, and edges
- server subscription semantics distinguish module snapshots, patches, and invalidations
- mutation semantics distinguish client commands, optimistic overlays, server transactions, and authoritative module patches
- binding semantics distinguish Store contracts, generated server adapters, generated C# clients, generated WASM exports, and generated JS/TS wrappers
- authorization, identity, schema-version, and protocol-compatibility boundaries are explicit
- lazy edge and retention policies avoid both giant module snapshots and one-row-at-a-time fetches
- server subscription and module-cache semantics keep idle clients lightweight and allow subscribed module cache entries to be evicted
- AOT/browser unsupported behavior is named up front
- the first demo scope is small enough to finish without building distributed sync infrastructure first

## Open Questions

- Should the first implementation live as projects in the DataLinq solution, or should DataLinq.Store get a separate repo immediately after the protocol is drafted?
- Should query descriptors be generated from attributed methods, source-generated static classes, or explicit registration code?
- Should the first transport be SignalR, raw WebSocket, Server-Sent Events, or in-process test transport?
- How much local querying should the browser store support over loaded module graphs before it asks the server for another module?
- Should TypeScript declarations be generated from DataLinq metadata in the first browser proof, or deferred?
