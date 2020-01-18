using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Slim.Metadata;

namespace Slim.Instances
{
    public class PrimaryKeys
    {
        public PrimaryKeys(DbDataReader reader, Table table)
        {
            Data = ReadReader(reader, table).ToArray();
        }

        public PrimaryKeys(RowData row)
        {
            Data = ReadRow(row).ToArray();
        }

        public PrimaryKeys(params object[] data)
        {
            Data = data;
        }

        public PrimaryKeys(IEnumerable<object> data)
        {
            Data = data.ToArray();
        }

        public object[] Data { get; }
        //public (Column column, object value)[] Data { get; }

        public bool Equals(PrimaryKeys other)
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
            if (obj.GetType() != typeof(PrimaryKeys))
                return false;

            return ArraysEqual(Data, (obj as PrimaryKeys).Data);
            //return Data.SequenceEqual((obj as RowKey).Data);
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
                for (int i = 0; i < Data.Length; i++)
                {
                    hash = hash * 31 + Data[i].GetHashCode();
                }
                return hash;
            }
        }

        static bool ArraysEqual<T>(T[] a1, T[] a2)
        {
            if (a1.Length == a2.Length)
            {
                for (int i = 0; i < a1.Length; i++)
                {
                    if (!a1[i].Equals(a2[i]))
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        private IEnumerable<object> ReadReader(DbDataReader reader, Table table)
        {
            foreach (var column in table.PrimaryKeyColumns)
                yield return reader.ReadColumn(column);
        }

        private IEnumerable<object> ReadRow(RowData row)
        {
            foreach (var column in row.Table.PrimaryKeyColumns)
                yield return row.GetValue(column.DbName);
        }
    }
}