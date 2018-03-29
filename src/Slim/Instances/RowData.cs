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

        private IEnumerable<(string name, object value)> ReadReader(DbDataReader reader, Table table)
        {
            foreach (var column in table.Columns)
            {
                var ordinal = reader.GetOrdinal(column.DbName);
                var value = reader.GetValue(ordinal);

                if (value is DBNull)
                    value = null;
                else if (column.CsNullable)
                    value = Convert.ChangeType(value, column.CsType.GetNullableConversionType());
                else if (value.GetType() != column.CsType)
                    value = Convert.ChangeType(value, column.CsType);

                yield return (column.CsName, value);
            }
        }
    }
}