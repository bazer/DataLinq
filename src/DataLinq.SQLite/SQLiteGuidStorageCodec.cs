using System;
using DataLinq.Metadata;

namespace DataLinq.SQLite;

internal static class SQLiteGuidStorageCodec
{
    internal static object ToPhysicalValue(ColumnDefinition column, Guid canonicalValue) =>
        GuidCodec.ToPhysicalValue(canonicalValue, GetRequiredDefinition(column).Format);

    internal static Guid FromPhysicalValue(ColumnDefinition column, object physicalValue) =>
        GuidCodec.FromPhysicalValue(physicalValue, GetRequiredDefinition(column).Format);

    private static GuidStorageDefinition GetRequiredDefinition(ColumnDefinition column)
    {
        ArgumentNullException.ThrowIfNull(column);

        var definition = column.GetGuidStorageFor(DatabaseType.SQLite);
        if (definition is not null)
            return definition;

        if (column.IsGuidStorageUnresolvedFor(DatabaseType.SQLite))
        {
            throw new InvalidOperationException(
                $"Column '{column.Table.DbName}.{column.DbName}' has unresolved SQLite UUID storage metadata. " +
                "Declare an explicit SQLite GuidStorage byte order for the ambiguous BLOB column and regenerate the model metadata.");
        }

        throw new InvalidOperationException(
            $"Column '{column.Table.DbName}.{column.DbName}' does not have resolved SQLite UUID storage metadata. " +
            "Regenerate or repair the model metadata before reading or writing the column.");
    }
}
