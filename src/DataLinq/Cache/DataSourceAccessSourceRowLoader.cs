using System;
using System.Collections.Generic;
using System.Threading;
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
internal sealed class DataSourceAccessSourceRowLoader : ISourceRowLoader, ISourceIndexRowLoader
{
    private readonly IDataSourceAccess dataSource;
    private readonly string sourceName;

    internal DataSourceAccessSourceRowLoader(IDataSourceAccess dataSource)
    {
        this.dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
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
        cancellationToken.ThrowIfCancellationRequested();

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

    public SourceRowLoadResult Load(SourcePrimaryKeyRowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCanLoad(request.Table, "load source rows");

        request.ThrowIfCancellationRequested();
        var select = CreateSelect(request);
        var rows = ReadCanonicalRows(
            select,
            request.Table,
            request.CancellationToken,
            request.CanonicalProviderKeys.Length);
        request.ThrowIfCancellationRequested();
        return new SourceRowLoadResult(request, rows);
    }

    public SourceIndexRowLoadResult Load(SourceIndexRowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCanLoad(request.Table, "load indexed source rows");

        request.ThrowIfCancellationRequested();
        var select = CreateSelect(request);
        var rows = ReadCanonicalRows(
            select,
            request.Table,
            request.CancellationToken);
        request.ThrowIfCancellationRequested();
        return new SourceIndexRowLoadResult(request, rows);
    }

    private void EnsureCanLoad(TableDefinition table, string operation)
    {
        DataSourceAccess.EnsureReadAllowed(dataSource, operation);

        if (!ReferenceEquals(table.Database, dataSource.Metadata))
        {
            throw new InvalidOperationException(
                $"Read source metadata does not own table '{table.DbName}'.");
        }
    }

    private List<CanonicalProviderValueRow> ReadCanonicalRows(
        Select<object> select,
        TableDefinition table,
        CancellationToken cancellationToken,
        int capacity = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var command = select.ToDbCommand();
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = dataSource.DatabaseAccess.ExecuteReader(command);
        var rows = new List<CanonicalProviderValueRow>(capacity);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reader.ReadNextRow())
                break;

            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(ProviderRowDecoder.DecodeFullRow(reader, table, sourceName));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return rows;
    }

    private CanonicalProviderValueRow? ReadSingleCanonicalRow(
        IQuery query,
        TableDefinition table,
        in DataLinqKey requestedKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var command = dataSource.Provider.ToDbCommand(query);
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = dataSource.DatabaseAccess.ExecuteReader(command);

        cancellationToken.ThrowIfCancellationRequested();
        if (!reader.ReadNextRow())
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var row = ProviderRowDecoder.DecodeFullRow(reader, table, sourceName);

        cancellationToken.ThrowIfCancellationRequested();
        var hasSecondRow = reader.ReadNextRow();
        cancellationToken.ThrowIfCancellationRequested();
        if (hasSecondRow)
        {
            throw new InvalidOperationException(
                $"Singular source-row query for table '{table.DbName}' returned more than one row.");
        }

        SourceRowLoadingValidation.ValidateSingleResult(
            table,
            in requestedKey,
            row,
            "Singular source-row query");

        cancellationToken.ThrowIfCancellationRequested();
        return row;
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
