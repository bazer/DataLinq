using System;
using DataLinq.Metadata;

namespace DataLinq.MySql;

/// <summary>
/// Represents a data writer for MySql database.
/// </summary>
public class SqlDataLinqDataWriter : IDataLinqDataWriter
{
    protected SqlFromMetadataFactory sqlFromMetadataFactory { get; } = new SqlFromMetadataFactory();

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlDataLinqDataWriter"/> class.
    /// </summary>
    public SqlDataLinqDataWriter()
    {
    }

    /// <summary>
    /// Converts the specified value to the appropriate type for the specified column.
    /// </summary>
    /// <param name="column">The column metadata.</param>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public object? ConvertValue(ColumnDefinition column, object? value)
    {
        if (value == null)
            return null;

        if (value is Guid guid)
        {
            var dbType = sqlFromMetadataFactory.GetDbType(column);

            if (dbType.Name == "binary" && dbType.Length == 16)
                return guid.ToByteArray();
        }

        return value;
    }
}
