using Slim.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Slim.Cache
{
    public class DatabaseCache
    {
        public Database Database { get; set; }

        public List<TableCache> TableCaches { get; }

        public DatabaseCache(Database database)
        {
            this.Database = database;

            this.TableCaches =  this.Database.Tables
                .Select(x => new TableCache(x))
                .ToList();
        }

        public void Apply(params TransactionChange[] changes)
        {

        }
    }
}
