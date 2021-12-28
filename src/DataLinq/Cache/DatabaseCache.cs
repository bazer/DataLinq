using DataLinq.Metadata;
using DataLinq.Mutation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DataLinq.Cache
{
    public class DatabaseCache
    {
        public DatabaseMetadata Database { get; set; }

        public List<TableCache> TableCaches { get; }

        public DatabaseCache(DatabaseMetadata database)
        {
            this.Database = database;

            this.TableCaches =  this.Database.Tables
                .Select(x => new TableCache(x))
                .ToList();
        }

        public void Apply(params StateChange[] changes)
        {
            foreach (var change in changes)
            {
                TableCaches.Single(x => x.Table == change.Table).Apply(change);
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

            foreach (var (limitType, amount) in Database.CacheLimits)
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
    }
}
