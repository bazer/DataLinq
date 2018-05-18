using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Slim.Extensions;
using Slim.Metadata;

namespace Slim.Instances
{
    public class RowData
    {
        public RowData(Dictionary<string, object> data, Table table)
        {
            Data = data;
            Table = table;
        }

        public RowData(DbDataReader reader, Table table)
        {
            Table = table;
            Data = ReadReader(reader, table).ToDictionary(x => x.name, x => x.value);
        }

        public Dictionary<string, object> Data { get; }
        public Table Table { get; }

        public RowKey GetKey() => 
            new RowKey(this);

        private IEnumerable<(string name, object value)> ReadReader(DbDataReader reader, Table table)
        {
            foreach (var column in table.Columns)
            {
                var value = reader.ReadColumn(column);

                yield return (column.DbName, value);
            }
        }
    }
}