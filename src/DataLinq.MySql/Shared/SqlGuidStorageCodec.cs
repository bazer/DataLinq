using System;
using DataLinq.Metadata;

namespace DataLinq.MySql;

internal static class SqlGuidStorageCodec
{
    internal static object ToPhysicalValue(
        ColumnDefinition column,
        DatabaseType databaseType,
        Guid canonicalValue) =>
        GuidCodec.ToPhysicalValue(
            canonicalValue,
            GetRequiredDefinition(column, databaseType).Format);

    internal static GuidStorageDefinition GetRequiredDefinition(
        ColumnDefinition column,
        DatabaseType databaseType)
    {
        ArgumentNullException.ThrowIfNull(column);
        ValidateProvider(databaseType);

        var definition = column.GetGuidStorageFor(databaseType);
        if (definition is not null)
            return definition;

        if (column.IsGuidStorageUnresolvedFor(databaseType))
        {
            throw new InvalidOperationException(
                $"Column '{column.Table.DbName}.{column.DbName}' has unresolved {databaseType} UUID storage metadata. " +
                "Declare an explicit binary GuidStorage byte order and regenerate the model metadata.");
        }

        throw new InvalidOperationException(
            $"Column '{column.Table.DbName}.{column.DbName}' does not have resolved {databaseType} UUID storage metadata. " +
            "Regenerate or repair the model metadata before reading or writing the column.");
    }

    private static void ValidateProvider(DatabaseType databaseType)
    {
        if (databaseType is DatabaseType.MySQL or DatabaseType.MariaDB)
            return;

        throw new ArgumentOutOfRangeException(
            nameof(databaseType),
            databaseType,
            "The SQL UUID storage codec only supports MySQL and MariaDB.");
    }
}
