using DataLinq.Metadata;
using System;

namespace DataLinq.MySql
{
    public class MySqlDataLinqDataWriter : IDataLinqDataWriter
    {
        public MySqlDataLinqDataWriter()
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
