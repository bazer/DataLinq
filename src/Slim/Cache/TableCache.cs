using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Modl.Db.Query;
using Slim.Instances;
using Slim.Metadata;

namespace Slim.Cache
{
    public class TableCache
    {
        protected ConcurrentDictionary<RowKey, object> Rows = new ConcurrentDictionary<RowKey, object>();

        public TableCache(Table table)
        {
            this.Table = table;
        }

        public int RowCount => Rows.Count;
        public Table Table { get; }

        public IEnumerable<RowKey> GetKeys(Column column, object foreignKey)
        {
            var select = new Select(Table.Database.DatabaseProvider, Table)
                .Where(column.DbName).EqualTo(foreignKey);

            return select
                .ReadKeys();
        }

        public object GetRow(RowKey key)
        {
            var select = new Select(Table.Database.DatabaseProvider, Table);

            foreach (var (column, value) in key.Data)
                select.Where(column.DbName).EqualTo(value);

            return select
                .ReadInstances()
                .Select(InstanceFactory.NewImmutableRow)
                .FirstOrDefault();
        }

        public IEnumerable<object> GetRows(Column column, object foreignKey)
        {
            var keys = column.Index.GetOrAdd(foreignKey, x => GetKeys(column, x).ToArray());

            foreach (var key in keys)
            {
                yield return Rows.GetOrAdd(key, GetRow);
            }
        }
    }
}