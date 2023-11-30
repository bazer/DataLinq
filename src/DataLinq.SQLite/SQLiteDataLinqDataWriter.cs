using System;
using DataLinq.Metadata;

namespace DataLinq.SQLite
{
    /// <summary>
    /// Represents a data writer for SQLite database.
    /// </summary>
    public class SQLiteDataLinqDataWriter : IDataLinqDataWriter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteDataLinqDataWriter"/> class.
        /// </summary>
        public SQLiteDataLinqDataWriter()
        {
        }

        /// <summary>
        /// Converts the specified value to the appropriate type for the specified column.
        /// </summary>
        /// <param name="column">The column metadata.</param>
        /// <param name="value">The value to convert.</param>
        /// <returns>The converted value.</returns>
        public object? ConvertValue(Column column, object? value)
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
