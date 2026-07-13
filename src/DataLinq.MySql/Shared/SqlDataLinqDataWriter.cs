using System;
using DataLinq.Metadata;

namespace DataLinq.MySql;

/// <summary>
/// Represents a data writer for MySql database.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SqlDataLinqDataWriter"/> class.
/// </remarks>
public class SqlDataLinqDataWriter(SqlFromMetadataFactory sqlFromMetadataFactory) : IDataLinqDataWriter
{
    protected SqlFromMetadataFactory SqlFromMetadataFactory { get; } = sqlFromMetadataFactory;


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

        if (column.IsGuidColumn &&
            !column.PrimaryKey &&
            value is Guid guid)
            return SqlGuidStorageCodec.ToPhysicalValue(
                column,
                SqlFromMetadataFactory.ProviderDatabaseType,
                guid);

        if (value is Guid legacyGuid)
        {
            var dbType = SqlFromMetadataFactory.GetDbType(column);

            if (dbType.Name == "uuid" || (dbType.Name == "char" && dbType.Length == 36))
                return legacyGuid.ToString();

            if (dbType.Name == "binary" && dbType.Length == 16)
                return legacyGuid.ToByteArray();
        }

        return value;
    }
}
