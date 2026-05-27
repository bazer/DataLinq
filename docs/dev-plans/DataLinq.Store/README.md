> [!WARNING]
> This document is incubating product-planning material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store Incubation Plan

**Status:** Draft incubation plan.

## Purpose

DataLinq.Store is a proposed companion project that uses DataLinq's generated model and cache foundations to provide a client state manager for .NET, Blazor, browser WebAssembly, and JavaScript applications.

The long-term expectation is that this becomes its own repository. During planning, the documents live here so the design can stay aligned with DataLinq's generated metadata, cache invalidation, result-set caching, AOT, and WebAssembly work.

The product thesis is:

> DataLinq.Store should synchronize server-authorized DataLinq result sets into a normalized client state store, then keep client views fresh through explicit snapshots, patches, and invalidation messages.

This is intentionally not "Redux in C#." The valuable shape is a relational state manager:

- typed tables
- provider-key identity
- generated metadata
- immutable snapshots
- transactional client updates
- derived query/view subscriptions
- server-authorized result-set sync
- coarse JavaScript and Blazor integration

## Design Stance

The client should not treat server cache objects as the source of truth. Server result caches are computed views. The client should store normalized rows and separate query-result memberships.

Preferred client state shape:

```text
Rows:
  Employee[1]
  Employee[2]
  Department[4]

Result views:
  employeesByDepartment(4) => [Employee:1, Employee:2]
  activeEmployees => [Employee:1]
```

This lets one row update refresh every subscribed view that contains that row, without duplicating the same entity across unrelated result-set objects.

## Relationship To Existing DataLinq Plans

DataLinq.Store depends on several DataLinq concepts that are either shipped or already planned:

- generated model metadata and generated factories
- provider-key identity and row cache behavior
- explicit cache invalidation envelopes
- dependency-tracked result-set caching
- distributed cache coordination and CDC concepts
- AOT and WebAssembly compatibility work

The most relevant existing planning documents are:

- [Result set caching](../query-and-runtime/Result%20set%20caching.md)
- [Phase 16: Dependency-Tracked Result-Set Caching](../roadmap-implementation/phase-16-dependency-tracked-result-set-caching/README.md)
- [Distributed Cache Coordination and CDC](../architecture/Distributed%20Cache%20Coordination%20and%20CDC.md)
- [Practical AOT and Size Plan](../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md)

DataLinq.Store should not broaden DataLinq's current AOT or WebAssembly support claims by implication. The Store design should define its own constrained-runtime support boundary and prove it with dedicated smoke projects later.

## Package Shape

The eventual package/repository shape should stay split by responsibility:

```text
DataLinq.Store
  Client normalized state engine.

DataLinq.Store.Generators
  Generated registries, accessors, table descriptors, query descriptors, and serializer hints.

DataLinq.Store.Blazor
  Blazor service registration, component notification helpers, and renderer-friendly subscription APIs.

DataLinq.Store.Js
  Browser WebAssembly JS export facade and TypeScript declaration generation.

DataLinq.Sync.Abstractions
  Protocol contracts for snapshots, patches, invalidation, subscriptions, freshness tokens, and schema versions.

DataLinq.Sync.Server.AspNetCore
  Server subscription hub, authorization hooks, transport hosting, and DataLinq result-cache integration.

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

- normalized row storage by table and primary key
- immutable read snapshots
- transactional patch application
- query-result membership tracking
- table-level and query-level subscriptions
- stale/loading/error state per subscription
- generated model metadata consumption
- deterministic serialization and hydration
- diagnostics for row count, subscription count, patch volume, and invalidation behavior

The minimum credible Sync layer needs:

- named query subscriptions
- stable query identifiers
- parameter serialization and hashing
- server authorization per subscription
- schema/model version negotiation
- snapshot messages
- patch messages
- invalidation messages
- reconnect and resync behavior
- explicit ordering and freshness semantics

## Non-Goals

Do not make the first version responsible for:

- arbitrary client-provided LINQ expression execution on the server
- transparent distributed consistency
- replacing the database as the source of truth
- full offline conflict-free replication
- SQLite/OPFS persistence as a required baseline
- a general-purpose JavaScript state manager for tiny apps
- query syntax that hides unsupported server behavior

The database remains authoritative. Server DataLinq queries remain authoritative for access control and query semantics. The client store is a synchronized local projection.

## Product Expectations

DataLinq.Store should be judged against state-management expectations, not ORM expectations.

It should make these workflows boring:

- subscribe to a server-authorized result
- render the current snapshot
- apply server patches transactionally
- update every affected local view from one row change
- perform optimistic local mutations
- reconcile server acknowledgments
- invalidate and refetch when precision is unavailable
- hydrate after reload
- expose stable state to Blazor and JavaScript

It should also make failures explicit:

- schema mismatch
- authorization revoked
- missed event gap
- stale query result
- unsupported query shape
- transport reconnect
- optimistic mutation rejected

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

1. Define the product vocabulary: store, table, row, query membership, snapshot, patch, subscription, freshness token.
2. Define the sync protocol DTOs independently of transport.
3. Design the normalized client store and patch application semantics.
4. Design server named-query subscriptions over DataLinq result-cache concepts.
5. Define AOT and WebAssembly constraints for the supported client path.
6. Build a small in-memory proof against generated test models.
7. Add Blazor integration.
8. Add browser WebAssembly JavaScript facade.
9. Add hydration/persistence after the runtime semantics are stable.
10. Only then evaluate SQLite/OPFS and broader local querying.

## Exit Criteria For Planning

This incubation plan is ready to move toward implementation when:

- the protocol messages are specified enough to write compatibility tests
- client store semantics distinguish normalized rows from result memberships
- server subscription semantics distinguish snapshots, patches, and invalidations
- authorization and schema-version boundaries are explicit
- AOT/browser unsupported behavior is named up front
- the first demo scope is small enough to finish without building distributed sync infrastructure first

## Open Questions

- Should the first implementation live as projects in the DataLinq solution, or should DataLinq.Store get a separate repo immediately after the protocol is drafted?
- Should query descriptors be generated from attributed methods, source-generated static classes, or explicit registration code?
- Should the first transport be SignalR, raw WebSocket, Server-Sent Events, or in-process test transport?
- Should optimistic mutations call DataLinq-generated mutation endpoints, or should they use application-defined command handlers?
- How much local querying should the browser store support before it asks the server for a named result?
- Should TypeScript declarations be generated from DataLinq metadata in the first browser proof, or deferred?
