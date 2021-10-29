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
    }
}
