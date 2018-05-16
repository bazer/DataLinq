using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using Slim.Extensions;
using Slim.Metadata;

namespace Slim.Instances
{
    public class RowKey
    {
        public RowKey(DbDataReader reader, Table table)
        {
            Data = ReadReader(reader, table).ToArray();
        }

        public (Column column, object value)[] Data { get; }

        private IEnumerable<(Column column, object value)> ReadReader(DbDataReader reader, Table table)
        {
            foreach (var column in table.Columns.Where(x => x.PrimaryKey))
            {
                yield return (column, reader.ReadColumn(column));
            }
        }

        public bool Equals(RowKey other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;

            return other.Data.Equals(Data);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != typeof(RowKey))
                return false;
            return Equals((RowKey)obj);
        }

        public override int GetHashCode()
        {
            return Data.GetHashCode();
        }
    }
}
