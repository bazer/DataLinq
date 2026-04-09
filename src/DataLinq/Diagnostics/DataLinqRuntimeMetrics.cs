using System.Threading;

namespace DataLinq.Diagnostics;

public readonly record struct DataLinqRuntimeMetricsSnapshot(
    long EntityQueryExecutions,
    long ScalarQueryExecutions,
    long RowCacheHits,
    long RowCacheMisses,
    long DatabaseRowsLoaded,
    long RowMaterializations,
    long RowCacheStores,
    long RelationReferenceCacheHits,
    long RelationReferenceLoads,
    long RelationCollectionCacheHits,
    long RelationCollectionLoads)
{
    public override string ToString()
        => $"entity-queries={EntityQueryExecutions}, scalar-queries={ScalarQueryExecutions}, " +
           $"row-cache-hits={RowCacheHits}, row-cache-misses={RowCacheMisses}, database-rows={DatabaseRowsLoaded}, " +
           $"materializations={RowMaterializations}, row-cache-stores={RowCacheStores}, " +
           $"relation-ref-hits={RelationReferenceCacheHits}, relation-ref-loads={RelationReferenceLoads}, " +
           $"relation-collection-hits={RelationCollectionCacheHits}, relation-collection-loads={RelationCollectionLoads}";
}

public static class DataLinqRuntimeMetrics
{
    private static long _entityQueryExecutions;
    private static long _scalarQueryExecutions;
    private static long _rowCacheHits;
    private static long _rowCacheMisses;
    private static long _databaseRowsLoaded;
    private static long _rowMaterializations;
    private static long _rowCacheStores;
    private static long _relationReferenceCacheHits;
    private static long _relationReferenceLoads;
    private static long _relationCollectionCacheHits;
    private static long _relationCollectionLoads;

    public static DataLinqRuntimeMetricsSnapshot Snapshot()
        => new(
            EntityQueryExecutions: Interlocked.Read(ref _entityQueryExecutions),
            ScalarQueryExecutions: Interlocked.Read(ref _scalarQueryExecutions),
            RowCacheHits: Interlocked.Read(ref _rowCacheHits),
            RowCacheMisses: Interlocked.Read(ref _rowCacheMisses),
            DatabaseRowsLoaded: Interlocked.Read(ref _databaseRowsLoaded),
            RowMaterializations: Interlocked.Read(ref _rowMaterializations),
            RowCacheStores: Interlocked.Read(ref _rowCacheStores),
            RelationReferenceCacheHits: Interlocked.Read(ref _relationReferenceCacheHits),
            RelationReferenceLoads: Interlocked.Read(ref _relationReferenceLoads),
            RelationCollectionCacheHits: Interlocked.Read(ref _relationCollectionCacheHits),
            RelationCollectionLoads: Interlocked.Read(ref _relationCollectionLoads));

    public static void Reset()
    {
        Interlocked.Exchange(ref _entityQueryExecutions, 0);
        Interlocked.Exchange(ref _scalarQueryExecutions, 0);
        Interlocked.Exchange(ref _rowCacheHits, 0);
        Interlocked.Exchange(ref _rowCacheMisses, 0);
        Interlocked.Exchange(ref _databaseRowsLoaded, 0);
        Interlocked.Exchange(ref _rowMaterializations, 0);
        Interlocked.Exchange(ref _rowCacheStores, 0);
        Interlocked.Exchange(ref _relationReferenceCacheHits, 0);
        Interlocked.Exchange(ref _relationReferenceLoads, 0);
        Interlocked.Exchange(ref _relationCollectionCacheHits, 0);
        Interlocked.Exchange(ref _relationCollectionLoads, 0);
    }

    internal static void RecordEntityQueryExecution() => Interlocked.Increment(ref _entityQueryExecutions);
    internal static void RecordScalarQueryExecution() => Interlocked.Increment(ref _scalarQueryExecutions);
    internal static void RecordRowCacheHits(int count) => Interlocked.Add(ref _rowCacheHits, count);
    internal static void RecordRowCacheMisses(int count) => Interlocked.Add(ref _rowCacheMisses, count);
    internal static void RecordDatabaseRowsLoaded(int count) => Interlocked.Add(ref _databaseRowsLoaded, count);
    internal static void RecordRowMaterialization() => Interlocked.Increment(ref _rowMaterializations);
    internal static void RecordRowCacheStore() => Interlocked.Increment(ref _rowCacheStores);
    internal static void RecordRelationReferenceCacheHit() => Interlocked.Increment(ref _relationReferenceCacheHits);
    internal static void RecordRelationReferenceLoad() => Interlocked.Increment(ref _relationReferenceLoads);
    internal static void RecordRelationCollectionCacheHit() => Interlocked.Increment(ref _relationCollectionCacheHits);
    internal static void RecordRelationCollectionLoad() => Interlocked.Increment(ref _relationCollectionLoads);
}
