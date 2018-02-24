using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using Slim.Metadata;

namespace Slim.Instances
{
    public class InstanceData
    {
        public Dictionary<string, object> Data { get; }
        public Table Table { get; }

        public InstanceData(Dictionary<string, object> data, Table table)
        {
            Data = data;
            Table = table;
        }

        public InstanceData(DbDataReader reader, Table table)
        {
            Table = table;
            Data = ReadReader(reader, table).ToDictionary(x => x.name, x => x.value);
        }

        private IEnumerable<(string name, object value)> ReadReader(DbDataReader reader, Table table)
        {
            foreach (var column in table.Columns)
            {
                var ordinal = reader.GetOrdinal(column.Name);
                yield return (column.Name, reader.GetValue(ordinal));
            }
        }

        //private object GetByType()
    }
}
