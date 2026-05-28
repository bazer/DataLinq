> [!WARNING]
> This document is incubating architecture material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store Module Paging, Lifetimes, And Retention

**Status:** Draft specification.

## Purpose

DataLinq.Store should be lazy by default, but not naive.

The core rule is:

> Fetch module graph state when the client needs it, but make large edges explicit so laziness does not become one server roundtrip per row.

A module can represent a full screen or even a whole app state, but it should not force every relation and collection to be fully hydrated. Large module edges need policies for paging, windowing, prefetching, retention, and eviction.

## Module Loading Policies

Module root policy:

```text
Eager
  Load the module root snapshot immediately on subscription.

Lazy
  Define the module contract but do not fetch until requested.

HydrateFromPersistence
  Show persisted snapshot first, then validate/refetch.

ServerPushed
  Server pushes when an external event makes the module relevant.
```

The first implementation should support eager subscription and persisted hydration. Fully lazy module definitions can come later.

## Edge Loading Policies

Edges need their own policies.

```text
EagerEdge
  Included in the module snapshot.

LazyEdge
  Loaded only when requested.

PagedEdge
  Loaded in pages with stable ordering.

WindowedEdge
  Loaded around a cursor/range, useful for timelines and virtualized lists.

CountOnlyEdge
  Provides count without item hydration.

SummaryEdge
  Provides aggregate/summary nodes before details are fetched.
```

Example:

```text
ProjectWorkspace
  project: eager
  taskColumns: eager
  visibleTasks: paged, page size 100
  auditLog: lazy windowed
  comments: count-only until opened
```

This prevents the false choice between tiny modules and enormous modules.

## Avoiding One-By-One Fetches

Lazy loading must be batch-aware.

Rules:

- edge fetches should request pages/fragments, not individual rows by default
- N nearby edge requests should coalesce into one server request when possible
- generated clients should expose collection/page APIs, not row-at-a-time APIs
- UI virtualization should request windows, not loop over item fetches
- server should support fragment refetch for declared lazy edges

Bad:

```text
for each task id:
  fetch task details
```

Good:

```text
fetch TaskDetails edge window:
  project = p_42
  cursor = visible range
  count = 100
```

## Prefetching

Prefetching should be explicit or policy-driven.

Possible policies:

- prefetch first page with module root
- prefetch next page when scroll position reaches threshold
- prefetch related summary nodes
- prefetch after idle
- prefetch only on fast connection
- disable prefetch on low-memory devices

Prefetch must respect authorization and retention budgets.

## Lifetimes

Module graph state needs lifetime rules.

State categories:

```text
Active
  Has active UI subscribers.

Warm
  Recently used, no active subscribers.

Persisted
  Stored for hydration after reload.

Stale
  Known invalid or unverified.

Pinned
  Application requested retention.

Evictable
  Can be removed under memory/age/size policy.
```

Subscription disposal should move modules from active to warm, not necessarily delete them immediately.

## Retention Budgets

The client store should expose retention budgets:

- max modules
- max nodes
- max edges
- max serialized bytes
- max estimated memory bytes
- max optimistic overlay age
- max stale age
- max warm module age
- persistence quota budget

When budgets are exceeded, eviction should be deterministic and diagnosable.

Eviction order can start simple:

1. expired stale modules
2. unpinned warm modules by least-recently-used
3. persisted-only modules
4. large lazy edges
5. active modules only under explicit emergency policy

## Persistence

Persisted modules need metadata:

- module id
- module version
- parameter hash
- schema hash
- authorization context stamp
- snapshot sequence
- persisted time
- stale/validated state
- payload size

On startup:

1. load persisted metadata
2. reject schema-incompatible modules
3. reject authorization-context mismatch
4. hydrate allowed modules as stale or pending validation
5. validate/refetch before showing when policy requires fresh data

Persistence is a convenience and performance feature. It is not authority.

## Multi-Tab Behavior

Multi-tab behavior needs a plan before production browser use.

Options:

```text
Independent tabs
  Each tab owns subscriptions. Simple, more server load.

Leader tab
  One tab owns server sync and broadcasts patches to other tabs.

Shared persistence only
  Tabs do not live-sync, but hydrate from the same IndexedDB state.
```

The first implementation can use independent tabs. The architecture should leave room for leader election through `BroadcastChannel` or another browser coordination mechanism.

## Backpressure

Backpressure rules:

- coalesce repeated invalidations for the same module
- collapse patch queues into full snapshot refetch when too far behind
- throttle UI notifications
- batch server messages per transaction
- pause prefetch under high load
- drop warm module subscriptions before active ones
- prefer refetch over replay when replay is too expensive

If the client cannot keep up, correctness requires refetch or invalidation, not applying an unbounded stale queue.

## Testing Strategy

Minimum tests:

- eager module loads once
- lazy edge does not load until requested
- paged edge fetches a page, not individual rows
- nearby page requests coalesce when configured
- stale module hydrates from persistence and then refetches
- schema mismatch rejects persisted module
- authorization mismatch rejects persisted module
- retention evicts warm modules before active modules
- invalidation coalesces repeated module invalidations
- patch backlog can fall back to full refetch
- multi-tab independent mode does not corrupt persisted state

These are mostly normal .NET tests against the client store. Browser tests should cover IndexedDB, multi-tab coordination, and visibility/lifecycle behavior.

## Developer Ergonomics

Module authors should be able to declare edge policy near the module definition.

Conceptual sketch:

```csharp
builder.Collection(
    "visibleTasks",
    ProjectQueries.VisibleTasks(db, projectId),
    edge => edge
        .Paged(pageSize: 100)
        .PrefetchFirstPage()
        .RetainWarm(TimeSpan.FromMinutes(5)));
```

The exact API can change. The requirement is that laziness, paging, and retention are part of the module contract, not scattered through UI code.

## Open Questions

- Should eager edges have a hard max item count by default?
- Should lazy edge fetches be commands, queries, or a separate protocol message family?
- Should retention budgets be global, per module type, or per authorization context?
- Should persisted modules default to stale-visible or stale-hidden?
- Should multi-tab leader mode be part of V1 or deferred?
- How should the client estimate memory for graph nodes and edges in browser WASM?
- Should backpressure decisions be client-owned, server-owned, or negotiated?
