using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Extensions.Helpers;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Workers;

namespace DataLinq.Cache;

public class DatabaseCache : IDisposable
{
    public IDatabaseProvider Database { get; set; }

    public List<TableCache> TableCaches { get; }

    public CleanCacheWorker CleanCacheWorker { get; }

    public CacheHistory History { get; } = new();

    public DatabaseCache(IDatabaseProvider database)
    {
        this.Database = database;

        this.TableCaches = this.Database.Metadata.TableModels
            .Select(x => new TableCache(x.Table, this))
            .ToList();

        this.MakeSnapshot();

        var cacheCleanupInterval = database.Metadata.CacheCleanup;

        if (!cacheCleanupInterval.Any())
            cacheCleanupInterval = (CacheCleanupType.Minutes, 5L).Yield().ToList();

        foreach (var timespan in cacheCleanupInterval.Select(x => GetFromCacheCleanupType(x.cleanupType, x.amount)))
        {
            this.CleanCacheWorker = new CleanCacheWorker(database, new LongRunningTaskCreator(), timespan);
            this.CleanCacheWorker.Start();
        }
    }

    public TableCache GetTableCache(string tableName)
    {
        return TableCaches.Single(x => x.Table.DbName == tableName);
    }

    public TableCache GetTableCache(TableMetadata table)
    {
        return TableCaches.Single(x => x.Table == table);
    }

    public DatabaseCacheSnapshot GetLatestSnapshot()
    {
        return History.GetLatest() ?? MakeSnapshot();
    }

    public DatabaseCacheSnapshot MakeSnapshot()
    {
        var snapshot = new DatabaseCacheSnapshot(DateTime.UtcNow, TableCaches.Select(x => x.MakeSnapshot()).ToArray());
        History.Add(snapshot);

        return snapshot;
    }

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
    {
        if (!Database.Metadata.IndexCache.Any() || Database.Metadata.IndexCache.Any(x => x.indexCacheType == IndexCacheType.None))
            return (IndexCacheType.None, 0);

        if (Database.Metadata.IndexCache.Any(x => x.indexCacheType == IndexCacheType.MaxAmountRows))
            return (IndexCacheType.MaxAmountRows, Database.Metadata.IndexCache.First(x => x.indexCacheType == IndexCacheType.MaxAmountRows).amount);

        if (Database.Metadata.IndexCache.Any(x => x.indexCacheType == IndexCacheType.All))
            return (IndexCacheType.All, null);

        throw new NotImplementedException();
    }


    public void ApplyChanges(IEnumerable<StateChange> changes, Transaction? transaction = null)
    {
        foreach (var change in changes.GroupBy(x => x.Table))
        {
            TableCaches.Single(x => x.Table == change.Key).ApplyChanges(change, transaction);
        }
    }

    public void RemoveTransaction(Transaction transaction)
    {
        foreach (var table in TableCaches)
        {
            table.TryRemoveTransaction(transaction);
        }
    }

    public IEnumerable<(TableCache table, int numRows)> RemoveRowsBySettings()
    {
        foreach (var table in TableCaches)
        {
            foreach (var (limitType, amount) in table.Table.CacheLimits)
            {
                foreach (var rows in RemoveRowsByLimit(limitType, amount))
                    yield return rows;
            }
        }

        foreach (var (limitType, amount) in Database.Metadata.CacheLimits)
        {
            foreach (var rows in RemoveRowsByLimit(limitType, amount))
                yield return rows;
        }
    }

    public IEnumerable<(TableCache table, int numRows)> RemoveRowsByLimit(CacheLimitType limitType, long amount)
    {
        foreach (var table in TableCaches)
        {
            var numRows = table.RemoveRowsByLimit(limitType, amount);

            if (numRows > 0)
                yield return (table, numRows);
        }
    }

    public IEnumerable<(TableCache table, int numRows)> RemoveRowsInsertedBeforeTick(long tick)
    {
        foreach (var table in TableCaches)
        {
            var numRows = table.RemoveRowsInsertedBeforeTick(tick);

            if (numRows > 0)
                yield return (table, numRows);
        }
    }

    public void ClearCache()
    {
        foreach (var table in TableCaches)
        {
            table.ClearCache();
        }
    }

    public void Dispose()
    {
        this.CleanCacheWorker.Stop();
        this.ClearCache();
    }
}
