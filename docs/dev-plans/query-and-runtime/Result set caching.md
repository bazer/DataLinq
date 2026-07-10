> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.

# DataLinq: Dependency-Tracked Result And Module Caching

**Status:** Proposed.

**Target:** Unscheduled.

**Last reviewed:** 2026-07-10.

This depends on the implemented invalidation/freshness primitives plus stronger provider-value, projection, module-contract, and committed-change semantics. It is not part of 0.9 and is not current shipped behavior.

## Vision And Principle

Modern applications often have complex, read-heavy views that are expensive to generate on every request. A normal `IMemoryCache` entry with a 5-minute TTL is a crude compromise: the data can be stale for nearly 5 minutes, and the system recomputes even when nothing changed.

DataLinq's result-cache principle should be:

> Cached state should be based on data validity, not arbitrary timers.

The original Phase 16 design described stamped DTOs and view models. That mechanism is still useful, but the more concrete product direction is now state modules:

> A state module is a developer-defined, versioned, queryable, syncable graph projection over DataLinq data.

For DataLinq.Store, a cached result is primarily a module snapshot. The server can validate, serialize, patch, invalidate, and sync that module snapshot. The client can hydrate, query, render, and update the same module graph.

Generic stamped application results can remain a lower-level escape hatch, but module snapshots should be the main design target because they define:

- which fields are exposed
- which relations are exposed
- which keys are safe for the client
- which dependencies make the result stale
- how the result is serialized
- how the result can be patched

## Developer Workflow

The developer should define a named state module or explicit cached computation boundary.

For a module:

1. **Define the module contract.**
   The module definition names the module, version, parameters, node types, fields, edges, key policy, and authorization policy.

2. **Check for a valid cached module snapshot.**
   Before recomputing, the server asks the module cache whether a snapshot for the module identity, version, parameters, and authorization context is still valid.

3. **Use the cached snapshot or recompute.**
   If DataLinq can prove the cached module is fresh, the server can return or sync it immediately. If it cannot prove freshness, the module is recomputed.

4. **Enter a tracking scope.**
   Recomputing a module happens through a read-tracking scope. The scope records the rows, tables, invalidation generations, and optional provider tokens used to build the module.

5. **Build the module graph.**
   The module builder projects database data into module nodes and edges. It does not blindly ship database rows.

6. **Stamp the module snapshot.**
   The completed snapshot gets a dependency fingerprint and freshness token.

7. **Store or sync the snapshot.**
   The server can store it in the module cache, return it to a request, or sync it to a DataLinq.Store client.

For a non-module application result, the same tracking and fingerprinting infrastructure can stamp a DTO or view model. That should be considered a compatibility/general-purpose path, not the primary Store sync shape.

## Core Concepts

### Tracking Scope

A tracking scope is a disposable, read-only data source that records what was read while a module or computed result was built.

It should record enough information to later answer:

- which tables were touched
- which provider keys were read
- which relation/index paths were used
- which invalidation generation or freshness marker existed at read time
- whether a dependency was tracked precisely or conservatively

### Dependency Fingerprint

When a tracking scope completes, the observed dependencies become a dependency fingerprint.

At minimum, a fingerprint contains:

1. table identity
2. provider-key identity when precise row tracking is possible
3. freshness marker at read time
4. conservative dependency markers when exact row tracking is not possible

The marker format is deliberately not fixed. It could be an invalidation generation, a provider version token, a provider-specific hash, a commit sequence, or a combination. Row hashing is an optional precision tool, not a hard prerequisite for the first slice.

### State Module

A state module is a versioned graph projection over DataLinq data.

It defines:

- module id
- module version
- parameter contract
- authorization policy
- node types
- node fields
- client key policy
- edges between node types
- serialization shape
- patch behavior
- local query surface

The DataLinq.Store module details live in [State Modules and Graph Cache](../DataLinq.Store/State%20Modules%20and%20Graph%20Cache.md).

### Module Snapshot

A module snapshot is a materialized module graph at a particular freshness sequence.

It contains:

- module id and version
- parameter hash
- schema/model version
- nodes
- edges
- sequence or freshness token
- dependency fingerprint

The snapshot is the natural cache value for server module caching and the natural sync value for client hydration.

### Module Patch

A module patch is a transactional change against a known module sequence.

It may include:

- node upserts
- node removals
- field updates
- edge replacements
- edge insertions
- edge removals
- edge reordering
- stale markers

The first implementation can avoid incremental patches and send full replacement snapshots after invalidation. Patch precision should be earned, not guessed.

### Validation Check

Validation takes a dependency fingerprint and checks it against current invalidation or row-state markers.

The check should avoid reloading full rows. If any dependency can no longer be proven fresh, the result is invalid.

For module snapshots, invalid means one of:

- recompute the module and send a replacement snapshot
- send `ModuleInvalidate` and let the client refetch
- later, produce a precise `ModulePatch` when the changed dependencies and module shape make that safe

## Module Cache Keys

Module cache keys need to be explicit. A credible key includes:

- database id
- module id
- module version
- parameter hash
- schema/model version
- authorization context stamp when required
- optional tenant/session scope

Authorization context is dangerous. If permissions affect module shape or fields, the cache key must reflect that. A shared cached module that ignores authorization is a data leak wearing a performance hat.

## Dependencies On Current Cache And Query Foundations

This feature is a natural evolution of the cache, invalidation, and query-shape work behind Phase 16.

1. **Phase 11 invalidation and freshness vocabulary**
   Result validation needs explicit database/table/provider-key invalidation APIs, invalidation envelopes, and shared freshness terms.

2. **Phase 12 cache-footprint accounting and cleanup behavior**
   Module snapshots and dependency fingerprints must report and bound their own overhead honestly. Do not pretend graph metadata or serialized snapshot bytes are free.

3. **Phase 13 and Phase 14 join semantics**
   Dependency tracking has to understand joined rows, relation-aware joins, left-join nullability, and projection boundaries. Otherwise it will either over-invalidate everything or under-track the rows that actually make a module stale.

4. **Projection and view semantics**
   A result cache is only credible when the computation boundary is explicit. State modules provide that boundary for client-visible state.

5. **DataLinq.Store state modules**
   Store sync gives Phase 16 a concrete output shape: module snapshots, patches, and invalidations.

## Non-Goals

Phase 16 should not claim:

- transparent caching of arbitrary LINQ result sets
- automatic distributed cache coherence
- full row replication to clients
- arbitrary browser-defined query execution
- provider-specific CDC clients
- replacement of application caches such as `IMemoryCache`
- patch precision before dependency precision exists

The safe first behavior is invalidate and refetch. Over-invalidation is acceptable. Under-invalidation is a correctness bug.

## Recommended First Slice

The first useful implementation should be small:

1. define dependency fingerprint DTOs
2. add an explicit read-tracking scope
3. define module snapshot cache metadata
4. validate module snapshots against invalidation state
5. recompute and replace full module snapshots when invalid
6. report diagnostics for tracked dependencies and validation results
7. benchmark validation overhead against simple recomputation

Incremental module patches should come after full replacement snapshots are boring.

## Diagnostics

Diagnostics should explain:

- which module was cached
- which dependency scope was tracked
- whether tracking was precise or conservative
- why validation passed or failed
- whether invalidation came from row, rows, table, or database scope
- snapshot size and estimated dependency metadata size
- cache hit, miss, validation-hit, validation-fail, recompute, and eviction counts

If a cached result cannot explain why it is fresh, it is not a trustworthy cache. It is just a stale-data generator with better branding.
