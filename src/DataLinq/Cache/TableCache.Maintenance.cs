using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Instances;

namespace DataLinq.Cache;

public partial class TableCache
{
    public void ClearCache()
    {
        ClearRows();
        ClearIndex();
    }

    public void ClearRows()
    {
        var rowsRemoved = RowCount;
        var startedAt = Stopwatch.GetTimestamp();
        rowCache?.ClearRows();
        var duration = Stopwatch.GetElapsedTime(startedAt);
        RefreshOccupancyMetrics();
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.Clear, rowsRemoved, duration);
        MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
        OnRowChanged();
    }

    public int RemoveRowsByLimit(CacheLimitType limitType, long amount, string cleanupTrigger = CacheMaintenanceTriggers.Manual)
    {
        if (limitType is CacheLimitType.Seconds or CacheLimitType.Minutes or CacheLimitType.Hours or CacheLimitType.Days or CacheLimitType.Ticks)
        {
            var cutoffTick = limitType switch
            {
                CacheLimitType.Seconds => DateTime.Now.Subtract(TimeSpan.FromSeconds(amount)).Ticks,
                CacheLimitType.Minutes => DateTime.Now.Subtract(TimeSpan.FromMinutes(amount)).Ticks,
                CacheLimitType.Hours => DateTime.Now.Subtract(TimeSpan.FromHours(amount)).Ticks,
                CacheLimitType.Days => DateTime.Now.Subtract(TimeSpan.FromDays(amount)).Ticks,
                CacheLimitType.Ticks => DateTime.Now.Subtract(TimeSpan.FromTicks(amount)).Ticks,
                _ => throw new NotImplementedException($"CacheLimitType '{limitType}' is not implemented.")
            };

            return RemoveRowsInsertedBeforeTick(cutoffTick, cleanupTrigger);
        }

        var startedAt = Stopwatch.GetTimestamp();
        var rowsRemoved = limitType switch
        {
            CacheLimitType.Rows => RemoveRowsOverRowLimit(NormalizeRowLimit(amount)),
            CacheLimitType.Bytes => RemoveRowsOverEstimatedSizeLimit(ConvertByteLimitToBytes(limitType, amount)),
            CacheLimitType.Kilobytes => RemoveRowsOverEstimatedSizeLimit(ConvertByteLimitToBytes(limitType, amount)),
            CacheLimitType.Megabytes => RemoveRowsOverEstimatedSizeLimit(ConvertByteLimitToBytes(limitType, amount)),
            CacheLimitType.Gigabytes => RemoveRowsOverEstimatedSizeLimit(ConvertByteLimitToBytes(limitType, amount)),
            _ => throw new NotImplementedException($"CacheLimitType '{limitType}' is not implemented.")
        };

        var duration = Stopwatch.GetElapsedTime(startedAt);
        RecordCacheLimitCleanup(GetCacheLimitOperationName(limitType), rowsRemoved, duration, cleanupTrigger);
        return rowsRemoved;
    }

    internal int RemoveRowsByEstimatedCacheByteLimit(long maxBytes, string cleanupTrigger = CacheMaintenanceTriggers.Manual)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var rowsRemoved = RemoveRowsOverEstimatedSizeLimit(maxBytes);
        var duration = Stopwatch.GetElapsedTime(startedAt);
        RecordCacheLimitCleanup(CacheMaintenanceOperations.SizeLimit, rowsRemoved, duration, cleanupTrigger);
        return rowsRemoved;
    }

    internal int RemoveRowsForMemoryPressure(long maxBytes, int maxRows, long targetEstimatedBytes)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var estimatedBytesBefore = GetMemoryEstimate().EstimatedCacheBytes;
        var rowsRemoved = RemoveRowsOverEstimatedSizeLimit(maxBytes, maxRows);
        var estimatedBytesAfter = GetMemoryEstimate().EstimatedCacheBytes;
        var duration = Stopwatch.GetElapsedTime(startedAt);
        RefreshOccupancyMetrics();
        DataLinqTelemetry.RecordCacheMaintenance(
            telemetryContext,
            Table.DbName,
            CacheMaintenanceOperations.MemoryPressure,
            rowsRemoved,
            duration,
            cleanupTrigger: CacheMaintenanceTriggers.MemoryPressure,
            cleanupReason: CacheMaintenanceReasons.MemoryPressure,
            cleanupBasis: CacheMaintenanceBases.EstimatedCacheBytes,
            estimatedBytesBefore: estimatedBytesBefore,
            estimatedBytesAfter: estimatedBytesAfter,
            targetEstimatedBytes: targetEstimatedBytes);
        MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
        return rowsRemoved;
    }

    public int RemoveRowsInsertedBeforeTick(long tick, string cleanupTrigger = CacheMaintenanceTriggers.Manual)
    {
        var startedAt = Stopwatch.GetTimestamp();
        RemoveAllIndicesInsertedBeforeTick(tick);
        var rowsRemoved = ProcessRemovedRowsAndNotify(rowCache?.RemoveRowsInsertedBeforeTickAndReturnKeys(tick) ?? []);
        var duration = Stopwatch.GetElapsedTime(startedAt);
        RecordCacheLimitCleanup(CacheMaintenanceOperations.AgeLimit, rowsRemoved, duration, cleanupTrigger);
        return rowsRemoved;
    }

    public TableCacheSnapshot MakeSnapshot()
    {
        return new(Table.DbName, RowCount, GetMemoryEstimate(), NewestTick, OldestTick, IndicesCount.ToArray());
    }

    internal CacheMemoryEstimate GetMemoryEstimate()
    {
        var estimate = rowCache?.GetMemoryEstimate() ?? CacheMemoryEstimate.Empty;
        var transactionEstimate = GetTransactionRowsMemoryEstimate();
        var indexEstimate = CacheMemoryEstimate.Sum(GetLoadedIndexCaches().Select(x => x.GetMemoryEstimate()));
        var notificationEstimate = GetNotificationMemoryEstimate();

        return estimate + transactionEstimate + indexEstimate + notificationEstimate;
    }

    internal void UnregisterTelemetry()
    {
        DataLinqTelemetry.UnregisterTableCache(telemetryContext, Table.DbName);
    }

    private CacheOccupancyMetricsSnapshot GetOccupancySnapshot()
    {
        var estimate = GetMemoryEstimate();

        return new CacheOccupancyMetricsSnapshot(
            Rows: RowCount,
            TransactionRows: transactionRows?.Values.Sum(x => (long)x.Count) ?? 0,
            RowPayloadBytes: estimate.RowPayloadBytes,
            EstimatedCacheBytes: estimate.EstimatedCacheBytes,
            RowStoreOverheadBytes: estimate.RowStoreOverheadBytes,
            TransactionRowPayloadBytes: estimate.TransactionRowPayloadBytes,
            TransactionRowStoreOverheadBytes: estimate.TransactionRowStoreOverheadBytes,
            IndexPayloadBytes: estimate.IndexPayloadBytes,
            IndexOverheadBytes: estimate.IndexOverheadBytes,
            RelationObjectBytes: estimate.RelationObjectBytes,
            NotificationBytes: estimate.NotificationBytes,
            SnapshotBytes: estimate.SnapshotBytes,
            IndexEntries: GetLoadedIndexCaches().Sum(x => (long)x.Count));
    }

    private void RefreshOccupancyMetrics()
    {
        MetricsHandle.UpdateCacheOccupancy(GetOccupancySnapshot());
    }

    private int RemoveRowsOverRowLimit(int maxRows) =>
        ProcessRemovedRowsAndNotify(rowCache?.RemoveRowsOverRowLimitAndReturnKeys(maxRows) ?? []);

    private int RemoveRowsOverEstimatedSizeLimit(long maxBytes, int maxRows = int.MaxValue)
    {
        var rowsRemoved = 0;
        var impactBuilder = new CacheInvalidationImpactBuilder();

        while (GetMemoryEstimate().EstimatedCacheBytes > maxBytes && rowsRemoved < maxRows)
        {
            var removedKeys = rowCache?.RemoveOldestRows(1) ?? [];
            if (removedKeys.Count == 0)
                break;

            rowsRemoved += ProcessRemovedRows(removedKeys, impactBuilder);
        }

        NotifyRemovedRows(impactBuilder);
        return rowsRemoved;
    }

    private int ProcessRemovedRowsAndNotify(IReadOnlyList<DataLinqKey> removedKeys)
    {
        var impactBuilder = new CacheInvalidationImpactBuilder();
        var rowsRemoved = ProcessRemovedRows(removedKeys, impactBuilder);
        NotifyRemovedRows(impactBuilder);
        return rowsRemoved;
    }

    private int ProcessRemovedRows(IReadOnlyList<DataLinqKey> removedKeys, CacheInvalidationImpactBuilder impactBuilder)
    {
        for (var i = 0; i < removedKeys.Count; i++)
        {
            var primaryKey = removedKeys[i];
            TryRemoveRowFromAllIndices(primaryKey, out _);
            impactBuilder.AddPrimaryKey(primaryKey);
        }

        return removedKeys.Count;
    }

    private void NotifyRemovedRows(CacheInvalidationImpactBuilder impactBuilder)
    {
        var impact = impactBuilder.Build();
        if (!impact.IsEmpty)
            OnRowChanged(impact);
    }

    private void RecordCacheLimitCleanup(string operationName, int rowsRemoved, TimeSpan duration, string cleanupTrigger)
    {
        RefreshOccupancyMetrics();
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, operationName, rowsRemoved, duration, cleanupTrigger);
        MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
    }

    private CacheMemoryEstimate GetTransactionRowsMemoryEstimate()
    {
        var rowsByTransaction = transactionRows;
        if (rowsByTransaction is null)
            return CacheMemoryEstimate.Empty;

        var rowEstimate = CacheMemoryEstimate.Sum(rowsByTransaction.Values.Select(x => x.GetMemoryEstimate()));
        var transactionRowStoreOverheadBytes = CacheMemoryEstimator.SaturatingAdd(
            rowEstimate.RowStoreOverheadBytes,
            CacheMemoryEstimator.ConcurrentDictionaryOverheadBytes(rowsByTransaction.Count));

        return new CacheMemoryEstimate(
            TransactionRowPayloadBytes: rowEstimate.RowPayloadBytes,
            TransactionRowStoreOverheadBytes: transactionRowStoreOverheadBytes);
    }

    internal static bool IsByteCacheLimit(CacheLimitType limitType) =>
        limitType is CacheLimitType.Bytes or CacheLimitType.Kilobytes or CacheLimitType.Megabytes or CacheLimitType.Gigabytes;

    internal static long ConvertByteLimitToBytes(CacheLimitType limitType, long amount)
        => limitType switch
        {
            CacheLimitType.Bytes => Math.Max(amount, 0),
            CacheLimitType.Kilobytes => CacheMemoryEstimator.SaturatingMultiply(amount, 1024),
            CacheLimitType.Megabytes => CacheMemoryEstimator.SaturatingMultiply(amount, 1024 * 1024),
            CacheLimitType.Gigabytes => CacheMemoryEstimator.SaturatingMultiply(amount, 1024L * 1024L * 1024L),
            _ => throw new ArgumentOutOfRangeException(nameof(limitType), limitType, "Cache limit is not byte-based.")
        };

    private static int NormalizeRowLimit(long amount)
    {
        if (amount > int.MaxValue)
            return int.MaxValue;

        if (amount < int.MinValue)
            return int.MinValue;

        return (int)amount;
    }

    private static string GetCacheLimitOperationName(CacheLimitType limitType)
        => limitType switch
        {
            CacheLimitType.Rows => CacheMaintenanceOperations.RowLimit,
            CacheLimitType.Bytes => CacheMaintenanceOperations.SizeLimit,
            CacheLimitType.Kilobytes => CacheMaintenanceOperations.SizeLimit,
            CacheLimitType.Megabytes => CacheMaintenanceOperations.SizeLimit,
            CacheLimitType.Gigabytes => CacheMaintenanceOperations.SizeLimit,
            CacheLimitType.Seconds => CacheMaintenanceOperations.AgeLimit,
            CacheLimitType.Minutes => CacheMaintenanceOperations.AgeLimit,
            CacheLimitType.Hours => CacheMaintenanceOperations.AgeLimit,
            CacheLimitType.Days => CacheMaintenanceOperations.AgeLimit,
            CacheLimitType.Ticks => CacheMaintenanceOperations.AgeLimit,
            _ => CacheMaintenanceOperations.Limit
        };
}
