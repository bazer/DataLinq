using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        if (TryConvertScalarProviderPrimaryKey(key, dataSource, out var scalarKey))
            return new ScalarPrimaryKeyRowQuery(Table, dataSource, Table.PrimaryKeyColumns[0], scalarKey)
                .ReadFirstRow();

        return new SqlQuery(Table, dataSource)
            .Where(Table.PrimaryKeyColumns, key)
            .SelectQuery()
            .ReadFirstRow();
    }

    private bool TryConvertScalarProviderPrimaryKey<TKey>(
        TKey key,
        IDataSourceAccess dataSource,
        out object? value)
        where TKey : notnull
    {
        value = null;
        if (!Table.PrimaryKeyShape.SupportsScalarProviderKeyStore ||
            primaryKeyColumnsCount != 1 ||
            key is IProviderKey)
            return false;

        var primaryKeyColumn = Table.PrimaryKeyColumns[0];
        object? rawValue = Table.PrimaryKeyShape[0].ProviderStoreKind switch
        {
            TableKeyComponentStoreKind.Int32 when key is int intKey => intKey,
            TableKeyComponentStoreKind.Int64 when key is long longKey => longKey,
            TableKeyComponentStoreKind.Guid when key is Guid guidKey => guidKey,
            TableKeyComponentStoreKind.String when key is string stringKey => stringKey,
            _ => null
        };

        if (rawValue is null)
            return false;

        value = dataSource.Provider.GetWriter().ConvertColumnValue(primaryKeyColumn, rawValue);
        return true;
    }

    private sealed class ScalarPrimaryKeyRowQuery(
        TableDefinition table,
        IDataSourceAccess dataSource,
        ColumnDefinition primaryKeyColumn,
        object? primaryKeyValue) : IQuery
    {
        private const int MaxCachedSqlTexts = 128;
        private static readonly ConcurrentDictionary<ScalarPrimaryKeyRowQueryTemplateKey, string> SqlTextCache = new();
        private static int sqlTextCacheEntryCount;

        public Sql ToSql(string? paramPrefix = null)
        {
            var parameterName = (paramPrefix ?? string.Empty) + "w0";
            var sql = new Sql(GetSqlText(parameterName));
            dataSource.Provider.GetParameter(sql, parameterName, primaryKeyValue);

            return sql;
        }

        private string GetSqlText(string parameterName)
        {
            var key = new ScalarPrimaryKeyRowQueryTemplateKey(
                dataSource.Provider.GetType(),
                dataSource.Provider.DatabaseType,
                dataSource.Provider.DatabaseName,
                table,
                primaryKeyColumn,
                dataSource.Provider.Constants.EscapeCharacter,
                parameterName);

            if (SqlTextCache.TryGetValue(key, out var cachedSqlText))
                return cachedSqlText;

            var sqlText = RenderSqlText(parameterName);
            if (SqlTextCache.TryAdd(key, sqlText) &&
                Interlocked.Increment(ref sqlTextCacheEntryCount) > MaxCachedSqlTexts)
            {
                SqlTextCache.Clear();
                Interlocked.Exchange(ref sqlTextCacheEntryCount, 0);
            }

            return sqlText;
        }

        private string RenderSqlText(string parameterName)
        {
            var sql = new Sql().AddText("SELECT ");
            AddSelectedColumns(sql);
            sql.AddText(" FROM ");
            dataSource.Provider.GetTableName(sql, table.DbName);
            sql.AddText("\nWHERE\n");
            AddColumn(sql, primaryKeyColumn);
            sql.AddText(" ");
            sql.AddText(dataSource.Provider.GetOperatorSql(Operator.Equal));
            sql.AddText(" ");
            dataSource.Provider.GetParameterValue(sql, parameterName);

            return sql.Text;
        }

        public RowData? ReadFirstRow()
        {
            using var command = dataSource.Provider.ToDbCommand(this);
            using var reader = dataSource.DatabaseAccess.ExecuteReader(command);

            return reader.ReadNextRow()
                ? new RowData(reader, table, table.Columns, true)
                : null;
        }

        private void AddSelectedColumns(Sql sql)
        {
            for (var i = 0; i < table.Columns.Length; i++)
            {
                if (i > 0)
                    sql.AddText(", ");

                AddColumn(sql, table.Columns[i]);
            }
        }

        private void AddColumn(Sql sql, ColumnDefinition column)
        {
            var escapeCharacter = dataSource.Provider.Constants.EscapeCharacter;
            sql.AddText(escapeCharacter);
            sql.AddText(column.DbName);
            sql.AddText(escapeCharacter);
        }

        private readonly record struct ScalarPrimaryKeyRowQueryTemplateKey(
            Type ProviderType,
            DatabaseType DatabaseType,
            string DatabaseName,
            TableDefinition Table,
            ColumnDefinition PrimaryKeyColumn,
            string EscapeCharacter,
            string ParameterName);
    }
}
