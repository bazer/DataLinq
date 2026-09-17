using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DataLinq.Diagnostics;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Mutation;

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
        SqlIdentifier.Append(sql, query.Alias ?? throw new InvalidOperationException("A derived query source requires an alias."), query.EscapeCharacter);
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
                if (query.Table.TryGetColumnByDbName(SqlIdentifier.Unquote(whatList[i], query.EscapeCharacter), out _))
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
            SqlIdentifier.Append(sql, columns[i].DbName, query.EscapeCharacter);
        }
    }

    private static void AddColumnSeparator(Sql sql, int index)
    {
        if (index > 0)
            sql.AddText(", ");
    }

    private void AddColumnPrefix(Sql sql, string? alias)
    {
        if (alias is null)
            return;

        SqlIdentifier.Append(sql, alias, query.EscapeCharacter);
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
        => ReadReader(CancellationToken.None);

    internal IEnumerable<IDataLinqDataReader> ReadReader(
        CancellationToken cancellationToken, TransactionOperationGate.Step? owner = null) =>
        DataSourceAccess.ReadSequence(query.DataSource, "read query rows",
            step => ReadReaderCore(cancellationToken, step), owner, cancellationToken);

    private IEnumerable<IDataLinqDataReader> ReadReaderCore(
        CancellationToken cancellationToken, TransactionOperationGate.Step? owner)
    {
        using var read = DataSourceAccess.BeginRead(
            query.DataSource, "read query rows", owner, cancellationToken);
        using var resources = new ReadCommandResources((query.DataSource as Transaction)?.TransactionID);
        IDataLinqDataReader reader;
        var stage = ExecutionFailureStage.Validation;
        try
        {
            var command = resources.OwnCommand(ToDbCommand());
            cancellationToken.ThrowIfCancellationRequested();
            stage = ExecutionFailureStage.CommandExecution;
            reader = resources.OwnReader(query.DataSource.DatabaseAccess.ExecuteReader(command));
        }
        catch (Exception failure)
        {
            resources.RecordFailure(failure, stage);
            throw;
        }

        while (true)
        {
            bool hasRow;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                hasRow = reader.ReadNextRow();
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception failure)
            {
                resources.RecordFailure(failure, ExecutionFailureStage.RowLoading);
                throw;
            }
            if (!hasRow)
                yield break;

            yield return reader;
        }
    }

    public IEnumerable<RowData> ReadRows() => ReadRows(owner: null);

    internal IEnumerable<RowData> ReadRows(TransactionOperationGate.Step? owner) =>
        DataSourceAccess.ReadSequence(query.DataSource, "read query rows", ReadRowsCore, owner);

    private IEnumerable<RowData> ReadRowsCore(TransactionOperationGate.Step? owner)
    {
        // Resolve the actual columns being fetched to ensure the RowData 
        // reader aligns with the DataReader's fields.
        var columnsToRead = GetColumnsToRead();

        foreach (var reader in ReadReader(default, owner))
            yield return new RowData(
                reader,
                query.Table,
                columnsToRead,
                true,
                $"sql:{query.DataSource.Provider.DatabaseType}:select-rows");
    }

    public RowData? ReadFirstRow()
        => ReadFirstRow(owner: null);

    internal RowData? ReadFirstRow(TransactionOperationGate.Step? owner)
    {
        using var read = DataSourceAccess.BeginRead(query.DataSource, "read the first query row", owner);
        try
        {
            using var resources = new ReadCommandResources((query.DataSource as Transaction)?.TransactionID);
            var stage = ExecutionFailureStage.Validation;
            try
            {
                // Resolve the actual columns being fetched to ensure the RowData
                // reader aligns with the DataReader's fields.
                var columnsToRead = GetColumnsToRead();

                var command = resources.OwnCommand(query.DataSource.Provider.ToDbCommand(this));
                stage = ExecutionFailureStage.CommandExecution;
                var reader = resources.OwnReader(query.DataSource.DatabaseAccess.ExecuteReader(command));
                stage = ExecutionFailureStage.RowLoading;

                if (!reader.ReadNextRow())
                    return null;
                stage = ExecutionFailureStage.Materialization;
                return new RowData(
                        reader,
                        query.Table,
                        columnsToRead,
                        true,
                        $"sql:{query.DataSource.Provider.DatabaseType}:select-first-row");
            }
            catch (Exception failure)
            {
                resources.RecordFailure(failure, stage);
                throw;
            }
        }
        catch (Exception failure)
        {
            read?.ReportFailure(failure);
            throw;
        }
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
            var cleanName = SqlIdentifier.Unquote(what, escape);

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

    public IEnumerable<DataLinqKey> ReadKeys()
    {
        return DataSourceAccess.ReadSequence(query.DataSource, "read query keys",
            owner => KeyFactory.GetKeys(this, query.Table.PrimaryKeyColumns, owner));
    }

    //public IEnumerable<DataLinqKey> ReadForeignKeys(ColumnIndex foreignKeyIndex)
    //{
    //    return ReadReader()
    //        .Select(x => new RowData(x, query.Table, foreignKeyIndex.Columns.AsSpan()))
    //        .Select(x => new ForeignKey(foreignKeyIndex, x.GetValues(foreignKeyIndex.Columns).ToArray()));
    //}

    public IEnumerable<(DataLinqKey fk, DataLinqKey[] pks)> ReadPrimaryAndForeignKeys(ColumnIndex foreignKeyIndex)
        => DataSourceAccess.ReadSequence(query.DataSource, "read query key groups",
            owner => ReadPrimaryAndForeignKeysCore(foreignKeyIndex, owner));

    private IEnumerable<(DataLinqKey fk, DataLinqKey[] pks)> ReadPrimaryAndForeignKeysCore(
        ColumnIndex foreignKeyIndex, TransactionOperationGate.Step? owner)
    {
        var columnsToRead = GetPrimaryAndForeignKeyColumns(foreignKeyIndex);
        var primaryKeysByForeignKey = new Dictionary<DataLinqKey, List<DataLinqKey>>();

        foreach (var reader in ReadReader(default, owner))
        {
            var row = new RowData(
                reader,
                query.Table,
                columnsToRead,
                false,
                $"sql:{query.DataSource.Provider.DatabaseType}:key-pairs");
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
        DataSourceAccess.ReadSequence(query.DataSource, "execute a typed entity query",
            owner => ExecuteCore(owner).Select(x => (V)x));

    public IEnumerable<IImmutableInstance> Execute() => Execute(owner: null);

    internal IEnumerable<IImmutableInstance> Execute(TransactionOperationGate.Step? owner) =>
        DataSourceAccess.ReadSequence(query.DataSource, "execute an entity query", ExecuteCore, owner);

    private IEnumerable<IImmutableInstance> ExecuteCore(TransactionOperationGate.Step? owner)
    {
        DataSourceAccess.EnsureReadAllowed(query.DataSource, "execute an entity query", owner);
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
                    tableCache.TryGetRowFromProviderKeyValue(simpleScalarKey, query.DataSource, out var scalarRow, owner))
                {
                    if (scalarRow is not null)
                        yield return scalarRow;
                }
                else if (query.TryGetSimplePrimaryKey() is DataLinqKey simpleKey)
                {
                    var row = tableCache.GetRow(simpleKey, query.DataSource, owner);
                    if (row is not null)
                        yield return row;
                }
                else if (!query.HasDerivedSource &&
                    !query.HasJoins &&
                    tableCache.TryGetRowsFromScalarPrimaryKeyQuery(this, query.DataSource, out var providerKeyRows, owner))
                {
                    foreach (var row in providerKeyRows)
                        yield return row;
                }
                else
                {
                    this.What(query.Table.PrimaryKeyColumns);
                    var keys = KeyFactory.GetKeys(this, query.Table.PrimaryKeyColumns, owner).ToArray();
                    // The database has already applied ordering, collation, and paging.
                    // Replay that key sequence instead of sorting cached models in the CLR.
                    foreach (var row in tableCache.GetRows(keys, query.DataSource, owner: owner))
                        yield return row;
                }
            }
            else
            {
                foreach (var rowData in this.ReadRows(owner))
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
        => ExecuteScalar<V>(CancellationToken.None);

    internal V ExecuteScalar<V>(CancellationToken cancellationToken, TransactionOperationGate.Step? owner = null)
    {
        using var read = DataSourceAccess.BeginRead(
            query.DataSource, "execute a scalar query", owner, cancellationToken);
        try
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
                using var resources = new ReadCommandResources((query.DataSource as Transaction)?.TransactionID);
                var stage = ExecutionFailureStage.Validation;
                try
                {
                    var command = resources.OwnCommand(query.DataSource.Provider.ToDbCommand(this));
                    stage = ExecutionFailureStage.CommandExecution;
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = query.DataSource.DatabaseAccess.ExecuteScalar<V>(command);
                    resources.Dispose();
                    succeeded = true;
                    return result;
                }
                catch (Exception failure)
                {
                    resources.RecordFailure(failure, stage);
                    throw;
                }
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
        catch (Exception failure)
        {
            read?.ReportFailure(failure);
            throw;
        }
    }

    public object? ExecuteScalar()
        => ExecuteScalar(CancellationToken.None);

    internal object? ExecuteScalar(CancellationToken cancellationToken, TransactionOperationGate.Step? owner = null)
    {
        using var read = DataSourceAccess.BeginRead(
            query.DataSource, "execute a scalar query", owner, cancellationToken);
        try
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
                using var resources = new ReadCommandResources((query.DataSource as Transaction)?.TransactionID);
                var stage = ExecutionFailureStage.Validation;
                try
                {
                    var command = resources.OwnCommand(query.DataSource.Provider.ToDbCommand(this));
                    stage = ExecutionFailureStage.CommandExecution;
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = query.DataSource.DatabaseAccess.ExecuteScalar(command);
                    resources.Dispose();
                    succeeded = true;
                    return result;
                }
                catch (Exception failure)
                {
                    resources.RecordFailure(failure, stage);
                    throw;
                }
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
        catch (Exception failure)
        {
            read?.ReportFailure(failure);
            throw;
        }
    }

    public override string ToString()
    {
        return ToSql().ToString();
    }
}
