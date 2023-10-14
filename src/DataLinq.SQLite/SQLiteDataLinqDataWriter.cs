using DataLinq.Metadata;
using System;

namespace DataLinq.SQLite
{
    public class SQLiteDataLinqDataWriter : IDataLinqDataWriter
    {
        public SQLiteDataLinqDataWriter()
        {
        }

        public object? ConvertValue(Column column, object value)
        {
            if (value == null)
                return null;

            if (value is Guid guid)
            {
                var dbType = SqlFromMetadataFactory.GetDbType(column);

                if (dbType.Name == "binary" && dbType.Length == 16)
                    return guid.ToByteArray();
            }

            return value;
        }
    }
}
