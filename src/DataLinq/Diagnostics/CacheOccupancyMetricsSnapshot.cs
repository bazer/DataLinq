using System.Collections.Generic;
using DataLinq.Cache;

namespace DataLinq.Diagnostics;

/// <summary>
/// Current cache occupancy metrics for a table, provider, or the full DataLinq runtime.
/// </summary>
public readonly record struct CacheOccupancyMetricsSnapshot
{
    /// <summary>
    /// Creates a cache occupancy snapshot using the historical row-payload byte field.
    /// </summary>
    /// <param name="Rows">Current number of rows stored in the row cache.</param>
    /// <param name="TransactionRows">Current number of rows stored in transaction-local caches.</param>
    /// <param name="Bytes">Current estimated row-payload bytes in the row cache, not total cache memory footprint.</param>
    /// <param name="IndexEntries">Current number of cached index entries.</param>
    public CacheOccupancyMetricsSnapshot(long Rows, long TransactionRows, long Bytes, long IndexEntries)
        : this(
            Rows: Rows,
            TransactionRows: TransactionRows,
            RowPayloadBytes: Bytes,
            EstimatedCacheBytes: Bytes,
            RowStoreOverheadBytes: 0,
            TransactionRowPayloadBytes: 0,
            TransactionRowStoreOverheadBytes: 0,
            IndexPayloadBytes: 0,
            IndexOverheadBytes: 0,
            RelationObjectBytes: 0,
            NotificationBytes: 0,
            SnapshotBytes: 0,
            IndexEntries: IndexEntries)
    {
    }

    /// <summary>
    /// Creates a cache occupancy snapshot with estimated total cache bytes and component estimates.
    /// </summary>
    public CacheOccupancyMetricsSnapshot(
        long Rows,
        long TransactionRows,
        long RowPayloadBytes,
        long EstimatedCacheBytes,
        long RowStoreOverheadBytes,
        long TransactionRowPayloadBytes,
        long TransactionRowStoreOverheadBytes,
        long IndexPayloadBytes,
        long IndexOverheadBytes,
        long RelationObjectBytes,
        long NotificationBytes,
        long SnapshotBytes,
        long IndexEntries)
    {
        this.Rows = Rows;
        this.TransactionRows = TransactionRows;
        this.RowPayloadBytes = RowPayloadBytes;
        this.EstimatedCacheBytes = EstimatedCacheBytes;
        this.RowStoreOverheadBytes = RowStoreOverheadBytes;
        this.TransactionRowPayloadBytes = TransactionRowPayloadBytes;
        this.TransactionRowStoreOverheadBytes = TransactionRowStoreOverheadBytes;
        this.IndexPayloadBytes = IndexPayloadBytes;
        this.IndexOverheadBytes = IndexOverheadBytes;
        this.RelationObjectBytes = RelationObjectBytes;
        this.NotificationBytes = NotificationBytes;
        this.SnapshotBytes = SnapshotBytes;
        this.IndexEntries = IndexEntries;
    }

    /// <summary>Current number of rows stored in the row cache.</summary>
    public long Rows { get; init; }

    /// <summary>Current number of rows stored in transaction-local caches.</summary>
    public long TransactionRows { get; init; }

    /// <summary>
    /// Current estimated row-payload bytes in the row cache, not total cache memory footprint.
    /// </summary>
    public long Bytes => RowPayloadBytes;

    /// <summary>
    /// Current estimated row-payload bytes. This is the explicit name for the legacy <see cref="Bytes"/> value.
    /// </summary>
    public long RowPayloadBytes { get; init; }

    /// <summary>
    /// Current estimated cache memory footprint across row payloads and tracked cache containers.
    /// </summary>
    public long EstimatedCacheBytes { get; init; }

    /// <summary>Estimated row-cache container overhead bytes.</summary>
    public long RowStoreOverheadBytes { get; init; }

    /// <summary>Estimated row-payload bytes retained by transaction-local caches.</summary>
    public long TransactionRowPayloadBytes { get; init; }

    /// <summary>Estimated container overhead bytes retained by transaction-local caches.</summary>
    public long TransactionRowStoreOverheadBytes { get; init; }

    /// <summary>Estimated payload bytes retained by loaded index caches.</summary>
    public long IndexPayloadBytes { get; init; }

    /// <summary>Estimated container overhead bytes retained by loaded index caches.</summary>
    public long IndexOverheadBytes { get; init; }

    /// <summary>Estimated bytes retained by relation object metadata.</summary>
    public long RelationObjectBytes { get; init; }

    /// <summary>Estimated bytes retained by cache notification managers.</summary>
    public long NotificationBytes { get; init; }

    /// <summary>Estimated bytes retained by cache history snapshots.</summary>
    public long SnapshotBytes { get; init; }

    /// <summary>Current number of cached index entries.</summary>
    public long IndexEntries { get; init; }

    internal static CacheOccupancyMetricsSnapshot Sum(IEnumerable<CacheOccupancyMetricsSnapshot> snapshots)
    {
        long rows = 0;
        long transactionRows = 0;
        long rowPayloadBytes = 0;
        long estimatedCacheBytes = 0;
        long rowStoreOverheadBytes = 0;
        long transactionRowPayloadBytes = 0;
        long transactionRowStoreOverheadBytes = 0;
        long indexPayloadBytes = 0;
        long indexOverheadBytes = 0;
        long relationObjectBytes = 0;
        long notificationBytes = 0;
        long snapshotBytes = 0;
        long indexEntries = 0;

        foreach (var snapshot in snapshots)
        {
            rows = CacheMemoryEstimator.SaturatingAdd(rows, snapshot.Rows);
            transactionRows = CacheMemoryEstimator.SaturatingAdd(transactionRows, snapshot.TransactionRows);
            rowPayloadBytes = CacheMemoryEstimator.SaturatingAdd(rowPayloadBytes, snapshot.RowPayloadBytes);
            estimatedCacheBytes = CacheMemoryEstimator.SaturatingAdd(estimatedCacheBytes, snapshot.EstimatedCacheBytes);
            rowStoreOverheadBytes = CacheMemoryEstimator.SaturatingAdd(rowStoreOverheadBytes, snapshot.RowStoreOverheadBytes);
            transactionRowPayloadBytes = CacheMemoryEstimator.SaturatingAdd(transactionRowPayloadBytes, snapshot.TransactionRowPayloadBytes);
            transactionRowStoreOverheadBytes = CacheMemoryEstimator.SaturatingAdd(transactionRowStoreOverheadBytes, snapshot.TransactionRowStoreOverheadBytes);
            indexPayloadBytes = CacheMemoryEstimator.SaturatingAdd(indexPayloadBytes, snapshot.IndexPayloadBytes);
            indexOverheadBytes = CacheMemoryEstimator.SaturatingAdd(indexOverheadBytes, snapshot.IndexOverheadBytes);
            relationObjectBytes = CacheMemoryEstimator.SaturatingAdd(relationObjectBytes, snapshot.RelationObjectBytes);
            notificationBytes = CacheMemoryEstimator.SaturatingAdd(notificationBytes, snapshot.NotificationBytes);
            snapshotBytes = CacheMemoryEstimator.SaturatingAdd(snapshotBytes, snapshot.SnapshotBytes);
            indexEntries = CacheMemoryEstimator.SaturatingAdd(indexEntries, snapshot.IndexEntries);
        }

        return new CacheOccupancyMetricsSnapshot(
            Rows: rows,
            TransactionRows: transactionRows,
            RowPayloadBytes: rowPayloadBytes,
            EstimatedCacheBytes: estimatedCacheBytes,
            RowStoreOverheadBytes: rowStoreOverheadBytes,
            TransactionRowPayloadBytes: transactionRowPayloadBytes,
            TransactionRowStoreOverheadBytes: transactionRowStoreOverheadBytes,
            IndexPayloadBytes: indexPayloadBytes,
            IndexOverheadBytes: indexOverheadBytes,
            RelationObjectBytes: relationObjectBytes,
            NotificationBytes: notificationBytes,
            SnapshotBytes: snapshotBytes,
            IndexEntries: indexEntries);
    }
}
