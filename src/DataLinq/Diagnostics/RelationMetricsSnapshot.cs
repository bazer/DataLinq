using System.Collections.Generic;

namespace DataLinq.Diagnostics;

/// <summary>
/// Relation loading and relation cache metrics.
/// </summary>
/// <param name="ReferenceCacheHits">Total number of foreign-key relation cache hits.</param>
/// <param name="ReferenceLoads">Total number of foreign-key relation loads.</param>
/// <param name="CollectionCacheHits">Total number of relation collection cache hits.</param>
/// <param name="CollectionLoads">Total number of relation collection loads.</param>
public readonly record struct RelationMetricsSnapshot(
    long ReferenceCacheHits,
    long ReferenceLoads,
    long CollectionCacheHits,
    long CollectionLoads)
{
    internal static RelationMetricsSnapshot Sum(IEnumerable<RelationMetricsSnapshot> snapshots)
    {
        long referenceCacheHits = 0;
        long referenceLoads = 0;
        long collectionCacheHits = 0;
        long collectionLoads = 0;

        foreach (var snapshot in snapshots)
        {
            referenceCacheHits += snapshot.ReferenceCacheHits;
            referenceLoads += snapshot.ReferenceLoads;
            collectionCacheHits += snapshot.CollectionCacheHits;
            collectionLoads += snapshot.CollectionLoads;
        }

        return new RelationMetricsSnapshot(
            ReferenceCacheHits: referenceCacheHits,
            ReferenceLoads: referenceLoads,
            CollectionCacheHits: collectionCacheHits,
            CollectionLoads: collectionLoads);
    }
}
