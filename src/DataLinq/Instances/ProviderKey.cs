using System;
using System.Collections.Generic;
using DataLinq.Cache;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public interface IProviderKey
{
    int ValueCount { get; }
    object? GetValue(int index);
}

internal static class ProviderKeyComponents
{
    internal static bool IsNull<TKey>(TKey key)
        where TKey : notnull =>
        key is DataLinqKey { IsNull: true };

    internal static DataLinqKey ToDataLinqKey<TKey>(TKey key)
        where TKey : notnull
    {
        if (key is DataLinqKey dataLinqKey)
            return dataLinqKey;

        return key is IProviderKey providerKey
            ? DataLinqKey.FromProviderKey(providerKey)
            : DataLinqKey.FromValue(key);
    }

    internal static bool HasOnlyIntegralCanonicalComponents(TableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (table.PrimaryKeyColumns.Count == 0)
            return false;

        foreach (var column in table.PrimaryKeyColumns)
        {
            var providerType = column.ProviderClrType;
            if (providerType is null)
                return false;

            providerType = Nullable.GetUnderlyingType(providerType) ?? providerType;
            if (providerType != typeof(sbyte) &&
                providerType != typeof(byte) &&
                providerType != typeof(short) &&
                providerType != typeof(ushort) &&
                providerType != typeof(int) &&
                providerType != typeof(uint) &&
                providerType != typeof(long) &&
                providerType != typeof(ulong))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool SupportsNeutralSourceRowLoading(
        TableDefinition table,
        DatabaseType databaseType)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (HasOnlyIntegralCanonicalComponents(table))
            return true;

        if (table.PrimaryKeyColumns.Count != 1 ||
            databaseType is not (DatabaseType.SQLite or DatabaseType.MySQL or DatabaseType.MariaDB))
        {
            return false;
        }

        var column = table.PrimaryKeyColumns[0];
        return column.IsGuidColumn &&
            column.GetGuidStorageFor(databaseType) is not null &&
            !column.IsGuidStorageUnresolvedFor(databaseType);
    }

    internal static int GetValueCount<TKey>(TKey key)
        where TKey : notnull =>
        key is IProviderKey providerKey ? providerKey.ValueCount : 1;

    internal static object? GetValue<TKey>(TKey key, int index)
        where TKey : notnull
    {
        if (key is IProviderKey providerKey)
            return providerKey.GetValue(index);

        if (index == 0)
            return key;

        throw new IndexOutOfRangeException();
    }

    internal static void ThrowIfComponentCountMismatch<TKey>(
        TKey key,
        int expectedCount,
        string context)
        where TKey : notnull
    {
        var actualCount = GetValueCount(key);
        if (actualCount != expectedCount)
            throw new InvalidOperationException(
                $"{context} has {actualCount} components, expected {expectedCount}.");
    }
}

public interface IProviderKeyRowStoreAccessor
{
    bool TryAddRow(RowCache cache, RowData rowData, IImmutableInstance row);

    /// <summary>
    /// Adds a row using primary-key components captured before provider-to-model conversion. Older
    /// generated accessors retain their legacy row-data behavior; current accessors override this
    /// method to preserve the exact provider-key store.
    /// </summary>
    bool TryAddCanonicalRow(
        RowCache cache,
        DataLinqKey canonicalProviderKey,
        RowData rowData,
        IImmutableInstance row) =>
        TryAddRow(cache, rowData, row);

    bool TryGetRow(RowCache cache, DataLinqKey key, out IImmutableInstance? row);
    bool TryRemoveRow(RowCache cache, DataLinqKey key, out int numRowsRemoved);
    bool TryCreateKey(IRowData rowData, out DataLinqKey key);
    bool TryCreateKey(IModelInstance model, out DataLinqKey key);
}

public interface IProviderKeyDataReaderRowStoreAccessor : IProviderKeyRowStoreAccessor
{
    bool TryGetRow(
        TableCache tableCache,
        IDataLinqDataReader reader,
        IReadOnlyList<int> primaryKeyOrdinals,
        IDataSourceAccess dataSource,
        out IImmutableInstance? row);
}
