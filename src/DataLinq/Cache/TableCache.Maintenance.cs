using System;
using System.Diagnostics;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Diagnostics;

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

    public int RemoveRowsByLimit(CacheLimitType limitType, long amount)
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

            return RemoveRowsInsertedBeforeTick(cutoffTick);
        }

        var startedAt = Stopwatch.GetTimestamp();
        var rowsRemoved = limitType switch
        {
            CacheLimitType.Rows => rowCache?.RemoveRowsOverRowLimit((int)amount) ?? 0,
            CacheLimitType.Bytes => rowCache?.RemoveRowsOverSizeLimit(amount) ?? 0,
            CacheLimitType.Kilobytes => rowCache?.RemoveRowsOverSizeLimit(amount * 1024) ?? 0,
            CacheLimitType.Megabytes => rowCache?.RemoveRowsOverSizeLimit(amount * 1024 * 1024) ?? 0,
            CacheLimitType.Gigabytes => rowCache?.RemoveRowsOverSizeLimit(amount * 1024 * 1024 * 1024) ?? 0,
            _ => throw new NotImplementedException($"CacheLimitType '{limitType}' is not implemented.")
        };

        var duration = Stopwatch.GetElapsedTime(startedAt);
        RefreshOccupancyMetrics();
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, GetCacheLimitOperationName(limitType), rowsRemoved, duration);
        MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
        return rowsRemoved;
    }

    public int RemoveRowsInsertedBeforeTick(long tick)
    {
        var startedAt = Stopwatch.GetTimestamp();
        RemoveAllIndicesInsertedBeforeTick(tick);
        var rowsRemoved = rowCache?.RemoveRowsInsertedBeforeTick(tick) ?? 0;
        var duration = Stopwatch.GetElapsedTime(startedAt);
        RefreshOccupancyMetrics();
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.AgeLimit, rowsRemoved, duration);
        MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
        return rowsRemoved;
    }

    public TableCacheSnapshot MakeSnapshot()
    {
        return new(Table.DbName, RowCount, TotalBytes, NewestTick, OldestTick, IndicesCount.ToArray());
    }

    internal void UnregisterTelemetry()
    {
        DataLinqTelemetry.UnregisterTableCache(telemetryContext, Table.DbName);
    }

    private CacheOccupancyMetricsSnapshot GetOccupancySnapshot()
        => new(
            Rows: RowCount,
            TransactionRows: transactionRows?.Values.Sum(x => (long)x.Count) ?? 0,
            Bytes: TotalBytes,
            IndexEntries: GetLoadedIndexCaches().Sum(x => (long)x.Count));

    private void RefreshOccupancyMetrics()
    {
        MetricsHandle.UpdateCacheOccupancy(GetOccupancySnapshot());
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
