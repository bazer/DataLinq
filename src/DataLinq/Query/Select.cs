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
        var canUseTemplate = query.TryGetValueEqualityTemplateKey(paramPrefix, out var key, out var values);
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
        query.AddTableName(sql, query.Table.DbName, query.Alias);
        query.GetJoins(sql, paramPrefix);
        query.GetWhere(sql, paramPrefix);
        query.GetOrderBy(sql);
        query.GetLimit(sql);

        return sql;
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

        return ReadReader()
            .Select(x => new RowData(x, query.Table, columnsToRead, true));
    }

    private ColumnDefinition[] GetColumnsToRead()
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
            var col = query.Table.Columns.FirstOrDefault(c => c.DbName.Equals(cleanName, StringComparison.OrdinalIgnoreCase));

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
        return definitions.ToArray();
    }

    public IEnumerable<IKey> ReadKeys()
    {
        return KeyFactory.GetKeys(this, query.Table.PrimaryKeyColumns);
    }

    //public IEnumerable<IKey> ReadForeignKeys(ColumnIndex foreignKeyIndex)
    //{
    //    return ReadReader()
    //        .Select(x => new RowData(x, query.Table, foreignKeyIndex.Columns.AsSpan()))
    //        .Select(x => new ForeignKey(foreignKeyIndex, x.GetValues(foreignKeyIndex.Columns).ToArray()));
    //}

    public IEnumerable<(IKey fk, IKey[] pks)> ReadPrimaryAndForeignKeys(ColumnIndex foreignKeyIndex)
    {
        return ReadReader()
            .Select(x => new RowData(x, query.Table, query.Table.PrimaryKeyColumns.Concat(foreignKeyIndex.Columns).Distinct().ToArray(), false))
            .Select(x => (fk: KeyFactory.CreateKeyFromValues(x.GetValues(foreignKeyIndex.Columns)), pk: KeyFactory.GetKey(x, query.Table.PrimaryKeyColumns)))
            .GroupBy(x => x.fk)
            .Select(x => (x.Key, x.Select(y => y.pk).ToArray()));
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
                this.What(query.Table.PrimaryKeyColumns);

                // OPTIMIZATION: Try to extract keys directly from the query structure (e.g. Single(x => x.Id == 1))
                // This avoids the first round-trip to fetch keys if the query is a simple PK lookup.
                var simpleKey = query.TryGetSimplePrimaryKey();

                var keys = simpleKey != null
                    ? [simpleKey]
                    : this.ReadKeys().ToArray();

                foreach (var row in query.DataSource.Provider.GetTableCache(query.Table).GetRows(keys, query.DataSource, orderings: query.OrderByList))
                    yield return row;
            }
            else
            {
                var rows = this
                    .ReadRows()
                    .Select(x => InstanceFactory.NewImmutableRow(x, query.DataSource));

                foreach (var row in rows)
                    yield return row;
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
