using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using DataLinq.Extensions;
using DataLinq.Metadata;

namespace DataLinq.Instances
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

        protected Dictionary<string, object> Data { get; }
        public Table Table { get; }

        public PrimaryKeys GetKeys() => 
            new PrimaryKeys(this);

        public object GetValue(string columnDbName)
        {
            return Data[columnDbName];
        }

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