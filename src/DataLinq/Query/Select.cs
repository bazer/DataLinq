using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq.Query;

public class Select<T> : IQuery
{
    protected readonly SqlQuery<T> query;
    public SqlQuery<T> Query => query;

    public Select(SqlQuery<T> query)
    {
        this.query = query;
    }

    public Sql ToSql(string? paramPrefix = null)
    {
        var canUseTemplate = query.TryGetTemplateKey(paramPrefix, out var key, out var values);
        if (canUseTemplate &&
            SelectSqlTemplateCache.TryGet(key, out var template))
        {
            return template.Bind(values);
        }

        var sql = RenderSql(paramPrefix);

        if (canUseTemplate && sql.Parameters.Count == values.Length)
        {
            var parameterNames = new string[sql.Parameters.Count];
            for (var i = 0; i < parameterNames.Length; i++)
                parameterNames[i] = sql.Parameters[i].ParameterName;

            SelectSqlTemplateCache.TryAdd(key, new SelectSqlTemplate(sql.Text, parameterNames));
        }

        return sql;
    }

    private Sql RenderSql(string? paramPrefix)
    {
        var sql = new Sql().AddText("SELECT ");
        AddSelectedColumns(sql);
        sql.AddText(" FROM ");
        AddSource(sql);
        query.GetJoins(sql, paramPrefix);
        query.GetWhere(sql, paramPrefix);
        query.GetGroupBy(sql);
        query.GetHaving(sql, paramPrefix);
        query.GetOrderBy(sql);
        query.GetLimit(sql);

        return sql;
    }

    private void AddSource(Sql sql)
    {
        if (query.DerivedSourceSql is not { } derivedSourceSql)
        {
            query.AddTableName(sql, query.Table.DbName, query.Alias);
            return;
        }

        sql.AddText("(");
        sql.AddText(derivedSourceSql.Text);
        sql.AddText(") ");
        sql.AddText(query.Alias ?? throw new InvalidOperationException("A derived query source requires an alias."));
        sql.Parameters.AddRange(derivedSourceSql.Parameters);
    }

    private void AddSelectedColumns(Sql sql)
    {
        var alias = string.IsNullOrWhiteSpace(query.Alias) ? null : query.Alias;
        var whatList = query.WhatList;
        if (whatList is not null)
        {
            for (var i = 0; i < whatList.Count; i++)
            {
                AddColumnSeparator(sql, i);
                if (whatList[i].StartsWith(query.EscapeCharacter, StringComparison.Ordinal))
                    AddColumnPrefix(sql, alias);

                sql.AddText(whatList[i]);
            }

            return;
        }

        var columns = query.Table.Columns;
        for (var i = 0; i < columns.Length; i++)
        {
            AddColumnSeparator(sql, i);
            AddColumnPrefix(sql, alias);
            sql.AddText(query.EscapeCharacter);
            sql.AddText(columns[i].DbName);
            sql.AddText(query.EscapeCharacter);
        }
    }

    private static void AddColumnSeparator(Sql sql, int index)
    {
        if (index > 0)
            sql.AddText(", ");
    }

    private static void AddColumnPrefix(Sql sql, string? alias)
    {
        if (alias is null)
            return;

        sql.AddText(alias);
        sql.AddText(".");
    }

    public IDbCommand ToDbCommand()
    {
        return query.DataSource.Provider.ToDbCommand(this);
    }

    public Select<T> What(IEnumerable<ColumnDefinition> columns)
    {
        query.What(columns);

        return this;
    }

    public Select<T> What(params string[] selectors)
    {
        query.What(selectors);

        return this;
    }

    public IEnumerable<IDataLinqDataReader> ReadReader()
    {
        return query.DataSource
            .DatabaseAccess
            .ReadReader(query.DataSource.Provider.ToDbCommand(this));
    }

    public IEnumerable<RowData> ReadRows()
    {
        // Resolve the actual columns being fetched to ensure the RowData 
        // reader aligns with the DataReader's fields.
        var columnsToRead = GetColumnsToRead();

        foreach (var reader in ReadReader())
            yield return new RowData(reader, query.Table, columnsToRead, true);
    }

    public RowData? ReadFirstRow()
    {
        // Resolve the actual columns being fetched to ensure the RowData
        // reader aligns with the DataReader's fields.
        var columnsToRead = GetColumnsToRead();

        using var command = query.DataSource.Provider.ToDbCommand(this);
        using var reader = query.DataSource.DatabaseAccess.ExecuteReader(command);

        return reader.ReadNextRow()
            ? new RowData(reader, query.Table, columnsToRead, true)
            : null;
    }

    private IReadOnlyList<ColumnDefinition> GetColumnsToRead()
    {
        // If no specific columns requested, return all (default SELECT *)
        if (query.WhatList == null || query.WhatList.Count == 0)
            return query.Table.Columns;

        // Map the string selectors in WhatList back to ColumnDefinitions.
        // We have to handle potential escaping characters in the WhatList strings.
        var escape = query.EscapeCharacter;
        var definitions = new List<ColumnDefinition>(query.WhatList.Count);

        foreach (var what in query.WhatList)
        {
            // Strip escape characters to match against DbName
            var cleanName = what.Replace(escape, "");

            // Find the matching column. 
            // Note: If 'what' is a raw SQL expression (e.g. "COUNT(*)"), this will be null.
            // RowData is designed for Entity Materialization, so it expects mapped columns.
            var col = query.Table.TryGetColumnByDbName(cleanName, out var exactColumn)
                ? exactColumn
                : query.Table.TryGetColumnByDbName(cleanName, StringComparison.OrdinalIgnoreCase, out var ignoreCaseColumn)
                    ? ignoreCaseColumn
                    : null;

            if (col != null)
            {
                definitions.Add(col);
            }
            else
            {
                // If we can't map it to a definition, we can't store it in the optimized RowData array.
                // For now, we skip unmapped columns (like aggregates) as they are usually handled by ExecuteScalar 
                // or specific projections that don't go through the standard RowData path.
                // However, to keep the Ordinal alignment correct in RowData.ReadReader, 
                // we technically shouldn't use RowData for arbitrary projections anymore.
                // But for standard "Select specific columns" scenarios, this works.
            }
        }

        // If we found NO matching columns (e.g. only aggregates), return empty
        // This effectively means RowData will be empty/useless, which is expected for purely aggregate queries.
        return definitions;
    }

    private List<OrderBy> GetCacheOrderings()
        => query.OrderByList
            .Where(static ordering => ordering.Column is not null)
            .Select(static ordering => new OrderBy(ordering.Column!, alias: null, ordering.Ascending))
            .ToList();

    public IEnumerable<DataLinqKey> ReadKeys()
    {
        return KeyFactory.GetKeys(this, query.Table.PrimaryKeyColumns);
    }

    //public IEnumerable<DataLinqKey> ReadForeignKeys(ColumnIndex foreignKeyIndex)
    //{
    //    return ReadReader()
    //        .Select(x => new RowData(x, query.Table, foreignKeyIndex.Columns.AsSpan()))
    //        .Select(x => new ForeignKey(foreignKeyIndex, x.GetValues(foreignKeyIndex.Columns).ToArray()));
    //}

    public IEnumerable<(DataLinqKey fk, DataLinqKey[] pks)> ReadPrimaryAndForeignKeys(ColumnIndex foreignKeyIndex)
    {
        var columnsToRead = GetPrimaryAndForeignKeyColumns(foreignKeyIndex);
        var primaryKeysByForeignKey = new Dictionary<DataLinqKey, List<DataLinqKey>>();

        foreach (var reader in ReadReader())
        {
            var row = new RowData(reader, query.Table, columnsToRead, false);
            var foreignKey = KeyFactory.GetKey(row, foreignKeyIndex.Columns);
            var primaryKey = KeyFactory.GetKey(row, query.Table.PrimaryKeyColumns);

            if (!primaryKeysByForeignKey.TryGetValue(foreignKey, out var primaryKeys))
            {
                primaryKeys = [];
                primaryKeysByForeignKey.Add(foreignKey, primaryKeys);
            }

            primaryKeys.Add(primaryKey);
        }

        foreach (var group in primaryKeysByForeignKey)
            yield return (group.Key, group.Value.ToArray());
    }

    private IReadOnlyList<ColumnDefinition> GetPrimaryAndForeignKeyColumns(ColumnIndex foreignKeyIndex)
    {
        var primaryKeyColumns = query.Table.PrimaryKeyColumns;
        var foreignKeyColumns = foreignKeyIndex.Columns;
        var columns = new List<ColumnDefinition>(primaryKeyColumns.Count + foreignKeyColumns.Count);

        for (var i = 0; i < primaryKeyColumns.Count; i++)
            columns.Add(primaryKeyColumns[i]);

        for (var i = 0; i < foreignKeyColumns.Count; i++)
        {
            var foreignKeyColumn = foreignKeyColumns[i];
            if (!columns.Contains(foreignKeyColumn))
                columns.Add(foreignKeyColumn);
        }

        return columns;
    }

    public IEnumerable<V> ExecuteAs<V>() =>
        Execute().Select(x => (V)x);

    public IEnumerable<IImmutableInstance> Execute()
    {
        var telemetryContext = DataLinqTelemetryContext.FromProvider(query.DataSource.Provider);
        var activity = DataLinqTelemetry.StartQueryActivity(
            telemetryContext,
            query.Table.DbName,
            "entity",
            query.DataSource is Mutation.Transaction);
        var startedAt = Stopwatch.GetTimestamp();
        var succeeded = false;

        DataLinqMetrics.RecordEntityQueryExecution(query.DataSource.Provider);

        try
        {
            if (query.Table.PrimaryKeyColumns.Length != 0)
            {
                var tableCache = query.DataSource.Provider.GetTableCache(query.Table);

                if (query.TryGetSimpleScalarPrimaryKey(out var simpleScalarKey) &&
                    tableCache.TryGetRowFromProviderKeyValue(simpleScalarKey, query.DataSource, out var scalarRow))
                {
                    if (scalarRow is not null)
                        yield return scalarRow;
                }
                else if (query.TryGetSimplePrimaryKey() is DataLinqKey simpleKey)
                {
                    var row = tableCache.GetRow(simpleKey, query.DataSource);
                    if (row is not null)
                        yield return row;
                }
                else if (!query.HasDerivedSource &&
                    !query.HasJoins &&
                    tableCache.TryGetRowsFromScalarPrimaryKeyQuery(this, query.DataSource, GetCacheOrderings(), out var providerKeyRows))
                {
                    foreach (var row in providerKeyRows)
                        yield return row;
                }
                else
                {
                    this.What(query.Table.PrimaryKeyColumns);
                    var keys = this.ReadKeys().ToArray();
                    var orderings = query.HasJoins ? null : GetCacheOrderings();

                    foreach (var row in tableCache.GetRows(keys, query.DataSource, orderings: orderings))
                        yield return row;
                }
            }
            else
            {
                foreach (var rowData in this.ReadRows())
                    yield return InstanceFactory.NewImmutableRow(rowData, query.DataSource);
            }

            succeeded = true;
        }
        finally
        {
            var duration = Stopwatch.GetElapsedTime(startedAt);
            DataLinqTelemetry.RecordQueryExecution(
                telemetryContext,
                query.Table.DbName,
                "entity",
                query.DataSource is Mutation.Transaction,
                succeeded,
                duration);

            if (activity is not null)
            {
                if (!succeeded)
                    activity.SetStatus(ActivityStatusCode.Error);

                activity.SetTag("datalinq.outcome", succeeded ? "success" : "failure");
                activity.Dispose();
            }
        }
    }

    public V ExecuteScalar<V>()
    {
        var telemetryContext = DataLinqTelemetryContext.FromProvider(query.DataSource.Provider);
        var activity = DataLinqTelemetry.StartQueryActivity(
            telemetryContext,
            query.Table.DbName,
            "scalar",
            query.DataSource is Mutation.Transaction);
        var startedAt = Stopwatch.GetTimestamp();
        var succeeded = false;

        DataLinqMetrics.RecordScalarQueryExecution(query.DataSource.Provider);

        try
        {
            var result = query.DataSource.DatabaseAccess.ExecuteScalar<V>(query.DataSource.Provider.ToDbCommand(this));
            succeeded = true;
            return result;
        }
        catch (Exception exception)
        {
            DataLinqTelemetry.RecordException(activity, exception);
            throw;
        }
        finally
        {
            var duration = Stopwatch.GetElapsedTime(startedAt);
            DataLinqTelemetry.RecordQueryExecution(
                telemetryContext,
                query.Table.DbName,
                "scalar",
                query.DataSource is Mutation.Transaction,
                succeeded,
                duration);

            if (activity is not null)
            {
                activity.SetTag("datalinq.outcome", succeeded ? "success" : "failure");
                activity.Dispose();
            }
        }
    }

    public object? ExecuteScalar()
    {
        var telemetryContext = DataLinqTelemetryContext.FromProvider(query.DataSource.Provider);
        var activity = DataLinqTelemetry.StartQueryActivity(
            telemetryContext,
            query.Table.DbName,
            "scalar",
            query.DataSource is Mutation.Transaction);
        var startedAt = Stopwatch.GetTimestamp();
        var succeeded = false;

        DataLinqMetrics.RecordScalarQueryExecution(query.DataSource.Provider);

        try
        {
            var result = query.DataSource.DatabaseAccess.ExecuteScalar(query.DataSource.Provider.ToDbCommand(this));
            succeeded = true;
            return result;
        }
        catch (Exception exception)
        {
            DataLinqTelemetry.RecordException(activity, exception);
            throw;
        }
        finally
        {
            var duration = Stopwatch.GetElapsedTime(startedAt);
            DataLinqTelemetry.RecordQueryExecution(
                telemetryContext,
                query.Table.DbName,
                "scalar",
                query.DataSource is Mutation.Transaction,
                succeeded,
                duration);

            if (activity is not null)
            {
                activity.SetTag("datalinq.outcome", succeeded ? "success" : "failure");
                activity.Dispose();
            }
        }
    }

    public override string ToString()
    {
        return ToSql().ToString();
    }
}
