using DataLinq.Attributes;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Workers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DataLinq.Cache
{
    public class DatabaseCache : IDisposable
    {
        public IDatabaseProvider Database { get; set; }

        public List<TableCache> TableCaches { get; }

        public CleanCacheWorker CleanCacheWorker { get; }

        public DatabaseCache(IDatabaseProvider database)
        {
            this.Database = database;

            this.TableCaches =  this.Database.Metadata.Tables
                .Select(x => new TableCache(x, database))
                .ToList();

            this.CleanCacheWorker = new CleanCacheWorker(database, new LongRunningTaskCreator(), TimeSpan.FromMinutes(5));
            this.CleanCacheWorker.Start();
        }

        public void ApplyChanges(IEnumerable<StateChange> changes, Transaction transaction = null)
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
                table.ClearRows();
            }
        }

        public void Dispose()
        {
            this.CleanCacheWorker.Stop();
            this.ClearCache();
        }
    }
}
