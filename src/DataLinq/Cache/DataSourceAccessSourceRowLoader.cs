using System;
using System.Threading;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Cache;

/// <summary>
/// SQL-era source adapter for the neutral primary-key and index row-loader contracts. It owns command
/// and reader lifetimes completely and returns only buffered canonical provider rows.
/// </summary>
internal sealed partial class DataSourceAccessSourceRowLoader : ISourceRowLoader, ISourceIndexRowLoader
{
    private readonly IDataSourceAccess dataSource;
    private readonly string sourceName;
    private readonly TransactionOperationGate.Step? owner;
    private readonly IAsyncSqlReaderFactory? asyncFactory;

    internal DataSourceAccessSourceRowLoader(
        IDataSourceAccess dataSource, TransactionOperationGate.Step? owner = null,
        IAsyncSqlReaderFactory? asyncFactory = null)
    {
        this.dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        this.owner = owner;
        this.asyncFactory = asyncFactory;
        sourceName = $"sql:{dataSource.Provider.DatabaseType}";
        ProviderRowMaterializer.ValidateSourceName(sourceName);
    }

    public CanonicalProviderValueRow? LoadSingle(
        TableDefinition table,
        in DataLinqKey canonicalProviderKey,
        CancellationToken cancellationToken = default)
    {
        SourceRowLoadingValidation.ValidatePrimaryKeyTable(table);
        SourceRowLoadingValidation.ValidateCanonicalKey(
            table,
            canonicalProviderKey,
            keyIndex: 0,
            nameof(canonicalProviderKey));
        EnsureCanLoad(table, "load one source row");
        using var read = DataSourceAccess.BeginRead(
            dataSource, "load one source row", owner, cancellationToken);
        try
        {
            var query = CreateSingleQuery(
                table,
                in canonicalProviderKey,
                cancellationToken);
            var row = ReadSingleCanonicalRow(
                query,
                table,
                in canonicalProviderKey,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return row;
        }
        catch (Exception failure)
        {
            read?.ReportFailure(failure);
            throw;
        }
    }

    public SourceRowLoadResult Load(SourcePrimaryKeyRowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCanLoad(request.Table, "load source rows");
        using var read = DataSourceAccess.BeginRead(
            dataSource, "load source rows", owner, request.CancellationToken);
        try
        {
            request.ThrowIfCancellationRequested();
            var select = CreateSelect(request);
            var result = ReadCanonicalRows(
                select,
                request);
            request.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception failure)
        {
            read?.ReportFailure(failure);
            throw;
        }
    }

    public SourceIndexRowLoadResult Load(SourceIndexRowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCanLoad(request.Table, "load indexed source rows");
        using var read = DataSourceAccess.BeginRead(
            dataSource, "load indexed source rows", owner, request.CancellationToken);
        try
        {
            request.ThrowIfCancellationRequested();
            var select = CreateSelect(request);
            var result = ReadCanonicalRows(
                select,
                request);
            request.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception failure)
        {
            read?.ReportFailure(failure);
            throw;
        }
    }

    private void EnsureCanLoad(TableDefinition table, string operation)
    {
        DataSourceAccess.EnsureReadAllowed(dataSource, operation, owner);

        if (!ReferenceEquals(table.Database, dataSource.Metadata))
        {
            throw new InvalidOperationException(
                $"Read source metadata does not own table '{table.DbName}'.");
        }
    }

    private SourceRowLoadResult ReadCanonicalRows(
        Select<object> select,
        SourcePrimaryKeyRowRequest request)
    {
        using var resources = new ReadCommandResources((dataSource as Transaction)?.TransactionID);
        var stage = ExecutionFailureStage.Validation;
        try
        {
            var cancellationToken = request.CancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            var command = resources.OwnCommand(select.ToDbCommand());
            cancellationToken.ThrowIfCancellationRequested();
            stage = ExecutionFailureStage.CommandExecution;
            var reader = resources.OwnReader(dataSource.DatabaseAccess.ExecuteReader(command));
            stage = ExecutionFailureStage.RowLoading;
            var builder = new SourceRowLoadResult.Builder(
                request,
                request.CanonicalProviderKeys.Length);

            while (true)
            {
                stage = ExecutionFailureStage.RowLoading;
                cancellationToken.ThrowIfCancellationRequested();
                if (!reader.ReadNextRow())
                    break;

                cancellationToken.ThrowIfCancellationRequested();
                stage = ExecutionFailureStage.Materialization;
                builder.Add(ProviderRowDecoder.DecodeFullRow(
                    reader,
                    request.Table,
                    sourceName));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return builder.Build();
        }
        catch (Exception failure)
        {
            resources.RecordFailure(failure, stage);
            throw;
        }
    }

    private SourceIndexRowLoadResult ReadCanonicalRows(
        Select<object> select,
        SourceIndexRowRequest request)
    {
        using var resources = new ReadCommandResources((dataSource as Transaction)?.TransactionID);
        var stage = ExecutionFailureStage.Validation;
        try
        {
            var cancellationToken = request.CancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            var command = resources.OwnCommand(select.ToDbCommand());
            cancellationToken.ThrowIfCancellationRequested();
            stage = ExecutionFailureStage.CommandExecution;
            var reader = resources.OwnReader(dataSource.DatabaseAccess.ExecuteReader(command));
            stage = ExecutionFailureStage.RowLoading;
            var builder = new SourceIndexRowLoadResult.Builder(request);

            while (true)
            {
                stage = ExecutionFailureStage.RowLoading;
                cancellationToken.ThrowIfCancellationRequested();
                if (!reader.ReadNextRow())
                    break;

                cancellationToken.ThrowIfCancellationRequested();
                stage = ExecutionFailureStage.Materialization;
                builder.Add(ProviderRowDecoder.DecodeFullRow(
                    reader,
                    request.Table,
                    sourceName));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return builder.Build();
        }
        catch (Exception failure)
        {
            resources.RecordFailure(failure, stage);
            throw;
        }
    }

    private CanonicalProviderValueRow? ReadSingleCanonicalRow(
        IQuery query,
        TableDefinition table,
        in DataLinqKey requestedKey,
        CancellationToken cancellationToken)
    {
        using var resources = new ReadCommandResources((dataSource as Transaction)?.TransactionID);
        var stage = ExecutionFailureStage.Validation;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = resources.OwnCommand(dataSource.Provider.ToDbCommand(query));
            cancellationToken.ThrowIfCancellationRequested();
            stage = ExecutionFailureStage.CommandExecution;
            var reader = resources.OwnReader(dataSource.DatabaseAccess.ExecuteReader(command));

            stage = ExecutionFailureStage.RowLoading;
            cancellationToken.ThrowIfCancellationRequested();
            if (!reader.ReadNextRow())
            {
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            stage = ExecutionFailureStage.Materialization;
            var row = ProviderRowDecoder.DecodeFullRow(reader, table, sourceName);

            stage = ExecutionFailureStage.RowLoading;
            cancellationToken.ThrowIfCancellationRequested();
            var hasSecondRow = reader.ReadNextRow();
            cancellationToken.ThrowIfCancellationRequested();
            if (hasSecondRow)
            {
                throw new InvalidOperationException(
                    $"Singular source-row query for table '{table.DbName}' returned more than one row.");
            }

            stage = ExecutionFailureStage.Materialization;
            SourceRowLoadingValidation.ValidateSingleResult(
                table,
                in requestedKey,
                row,
                "Singular source-row query");

            cancellationToken.ThrowIfCancellationRequested();
            return row;
        }
        catch (Exception failure)
        {
            resources.RecordFailure(failure, stage);
            throw;
        }
    }

    private IQuery CreateSingleQuery(
        TableDefinition table,
        in DataLinqKey canonicalProviderKey,
        CancellationToken cancellationToken)
    {
        var writer = dataSource.Provider.GetWriter();
        if (table.PrimaryKeyColumns.Count == 1)
        {
            var column = table.PrimaryKeyColumns[0];
            return new ScalarColumnRowsQuery(
                table,
                dataSource,
                column,
                writer.ConvertColumnValue(
                    column,
                    canonicalProviderKey.GetValue(0)));
        }

        var query = new SqlQuery(table, dataSource);
        for (var componentIndex = 0; componentIndex < table.PrimaryKeyColumns.Count; componentIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = table.PrimaryKeyColumns[componentIndex];
            query.Where(column.DbName).EqualTo(
                writer.ConvertColumnValue(
                    column,
                    canonicalProviderKey.GetValue(componentIndex)));
        }

        return query.SelectQuery();
    }

    private Select<object> CreateSelect(SourcePrimaryKeyRowRequest request)
    {
        var table = request.Table;
        var query = new SqlQuery(table, dataSource);
        var writer = dataSource.Provider.GetWriter();

        if (table.PrimaryKeyColumns.Count == 1)
        {
            var column = table.PrimaryKeyColumns[0];

            if (request.CanonicalProviderKeys.Length == 1)
            {
                query.Where(column.DbName).EqualTo(
                    writer.ConvertColumnValue(
                        column,
                        request.CanonicalProviderKeys[0].GetValue(0)));
                return query.SelectQuery();
            }

            var values = new object?[request.CanonicalProviderKeys.Length];
            for (var index = 0; index < values.Length; index++)
            {
                request.ThrowIfCancellationRequested();
                values[index] = writer.ConvertColumnValue(
                    column,
                    request.CanonicalProviderKeys[index].GetValue(0));
            }

            query.Where(column.DbName).In(values);
            return query.SelectQuery();
        }

        for (var keyIndex = 0; keyIndex < request.CanonicalProviderKeys.Length; keyIndex++)
        {
            request.ThrowIfCancellationRequested();
            var key = request.CanonicalProviderKeys[keyIndex];
            var keyGroup = query.AddWhereGroup(
                keyIndex == 0 ? BooleanType.And : BooleanType.Or);

            for (var componentIndex = 0; componentIndex < table.PrimaryKeyColumns.Count; componentIndex++)
            {
                var column = table.PrimaryKeyColumns[componentIndex];
                keyGroup.Where(column.DbName).EqualTo(
                    writer.ConvertColumnValue(
                        column,
                        key.GetValue(componentIndex)));
            }
        }

        return query.SelectQuery();
    }

    private Select<object> CreateSelect(SourceIndexRowRequest request)
    {
        var query = new SqlQuery(request.Table, dataSource);
        var writer = dataSource.Provider.GetWriter();
        var key = request.CanonicalProviderIndexKey;

        for (var componentIndex = 0; componentIndex < request.Index.Columns.Count; componentIndex++)
        {
            request.ThrowIfCancellationRequested();
            var column = request.Index.Columns[componentIndex];
            query.Where(column.DbName).EqualTo(
                writer.ConvertColumnValue(
                    column,
                    key.GetValue(componentIndex)));
        }

        return query.SelectQuery();
    }
}
