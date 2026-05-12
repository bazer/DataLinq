using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Cache;

public partial class TableCache
{
    private IEnumerable<RowData> GetRowDataFromPrimaryKeyValues<TKey>(IEnumerable<TKey> keys, IDataSourceAccess dataSource, List<OrderBy>? orderings = null)
        where TKey : notnull
    {
        var keyArray = keys as TKey[] ?? keys.ToArray();
        if (keyArray.Length == 0)
            return [];

        var q = new SqlQuery(Table, dataSource);

        if (Table.PrimaryKeyColumns.Length == 1)
        {
            var pkColumn = Table.PrimaryKeyColumns[0];

            q.Where(pkColumn.DbName)
             .In(keyArray.Select(key => dataSource.Provider.GetWriter().ConvertColumnValue(pkColumn, ProviderKeyComponents.GetValue(key, 0))));
        }
        else
        {
            var first = true;
            foreach (var key in keyArray)
            {
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
                q.OrderBy(order.Column, order.Alias, order.Ascending);
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
            if (reader.GetValue<TKey>(column, 0) is TKey key)
                keys.Add(key);
        }

        return keys;
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
            TableKeyComponentStoreKind.Int32 => reader.GetValue<int>(column, primaryKeyOrdinals[0]),
            TableKeyComponentStoreKind.Int64 => reader.GetValue<long>(column, primaryKeyOrdinals[0]),
            TableKeyComponentStoreKind.Guid => reader.GetValue<Guid>(column, primaryKeyOrdinals[0]),
            TableKeyComponentStoreKind.String => reader.GetValue<string>(column, primaryKeyOrdinals[0]),
            _ => null
        };

        return primaryKey is not null;
    }

    private RowData? GetRowDataFromPrimaryKeyValue<TKey>(TKey key, IDataSourceAccess dataSource)
        where TKey : notnull
    {
        return new SqlQuery(Table, dataSource)
            .Where(Table.PrimaryKeyColumns, key)
            .SelectQuery()
            .ReadFirstRow();
    }
}
