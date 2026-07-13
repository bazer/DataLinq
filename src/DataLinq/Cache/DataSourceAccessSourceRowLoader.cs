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
