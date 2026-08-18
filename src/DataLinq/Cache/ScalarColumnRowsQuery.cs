using System;
using System.Collections.Concurrent;
using System.Data;
using System.Threading;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Cache;

/// <summary>
/// Bounded, parameterized equality query used by cache-cold scalar lookups. SQL text is cached by
/// provider and metadata identity; each invocation owns only its parameter value and command.
/// </summary>
internal sealed class ScalarColumnRowsQuery(
    TableDefinition table,
    IDataSourceAccess dataSource,
    ColumnDefinition predicateColumn,
    object? predicateValue) : IQuery
{
    private const int MaxCachedSqlTexts = 128;
    private static readonly ConcurrentDictionary<ScalarColumnRowsQueryTemplateKey, string> SqlTextCache = new();
    private static int sqlTextCacheEntryCount;

    public Sql ToSql(string? paramPrefix = null)
    {
        var parameterName = (paramPrefix ?? string.Empty) + "w0";
        var sql = new Sql(GetSqlText(parameterName));
        dataSource.Provider.GetParameter(sql, parameterName, predicateValue);

        return sql;
    }

    public IDbCommand ToDbCommand() => dataSource.Provider.ToDbCommand(this);

    internal RowData? ReadFirstRow()
    {
        DataSourceAccess.EnsureReadAllowed(dataSource, "read a cache row");
        using var command = ToDbCommand();
        using var reader = dataSource.DatabaseAccess.ExecuteReader(command);

        return reader.ReadNextRow()
            ? new RowData(
                reader,
                table,
                table.Columns,
                true,
                $"sql:{dataSource.Provider.DatabaseType}:cache-scalar-row")
            : null;
    }

    private string GetSqlText(string parameterName)
    {
        var key = new ScalarColumnRowsQueryTemplateKey(
            dataSource.Provider.GetType(),
            dataSource.Provider.DatabaseType,
            dataSource.Provider.DatabaseName,
            table,
            predicateColumn,
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
        AddColumn(sql, predicateColumn);
        sql.AddText(" ");
        sql.AddText(dataSource.Provider.GetOperatorSql(Operator.Equal));
        sql.AddText(" ");
        dataSource.Provider.GetParameterValue(sql, parameterName);

        return sql.Text;
    }

    private void AddSelectedColumns(Sql sql)
    {
        for (var index = 0; index < table.Columns.Length; index++)
        {
            if (index > 0)
                sql.AddText(", ");

            AddColumn(sql, table.Columns[index]);
        }
    }

    private void AddColumn(Sql sql, ColumnDefinition column)
    {
        var escapeCharacter = dataSource.Provider.Constants.EscapeCharacter;
        sql.AddText(escapeCharacter);
        sql.AddText(column.DbName);
        sql.AddText(escapeCharacter);
    }

    private readonly record struct ScalarColumnRowsQueryTemplateKey(
        Type ProviderType,
        DatabaseType DatabaseType,
        string DatabaseName,
        TableDefinition Table,
        ColumnDefinition PredicateColumn,
        string EscapeCharacter,
        string ParameterName);
}
