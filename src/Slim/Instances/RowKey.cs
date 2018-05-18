using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Slim.Metadata;

namespace Slim.Instances
{
    public class RowKey
    {
        public RowKey(DbDataReader reader, Table table)
        {
            Data = ReadReader(reader, table).ToArray();
        }

        public RowKey(RowData row)
        {
            Data = ReadRow(row).ToArray();
        }

        public RowKey(params (Column column, object value)[] data)
        {
            Data = data;
        }

        public (Column column, object value)[] Data { get; }

        public bool Equals(RowKey other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;

            return Data.SequenceEqual(other.Data);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != typeof(RowKey))
                return false;

            return Data.SequenceEqual((obj as RowKey).Data);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                if (Data == null)
                {
                    return 0;
                }
                int hash = 17;
                foreach (var element in Data)
                {
                    hash = hash * 31 + element.GetHashCode();
                }
                return hash;
            }
        }

        private IEnumerable<(Column column, object value)> ReadReader(DbDataReader reader, Table table)
        {
            foreach (var column in table.Columns.Where(x => x.PrimaryKey))
                yield return (column, reader.ReadColumn(column));
        }

        private IEnumerable<(Column column, object value)> ReadRow(RowData row)
        {
            foreach (var column in row.Table.Columns.Where(x => x.PrimaryKey))
                yield return (column, row.Data[column.DbName]);
        }
    }
}