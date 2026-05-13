> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Precise Relation Cache Invalidation

**Status:** Implemented for Phase 11 Workstream B on 2026-05-13; retained as design rationale.

## Purpose

Phase 11 cannot treat "relation cache invalidation" as only `IndexCache` maintenance.

There are two different cache layers involved in relation traversal:

- relation index caches, which map relation-side key values to related primary keys
- loaded relation objects, such as `ImmutableRelation<T>` and `ImmutableForeignKey<T>`, which hold materialized immutable instances after a relation property is accessed

Phase 10 left useful provider-key hooks for row and index removal, but loaded relation objects still subscribe to a blunt table-change notification. Any committed mutation on a table can clear every loaded relation object for that table, even when only one relation bucket or one loaded row instance is affected.

That is correct but too aggressive. Phase 11 should make it precise before exposing public external invalidation APIs, because the first public surface will otherwise bake in over-broad behavior.

## Current Code Shape

The current relation-object invalidation path is table-scoped:

1. `ImmutableRelation<T, TKey>.LoadValues()` subscribes the relation object to changes on the target table.
2. `ImmutableForeignKey<T, TKey>.GetInstance()` does the same for reference relations.
3. `TableCache.ApplyCommittedChanges(...)` removes rows and some index entries, then calls `OnRowChanged()` without an impact payload.
4. `CacheNotificationManager.Notify()` calls `Clear()` on every live subscriber for that table, filtered only by transaction scope.

This is the source of the broad invalidation behavior. The index cache has more precise hooks than the relation-object notification layer does.

## Correctness Rule

A loaded relation object is stale if either of these is true:

- its membership may have changed
- any immutable row instance currently held by the relation object may have changed

The second condition is easy to miss and is the reason relation invalidation cannot be based only on changed foreign-key buckets.

Example:

```text
account.CreatedInvoices loads invoice 100
invoice 100 Number changes, but CreatedByAccountId stays 1
account.CreatedInvoices still has the old immutable invoice instance
```

The relation membership is unchanged, but the relation value is stale. That relation object must be cleared. Clearing every relation object on `runtime_invoices` is unnecessary; clearing the relation objects that contain invoice `100` is enough.

## Target Model

Phase 11 should introduce an internal invalidation impact model that can describe:

- table-wide fallback invalidation
- changed primary keys for rows whose cached immutable instance may be stale or deleted
- changed relation keys for relation buckets whose membership may have changed

Sketch:

```csharp
internal readonly record struct RelationCacheKey(
    ColumnIndex Index,
    DataLinqKey ProviderKey);

internal sealed record CacheInvalidationImpact(
    bool ClearTable,
    IReadOnlySet<DataLinqKey> ChangedPrimaryKeys,
    IReadOnlySet<RelationCacheKey> ChangedRelationKeys);
```

The exact shape can differ, but it must distinguish:

- `ColumnIndex`, because multiple foreign keys can point to the same table with the same scalar key value
- provider-key components, because Phase 11 must not revive `IKey`
- primary-key row identity, because row value changes make relation objects stale even when membership is stable

## Subscription Shape

Loaded relation subscriptions should carry enough metadata to match against an invalidation impact:

```csharp
internal sealed record RelationCacheSubscription(
    WeakReference<ICacheNotification> Subscriber,
    Transaction? Transaction,
    RelationCacheKey? RelationKey,
    ImmutableHashSet<DataLinqKey> LoadedPrimaryKeys);
```

For a collection relation:

- `RelationKey` is the relation-side index plus the parent key used to load the collection
- `LoadedPrimaryKeys` is the set of target-row primary keys materialized into the collection

For a reference relation:

- if it resolves through target primary-key lookup, `LoadedPrimaryKeys` contains that target primary key when non-null
- if it resolves through a non-primary relation index, `RelationKey` should identify that lookup bucket as well

For legacy or dynamic paths where the runtime cannot confidently compute this metadata, it should subscribe as table-wide and accept broad clearing.

## Matching Rule

A subscriber should be cleared when:

```text
impact.ClearTable
or subscription.RelationKey is in impact.ChangedRelationKeys
or subscription.LoadedPrimaryKeys intersects impact.ChangedPrimaryKeys
```

That gives the desired behavior:

- updating a non-key column clears only relation objects that actually loaded the changed row
- moving a foreign key clears old and new membership buckets when old/new values are known
- deleting a row clears relation objects that contain the row and the deleted row's relation buckets
- inserting a row clears the new relation bucket, but does not clear unrelated relation objects for the same table
- duplicate same-target foreign keys remain distinct because `ColumnIndex` participates in `RelationCacheKey`

## Local Mutation Impact

For local mutations, DataLinq has model metadata and mutable state. Phase 11 should normalize local changes into the same impact model used by manual and external invalidation.

Rules:

- **Insert**
  - add inserted primary key to `ChangedPrimaryKeys` if known
  - add relation keys for each relation/index value present on the inserted row
- **Update**
  - add the row primary key to `ChangedPrimaryKeys`
  - for each changed relation/index column, add old and new relation keys when both can be known
  - if old relation/index values cannot be known, downgrade the affected index or table to a conservative fallback
- **Delete**
  - add deleted primary key to `ChangedPrimaryKeys`
  - add relation keys for relation/index values present on the deleted row

The uncomfortable part is old values. The current mutation surface exposes changed new values, but Phase 11 should add or derive a mutation/invalidation fact object that can carry old and new provider values. External invalidation needs that anyway.

## External Invalidation Impact

External invalidation events should map to the same impact model:

- primary-key row invalidation always adds `ChangedPrimaryKeys`
- old/new index values add precise `ChangedRelationKeys`
- missing old relation/index values should clear the affected index or table rather than pretending precision exists
- unknown tables or key type mismatches should fail clearly or use a documented conservative fallback

This is the same principle as the CDC architecture plan: over-eviction is a performance cost; stale relation objects are a correctness bug.

## Implementation Tasks

1. Add an internal impact representation that can express table fallback, changed primary keys, and changed relation keys.
2. Extend `CacheNotificationManager` with targeted subscription and targeted notify paths.
3. Keep the existing table-wide `ICacheNotification.Clear()` behavior as the conservative fallback.
4. Update `ImmutableRelation<T, TKey>` to subscribe with its relation key and loaded target primary keys after loading values.
5. Update `ImmutableForeignKey<T, TKey>` to subscribe with loaded target primary key information, and relation-key information when the reference lookup is relation-index based.
6. Normalize local `StateChange` mutation effects into the impact model before notifying subscribers.
7. Reuse the same impact model from the public/manual/external invalidation API.
8. Record telemetry for precise relation-object notification versus table-wide fallback.

## Required Tests

Add tests for:

- updating a non-relation column clears only loaded relation objects that contain the changed row
- updating `CreatedByAccountId` from account `1` to account `2` clears the old and new `CreatedInvoices` relations, not unrelated `ApprovedInvoices` relations
- updating `ApprovedByAccountId` does not clear `CreatedInvoices` for the same account id
- inserting a child row clears only the relation bucket that should gain the row
- deleting a child row clears relation objects that contain that row
- reference relation cache clears when the referenced parent row changes
- table-wide fallback still clears all subscribers when old/new relation values are unavailable
- external invalidation uses the same matching behavior as local mutation invalidation

The previous broad-clear characterization was converted into `Cache_UnchangedForeignKeyUpdate_ClearsRelationCollectionsContainingChangedRows` so it now describes the improved behavior rather than preserving the old table-wide behavior as an acceptance target.

## Exit Criteria

This design is implemented when:

- relation-object invalidation is no longer table-wide for precise local mutation impacts
- public/manual/external invalidation can reuse the same relation-impact matching path
- table-wide clearing remains available as the explicit correctness fallback
- duplicate foreign keys to the same target table do not invalidate each other accidentally
- relation traversal after invalidation never returns a known-stale immutable row instance
