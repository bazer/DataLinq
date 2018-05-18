using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Modl.Db.Query;
using Slim.Extensions;
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
                .What(Table.Columns.Where(x => x.PrimaryKey))
                .Where(column.DbName).EqualTo(foreignKey);

            return select
                .ReadKeys();
        }

        public IEnumerable<RowData> GetRowData(IEnumerable<RowKey> keys)
        {
            var select = new Select(Table.Database.DatabaseProvider, Table);

            var query = new StringBuilder()
                .Append("SELECT * FROM ")
                .Append(Table.DbName)
                .Append(" WHERE ")
                .Append(keys
                    .Select(x => $"({x.Data.Select(y => $"{y.column.DbName} = '{y.value}'").ToJoinedString(" AND ")})")
                    .ToJoinedString(" OR "));

            return Table.Database.DatabaseProvider.ReadReader(query.ToString())
                .Select(x => new RowData(x, Table));
        }

        public IEnumerable<object> GetRows(Column column, object foreignKey)
        {
            var keys = column.Index.GetOrAdd(foreignKey, x => GetKeys(column, x).ToArray());


            var keysToLoad = new List<RowKey>(keys.Length);
            foreach (var key in keys)
            {
                if (Rows.TryGetValue(key, out object row))
                    yield return row;
                else
                    keysToLoad.Add(key);
            }

            if (keysToLoad.Count < keys.Length / 2)
            {
                foreach (var split in keysToLoad.splitList(100))
                {
                    foreach (var rowData in GetRowData(split))
                    {
                        var row = InstanceFactory.NewImmutableRow(rowData);

                        if (!Rows.TryAdd(rowData.GetKey(), row))
                            throw new Exception("Couldn't add row");

                        yield return row;
                    }
                }
            }
            else if (keysToLoad.Count != 0)
            {
                var key = new RowKey((column, foreignKey));
                foreach (var rowData in GetRowData(key.Yield()))
                {
                    var row = InstanceFactory.NewImmutableRow(rowData);

                    if (Rows.TryAdd(rowData.GetKey(), row))
                        yield return row;
                }
            }
        }
    }
}