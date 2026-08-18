using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Cache;

public partial class TableCache
{
    private IEnumerable<RowData> GetRowDataFromPrimaryKeyValues<TKey>(
        IReadOnlyList<TKey> keys,
        int offset,
        int count,
        IDataSourceAccess dataSource,
        List<OrderBy>? orderings = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > keys.Count - count)
            throw new ArgumentException("The requested primary-key slice exceeds the collection bounds.");
        if (count == 0)
            return [];

        var q = new SqlQuery(Table, dataSource);

        if (Table.PrimaryKeyColumns.Length == 1)
        {
            var pkColumn = Table.PrimaryKeyColumns[0];

            var values = new object?[count];
            for (var index = 0; index < count; index++)
            {
                values[index] = dataSource.Provider.GetWriter().ConvertColumnValue(
                    pkColumn,
                    ProviderKeyComponents.GetValue(keys[offset + index], 0));
            }

            q.Where(pkColumn.DbName).In(values);
        }
        else
        {
            var first = true;
            var exclusiveEnd = offset + count;
            for (var keyIndex = offset; keyIndex < exclusiveEnd; keyIndex++)
            {
                var key = keys[keyIndex];
                ProviderKeyComponents.ThrowIfComponentCountMismatch(
                    key,
                    primaryKeyColumnsCount,
                    $"Provider key for table '{Table.DbName}'");

                var keySpecificAndGroup = q.AddWhereGroup(first ? BooleanType.And : BooleanType.Or);
                first = false;

                for (var i = 0; i < primaryKeyColumnsCount; i++)
                {
                    var pkColumn = Table.PrimaryKeyColumns[i];
                    keySpecificAndGroup.Where(pkColumn.DbName)
                        .EqualTo(dataSource.Provider.GetWriter().ConvertColumnValue(pkColumn, ProviderKeyComponents.GetValue(key, i)));
                }
            }
        }

        if (orderings != null)
        {
            foreach (var order in orderings)
            {
                var column = order.Column ?? throw new InvalidOperationException("Cached row loading requires column-backed orderings.");
                q.OrderBy(column, order.Alias, order.Ascending);
            }
        }

        return q
            .SelectQuery()
            .ReadRows();
    }

    private static List<TKey> ReadScalarPrimaryKeys<TSelect, TKey>(Select<TSelect> select, ColumnDefinition column)
        where TKey : notnull
    {
        var keys = new List<TKey>();
        foreach (var reader in select.ReadReader())
        {
            if (ReadScalarProviderKey<TKey>(reader, column, 0) is TKey key)
                keys.Add(key);
        }

        return keys;
    }

    private static TKey? ReadScalarProviderKey<TKey>(
        IDataLinqDataReader reader,
        ColumnDefinition column,
        int ordinal)
        where TKey : notnull
    {
        if (!column.HasScalarConverter)
            return reader.GetValue<TKey>(column, ordinal);

        var canonicalValue = ProviderRowDecoder.DecodeCanonicalValue(
            reader,
            column,
            ordinal,
            "cache:scalar-primary-key",
            useColumnAwareGuid: true);
        return canonicalValue is null ? default : (TKey)canonicalValue;
    }

    private DataLinqKey ReadPrimaryKey(IDataLinqDataReader reader, IReadOnlyList<int> primaryKeyOrdinals)
    {
        if (primaryKeyColumnsCount == 1)
            return DataLinqKey.FromValue(reader.GetValue<object>(Table.PrimaryKeyColumns[0], primaryKeyOrdinals[0]));

        var values = new object?[primaryKeyColumnsCount];
        for (var i = 0; i < values.Length; i++)
            values[i] = reader.GetValue<object>(Table.PrimaryKeyColumns[i], primaryKeyOrdinals[i]);

        return DataLinqKey.FromValues(values);
    }

    private bool TryReadScalarPrimaryKeyValue(
        IDataLinqDataReader reader,
        IReadOnlyList<int> primaryKeyOrdinals,
        out object? primaryKey)
    {
        primaryKey = null;
        if (!Table.PrimaryKeyShape.SupportsScalarProviderKeyStore || primaryKeyOrdinals.Count != 1)
            return false;

        var column = Table.PrimaryKeyColumns[0];
        primaryKey = Table.PrimaryKeyShape[0].ProviderStoreKind switch
        {
            TableKeyComponentStoreKind.Int32 => ReadScalarProviderKey<int>(reader, column, primaryKeyOrdinals[0]),
            TableKeyComponentStoreKind.Int64 => ReadScalarProviderKey<long>(reader, column, primaryKeyOrdinals[0]),
            TableKeyComponentStoreKind.Guid => ReadScalarProviderKey<Guid>(reader, column, primaryKeyOrdinals[0]),
            TableKeyComponentStoreKind.String => ReadScalarProviderKey<string>(reader, column, primaryKeyOrdinals[0]),
            _ => null
        };

        return primaryKey is not null;
    }

    private RowData? GetRowDataFromPrimaryKeyValue<TKey>(TKey key, IDataSourceAccess dataSource)
        where TKey : notnull
    {
        if (TryConvertScalarProviderColumnValue(key, Table.PrimaryKeyColumns, dataSource, out var primaryKeyColumn, out var scalarKey))
            return new ScalarColumnRowsQuery(Table, dataSource, primaryKeyColumn, scalarKey)
                .ReadFirstRow();

        return new SqlQuery(Table, dataSource)
            .Where(Table.PrimaryKeyColumns, key)
            .SelectQuery()
            .ReadFirstRow();
    }

    private static bool TryConvertScalarProviderColumnValue<TKey>(
        TKey key,
        IReadOnlyList<ColumnDefinition> columns,
        IDataSourceAccess dataSource,
        out ColumnDefinition column,
        out object? value)
        where TKey : notnull
    {
        column = null!;
        value = null;
        if (columns.Count != 1)
            return false;

        column = columns[0];
        if (!TryGetRawScalarProviderColumnValue(key, TableKeyShape.GetProviderStoreKind(column), out var rawValue))
            return false;

        value = dataSource.Provider.GetWriter().ConvertColumnValue(column, rawValue);
        return true;
    }

    private static bool TryGetRawScalarProviderColumnValue<TKey>(
        TKey key,
        TableKeyComponentStoreKind storeKind,
        out object? value)
        where TKey : notnull
    {
        value = key;
        if (key is IProviderKey providerKey)
        {
            if (providerKey.ValueCount != 1)
                return false;

            value = providerKey.GetValue(0);
        }

        value = storeKind switch
        {
            TableKeyComponentStoreKind.Int32 when value is int intValue => intValue,
            TableKeyComponentStoreKind.Int64 when value is long longValue => longValue,
            TableKeyComponentStoreKind.Guid when value is Guid guidValue => guidValue,
            TableKeyComponentStoreKind.String when value is string stringValue => stringValue,
            _ => null
        };

        return value is not null;
    }

}
