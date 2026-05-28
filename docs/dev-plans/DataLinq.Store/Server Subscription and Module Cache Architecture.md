> [!WARNING]
> This document is incubating architecture material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store Server Subscription And Module Cache Architecture

**Status:** Draft specification.

## Purpose

The server must support many subscribed clients without keeping one full module graph per client in memory.

The core rule is:

> Server subscriptions are lightweight. Module snapshots are shared, bounded, evictable cache entries. Correctness comes from the database plus sequence, schema, and authorization metadata, not from retaining every subscribed client's module graph.

An idle client that is not sending or receiving meaningful data should cost roughly:

- connection or transport state
- subscription ids
- module identity
- parameter hash
- authorization context stamp
- last acknowledged sequence
- heartbeat/reconnect metadata

It should not require:

- a full materialized module graph per client
- a per-client dependency tree
- an unbounded per-client patch queue
- a non-evictable per-client snapshot

## Architecture Layers

The server-side model should have four separate layers:

```text
Subscription registry
  Lightweight connection -> module instance mapping.

Shared module cache
  Bounded, evictable snapshots keyed by module identity.

Change/invalidation log
  Ordered committed events used for replay/refetch decisions.

Module builder
  Rebuilds any authorized module snapshot from database state.
```

Keeping these separate prevents module cache retention from becoming part of correctness.

## Subscription Registry

The subscription registry stores lightweight records:

```text
ConnectionId
SubscriptionId
DatabaseId
ModuleId
ModuleVersion
ParameterHash
AuthorizationContextStamp
ClientSchemaHash
LastAcknowledgedSequence
TransportState
```

The registry should not need a full module snapshot to keep a subscription alive.

If the server evicts a module cache entry while clients are still subscribed, the subscription remains valid. The next meaningful event can rebuild, replace, refetch, or invalidate the module.

## Shared Module Cache

The module cache is an optimization.

Cache key:

```text
DatabaseId
ModuleId
ModuleVersion
ParameterHash
AuthorizationContextStamp
SchemaHash
```

Cache value:

- materialized module snapshot, serialized snapshot, or both
- dependency fingerprint
- sequence/freshness token
- size estimate
- last access time
- subscriber count estimate
- rebuild cost estimate when available

The cache is:

- shared by equivalent subscribers
- bounded by size and age
- evictable even while subscribed
- invalidated by dependency/freshness changes
- never trusted across authorization boundaries unless proven safe

## Eviction While Subscribed

Eviction is allowed while clients remain subscribed.

On the next relevant event:

```text
Cache entry exists and fresh
  -> use it for patch/replacement decision

Cache entry missing
  -> rebuild module if needed
  -> or send ModuleInvalidate/Refetch

Cache entry stale
  -> rebuild or invalidate
```

This prevents idle clients from pinning memory indefinitely.

## Persistent Server Snapshot Cache

Persistent server snapshot cache can be useful, but it is optional.

It should be treated as a cache adapter, not correctness infrastructure.

Potential adapters:

```text
DataLinq.Store.Server.Cache.Memory
DataLinq.Store.Server.Cache.Redis
DataLinq.Store.Server.Cache.Disk
```

Disk snapshot caching may help when:

- modules are expensive to compute
- snapshots are large and reusable
- server restarts are frequent
- authorization context is stable
- schema/version invalidation is robust

Disk snapshot caching is risky when:

- authorization changes often
- schema changes often
- snapshots contain sensitive data
- encryption at rest is required
- disk pressure is common
- serialized payload churn is high
- stale cache cleanup is weak

The first implementation should use memory only. Disk cache can come later as an explicit opt-in adapter.

## Restart And Reconnect

Server restart should not require restoring every module cache entry.

Reconnect flow:

1. client reconnects with known module subscriptions
2. client sends schema hash and last acknowledged sequences
3. server validates schema/protocol/module compatibility
4. server checks whether replay is possible
5. server replays patches, sends replacement snapshots, or invalidates/refetches

Server responses:

```text
ReplayPatches
  The server has enough event history and the module contract still matches.

SendReplacementSnapshot
  The server can rebuild the module from the database.

ModuleInvalidate
  The client must mark stale or request a fresh module snapshot.

SchemaMismatch
  The client must discard persisted state and reload or refresh generated assets.
```

If the server cannot prove continuity, it should replace or invalidate. It should not guess.

## Change And Invalidation Log

A bounded change/invalidation log helps reconnect and replay.

The log should store enough to answer:

- what changed?
- in which commit sequence?
- which module identities may be affected?
- is replay still available for this client sequence?
- did a gap force refetch?

The log can start in memory. Durable logs, outbox integration, Redis Streams, Kafka, or database-backed sequence storage can come later.

The log is not a replacement for the database. It is a replay/refetch aid.

## Backpressure

Never hold an unbounded queue per client.

Backpressure behavior:

```text
Small backlog
  Replay patches.

Large backlog
  Collapse to replacement snapshot.

Replay gap
  Invalidate/refetch.

Schema mismatch
  Reject and require reload.

Slow client
  Coalesce invalidations and prefer replacement snapshot.
```

Backpressure should be diagnosable. A client falling behind should produce metrics, not silent memory growth.

## Fanout

Equivalent subscribers should share work.

Example:

```text
100 clients subscribe to ProjectWorkspace(project:42)
1 module cache entry for the shared authorization context
100 lightweight subscription records
1 recomputation per relevant change
1 encoded snapshot/patch payload reused where possible
```

Authorization may prevent sharing. That is acceptable. Security beats reuse.

## Server Memory Budgets

Server-side budgets should include:

- max cached module snapshots
- max cached serialized bytes
- max cached materialized bytes
- max dependency fingerprint bytes
- max replay log events
- max per-client pending messages
- max subscription records per connection
- max rebuild concurrency

When budgets are exceeded:

1. evict stale module snapshots
2. evict cold module snapshots
3. evict large serialized payloads before metadata if useful
4. collapse pending patch queues into replacement snapshots
5. reject or throttle new subscriptions only as a last resort

## Correctness Invariants

- Losing the module cache must not corrupt correctness.
- Losing the replay log may force refetch, not stale patching.
- Server restart may force replacement snapshots or invalidations.
- A subscribed client does not pin module cache memory.
- Disk cache entries are never authoritative.
- Patches are only sent when sequence continuity and module compatibility are proven.

## Diagnostics

Server diagnostics should include:

- active connections
- active subscriptions
- subscriptions by module id
- module cache entries
- module cache hits/misses/evictions
- cache entries evicted while subscribed
- snapshot rebuild count and duration
- serialized snapshot bytes
- replay log depth
- replay success/failure
- backpressure collapses to snapshot
- per-client pending message counts
- reconnect decisions: replay, replace, invalidate, schema mismatch
- disk/Redis cache adapter hits and failures when configured

## First Useful Slice

The first implementation should be deliberately simple:

1. lightweight in-memory subscription registry
2. bounded in-memory shared module cache
3. evictable module snapshots even while subscribed
4. no disk snapshot cache
5. no durable replay log
6. reconnect validates schema and sends replacement snapshots when needed
7. mutation path sends full replacement snapshots or invalidations
8. basic diagnostics for subscriptions, cache hits, evictions, and rebuilds

Durable logs, Redis/shared cache, disk snapshot cache, and patch replay windows should wait until the basic rebuild/refetch model is solid.

## Open Questions

- Should module cache entries store materialized graphs, serialized payloads, or both in V1?
- Should equivalent subscribers share encoded payload bytes directly?
- Should replay log sequence be global per server, per database, or per module instance?
- Should server restart always force full replacement snapshots in V1?
- What server-side cache adapter interface is needed for future Redis/disk implementations?
- Should cache eviction prefer removing serialized payloads before dependency fingerprints?
- Should slow clients be disconnected after repeated backpressure collapses?
