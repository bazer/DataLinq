using System.Collections.Generic;

namespace DataLinq.Cache;

internal readonly record struct CacheMemoryEstimate(
    long RowPayloadBytes = 0,
    long RowStoreOverheadBytes = 0,
    long TransactionRowPayloadBytes = 0,
    long TransactionRowStoreOverheadBytes = 0,
    long IndexPayloadBytes = 0,
    long IndexOverheadBytes = 0,
    long RelationObjectBytes = 0,
    long NotificationBytes = 0,
    long SnapshotBytes = 0)
{
    public long EstimatedCacheBytes
    {
        get
        {
            var total = CacheMemoryEstimator.SaturatingAdd(RowPayloadBytes, RowStoreOverheadBytes);
            total = CacheMemoryEstimator.SaturatingAdd(total, TransactionRowPayloadBytes);
            total = CacheMemoryEstimator.SaturatingAdd(total, TransactionRowStoreOverheadBytes);
            total = CacheMemoryEstimator.SaturatingAdd(total, IndexPayloadBytes);
            total = CacheMemoryEstimator.SaturatingAdd(total, IndexOverheadBytes);
            total = CacheMemoryEstimator.SaturatingAdd(total, RelationObjectBytes);
            total = CacheMemoryEstimator.SaturatingAdd(total, NotificationBytes);
            return CacheMemoryEstimator.SaturatingAdd(total, SnapshotBytes);
        }
    }

    public static CacheMemoryEstimate Empty => default;

    public static CacheMemoryEstimate operator +(CacheMemoryEstimate left, CacheMemoryEstimate right)
        => new(
            RowPayloadBytes: CacheMemoryEstimator.SaturatingAdd(left.RowPayloadBytes, right.RowPayloadBytes),
            RowStoreOverheadBytes: CacheMemoryEstimator.SaturatingAdd(left.RowStoreOverheadBytes, right.RowStoreOverheadBytes),
            TransactionRowPayloadBytes: CacheMemoryEstimator.SaturatingAdd(left.TransactionRowPayloadBytes, right.TransactionRowPayloadBytes),
            TransactionRowStoreOverheadBytes: CacheMemoryEstimator.SaturatingAdd(left.TransactionRowStoreOverheadBytes, right.TransactionRowStoreOverheadBytes),
            IndexPayloadBytes: CacheMemoryEstimator.SaturatingAdd(left.IndexPayloadBytes, right.IndexPayloadBytes),
            IndexOverheadBytes: CacheMemoryEstimator.SaturatingAdd(left.IndexOverheadBytes, right.IndexOverheadBytes),
            RelationObjectBytes: CacheMemoryEstimator.SaturatingAdd(left.RelationObjectBytes, right.RelationObjectBytes),
            NotificationBytes: CacheMemoryEstimator.SaturatingAdd(left.NotificationBytes, right.NotificationBytes),
            SnapshotBytes: CacheMemoryEstimator.SaturatingAdd(left.SnapshotBytes, right.SnapshotBytes));

    public static CacheMemoryEstimate Sum(IEnumerable<CacheMemoryEstimate> estimates)
    {
        var total = Empty;

        foreach (var estimate in estimates)
            total += estimate;

        return total;
    }
}
