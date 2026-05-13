using System;
using System.Linq;
using DataLinq.Extensions.Helpers;
using DataLinq.Utils;

namespace DataLinq.Cache;

public class TableCacheSnapshot
{
    public TableCacheSnapshot(string tableName, int rowCount, long totalBytes, long? newestTick, long? oldestTick, (string index, int count)[] indices)
        : this(
            tableName,
            rowCount,
            new CacheMemoryEstimate(RowPayloadBytes: totalBytes),
            newestTick,
            oldestTick,
            indices)
    {
    }

    internal TableCacheSnapshot(
        string tableName,
        int rowCount,
        CacheMemoryEstimate memoryEstimate,
        long? newestTick,
        long? oldestTick,
        (string index, int count)[] indices)
    {
        TableName = tableName;
        RowCount = rowCount;
        RowPayloadBytes = memoryEstimate.RowPayloadBytes;
        EstimatedCacheBytes = memoryEstimate.EstimatedCacheBytes;
        RowStoreOverheadBytes = memoryEstimate.RowStoreOverheadBytes;
        TransactionRowPayloadBytes = memoryEstimate.TransactionRowPayloadBytes;
        TransactionRowStoreOverheadBytes = memoryEstimate.TransactionRowStoreOverheadBytes;
        IndexPayloadBytes = memoryEstimate.IndexPayloadBytes;
        IndexOverheadBytes = memoryEstimate.IndexOverheadBytes;
        RelationObjectBytes = memoryEstimate.RelationObjectBytes;
        NotificationBytes = memoryEstimate.NotificationBytes;
        SnapshotBytes = memoryEstimate.SnapshotBytes;
        NewestTick = newestTick > 0 ? newestTick : null;
        OldestTick = oldestTick > 0 ? oldestTick : null;
        Indices = indices;
    }

    public string TableName { get; }
    public long? NewestTick { get; }
    public long? OldestTick { get; }
    public (string index, int count)[] Indices { get; }
    public string IndicesFormatted => Indices.Select(x => $"{x.index} ({x.count})").ToJoinedString(", ");
    public int RowCount { get; }
    /// <summary>Estimated bytes for row values only, excluding cache container overhead.</summary>
    public long RowPayloadBytes { get; }
    public string RowPayloadBytesFormatted => RowPayloadBytes.ToFileSize();
    /// <summary>Estimated bytes retained by row payloads and tracked cache containers.</summary>
    public long EstimatedCacheBytes { get; }
    public string EstimatedCacheBytesFormatted => EstimatedCacheBytes.ToFileSize();
    /// <summary>Estimated row-cache container overhead bytes.</summary>
    public long RowStoreOverheadBytes { get; }
    /// <summary>Estimated row-payload bytes retained by transaction-local caches.</summary>
    public long TransactionRowPayloadBytes { get; }
    /// <summary>Estimated container overhead bytes retained by transaction-local caches.</summary>
    public long TransactionRowStoreOverheadBytes { get; }
    /// <summary>Estimated payload bytes retained by loaded index caches.</summary>
    public long IndexPayloadBytes { get; }
    /// <summary>Estimated container overhead bytes retained by loaded index caches.</summary>
    public long IndexOverheadBytes { get; }
    /// <summary>Estimated bytes retained by relation object metadata.</summary>
    public long RelationObjectBytes { get; }
    /// <summary>Estimated bytes retained by cache notification managers.</summary>
    public long NotificationBytes { get; }
    /// <summary>Estimated bytes retained by cache history snapshots.</summary>
    public long SnapshotBytes { get; }
    /// <summary>Compatibility alias for <see cref="RowPayloadBytes"/>.</summary>
    public long TotalBytes => RowPayloadBytes;
    public string TotalBytesFormatted => RowPayloadBytesFormatted;
    public DateTime? NewestDateTime => NewestTick.HasValue ? new(NewestTick.Value) : null;
    public DateTime? OldestDateTime => OldestTick.HasValue ? new(OldestTick.Value) : null;
}
