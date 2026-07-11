using System;
using System.Collections.Generic;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Query;

namespace DataLinq.Cache;

/// <summary>
/// SQL-era source adapter for the neutral primary-key row-loader contract. It owns command and reader
/// lifetimes completely and returns only buffered canonical provider rows.
/// </summary>
internal sealed class DataSourceAccessSourceRowLoader : ISourceRowLoader
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

        if (!ReferenceEquals(request.Table.Database, dataSource.Metadata))
        {
            throw new InvalidOperationException(
                $"Read source metadata does not own table '{request.Table.DbName}'.");
        }

        request.ThrowIfCancellationRequested();
        var select = CreateSelect(request);
        using var command = select.ToDbCommand();
        request.ThrowIfCancellationRequested();
        using var reader = dataSource.DatabaseAccess.ExecuteReader(command);
        var rows = new List<CanonicalProviderValueRow>(request.CanonicalProviderKeys.Length);

        while (true)
        {
            request.ThrowIfCancellationRequested();
            if (!reader.ReadNextRow())
                break;

            request.ThrowIfCancellationRequested();
            rows.Add(ProviderRowDecoder.DecodeFullRow(reader, request.Table, sourceName));
        }

        request.ThrowIfCancellationRequested();
        return new SourceRowLoadResult(request, rows);
    }

    private Select<object> CreateSelect(SourcePrimaryKeyRowRequest request)
    {
        var table = request.Table;
        var query = new SqlQuery(table, dataSource);
        var writer = dataSource.Provider.GetWriter();

        if (table.PrimaryKeyColumns.Count == 1)
        {
            var column = table.PrimaryKeyColumns[0];
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
}
