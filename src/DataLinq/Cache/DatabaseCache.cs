using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Workers;

namespace DataLinq.Cache;

public class DatabaseCache : IDisposable
{
    internal static Func<bool> IsBrowserRuntime { get; set; } = static () => OperatingSystem.IsBrowser();

    public IDatabaseProvider Database { get; set; }
    internal DatabaseCachePolicy Policy { get; }
    private readonly DataLinqLoggingConfiguration loggingConfiguration;
    public Dictionary<TableDefinition, TableCache> TableCaches { get; }

    public CleanCacheWorker? CleanCacheWorker { get; }

    public CacheHistory History { get; } = new();

    public DatabaseCache(IDatabaseProvider database, DataLinqLoggingConfiguration loggingConfiguration)
    {
        this.Database = database;
        this.loggingConfiguration = loggingConfiguration;
        this.Policy = DatabaseCachePolicy.FromMetadata(database.Metadata);
        this.TableCaches = new Dictionary<TableDefinition, TableCache>(this.Database.Metadata.TableModels.Count);
        for (var i = 0; i < this.Database.Metadata.TableModels.Count; i++)
        {
            var table = this.Database.Metadata.TableModels[i].Table;
            this.TableCaches.Add(table, new TableCache(table, this, loggingConfiguration));
        }

        if (IsBrowserRuntime())
            return;

        for (var i = 0; i < Policy.CacheCleanup.Count; i++)
        {
            var cacheCleanup = Policy.CacheCleanup[i];
            var timespan = GetFromCacheCleanupType(cacheCleanup.cleanupType, cacheCleanup.amount);
            this.CleanCacheWorker = new CleanCacheWorker(database, new LongRunningTaskCreator(), timespan);
            this.CleanCacheWorker.Start();
        }
    }

    //public TableCache GetTableCache(string tableName)
    //{
    //    return TableCaches.Single(x => x.Table.DbName == tableName);
    //}

    public TableCache GetTableCache(TableDefinition table)
    {
        return TableCaches[table];
    }

    public DatabaseCacheSnapshot GetLatestSnapshot()
    {
        return History.GetLatest() ?? MakeSnapshot();
    }

    public DatabaseCacheSnapshot MakeSnapshot()
    {
        var snapshot = new DatabaseCacheSnapshot(DateTime.UtcNow, TableCaches.Values.Select(x => x.MakeSnapshot()).ToArray());
        History.Add(snapshot);

        return snapshot;
    }

    internal CacheMemoryEstimate GetMemoryEstimate() =>
        CacheMemoryEstimate.Sum(TableCaches.Values.Select(x => x.GetMemoryEstimate())) + History.GetMemoryEstimate();

    private TimeSpan GetFromCacheCleanupType(CacheCleanupType type, long amount)
    {
        return type switch
        {
            CacheCleanupType.Seconds => TimeSpan.FromSeconds(amount),
            CacheCleanupType.Minutes => TimeSpan.FromMinutes(amount),
            CacheCleanupType.Hours => TimeSpan.FromHours(amount),
            CacheCleanupType.Days => TimeSpan.FromDays(amount),
            _ => throw new NotImplementedException(),
        };
    }

    public (IndexCacheType, int? amount) GetIndexCachePolicy()
        => GetIndexCachePolicy(Policy.IndexCache);

    internal (IndexCacheType, int? amount) GetIndexCachePolicy(TableDefinition table)
        => GetIndexCachePolicy(Policy.GetTableIndexCache(table));

    private static (IndexCacheType, int? amount) GetIndexCachePolicy(
        IReadOnlyList<(IndexCacheType indexCacheType, int? amount)> indexCache)
    {
        if (indexCache.Count == 0)
            return (IndexCacheType.None, 0);

        (IndexCacheType indexCacheType, int? amount)? maxRowsPolicy = null;
        var hasAllPolicy = false;

        for (var i = 0; i < indexCache.Count; i++)
        {
            var policy = indexCache[i];
            if (policy.indexCacheType == IndexCacheType.None)
                return (IndexCacheType.None, 0);

            if (policy.indexCacheType == IndexCacheType.MaxAmountRows)
                maxRowsPolicy = policy;
            else if (policy.indexCacheType == IndexCacheType.All)
                hasAllPolicy = true;
        }

        if (maxRowsPolicy.HasValue)
            return maxRowsPolicy.Value;

        if (hasAllPolicy)
            return (IndexCacheType.All, null);

        throw new NotImplementedException();
    }

    internal IDisposable OverrideTableCacheLimitsForTesting(
        TableDefinition table,
        IReadOnlyList<(CacheLimitType limitType, long amount)> cacheLimits) =>
        Policy.OverrideTableCacheLimitsForTesting(table, cacheLimits);

    public void ApplyChanges(IEnumerable<StateChange> changes, Transaction? transaction = null)
    {
        foreach (var change in changes.GroupBy(x => x.Table))
        {
            TableCaches[change.Key].ApplyChanges(change, transaction);
        }
    }

    public void RemoveTransaction(Transaction transaction)
    {
        foreach (var table in TableCaches.Values)
        {
            table.TryRemoveTransaction(transaction);
        }
    }

    public void CleanRelationNotifications()
    {
        foreach (var table in TableCaches.Values)
        {
            table.CleanRelationNotifications();
        }
    }


    public IEnumerable<(TableCache table, int numRows)> RemoveRowsBySettings(string cleanupTrigger = CacheMaintenanceTriggers.Manual)
    {
        foreach (var table in TableCaches.Values)
        {
            foreach (var (limitType, amount) in Policy.GetTableCacheLimits(table.Table))
            {
                var numRows = table.RemoveRowsByLimit(limitType, amount, cleanupTrigger);

                if (numRows > 0)
                    yield return (table, numRows);
            }
        }

        foreach (var (limitType, amount) in Policy.DatabaseCacheLimits)
        {
            foreach (var rows in RemoveRowsByLimit(limitType, amount, cleanupTrigger))
                yield return rows;
        }
    }

    public IEnumerable<(TableCache table, int numRows)> RemoveRowsByLimit(
        CacheLimitType limitType,
        long amount,
        string cleanupTrigger = CacheMaintenanceTriggers.Manual)
    {
        if (TableCache.IsByteCacheLimit(limitType))
        {
            foreach (var rows in RemoveRowsByDatabaseByteLimit(TableCache.ConvertByteLimitToBytes(limitType, amount), cleanupTrigger))
                yield return rows;

            yield break;
        }

        foreach (var table in TableCaches.Values)
        {
            var numRows = table.RemoveRowsByLimit(limitType, amount, cleanupTrigger);

            if (numRows > 0)
                yield return (table, numRows);
        }
    }

    private IEnumerable<(TableCache table, int numRows)> RemoveRowsByDatabaseByteLimit(long maxBytes, string cleanupTrigger)
    {
        while (GetMemoryEstimate().EstimatedCacheBytes > maxBytes)
        {
            var tableEstimate = TableCaches.Values
                .Where(x => x.RowCount > 0)
                .Select(x => (table: x, estimatedBytes: x.GetMemoryEstimate().EstimatedCacheBytes))
                .Where(x => x.estimatedBytes > 0)
                .OrderByDescending(x => x.estimatedBytes)
                .FirstOrDefault();

            if (tableEstimate.table is null)
                yield break;

            var overflowBytes = GetMemoryEstimate().EstimatedCacheBytes - maxBytes;
            var tableTargetBytes = Math.Max(0, tableEstimate.estimatedBytes - overflowBytes);
            var numRows = tableEstimate.table.RemoveRowsByEstimatedCacheByteLimit(tableTargetBytes, cleanupTrigger);

            if (numRows <= 0)
                yield break;

            yield return (tableEstimate.table, numRows);
        }
    }

    public IEnumerable<(TableCache table, int numRows)> RemoveRowsInsertedBeforeTick(long tick)
    {
        foreach (var table in TableCaches.Values)
        {
            var numRows = table.RemoveRowsInsertedBeforeTick(tick);

            if (numRows > 0)
                yield return (table, numRows);
        }
    }

    public void ClearCache()
    {
        foreach (var table in TableCaches.Values)
        {
            table.ClearCache();
        }
    }

    public void Dispose()
    {
        this.CleanCacheWorker?.Stop();
        foreach (var table in TableCaches.Values)
            table.UnregisterTelemetry();
        this.ClearCache();
    }
}
