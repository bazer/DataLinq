using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

/// <summary>Shared internal execution behind the later public/generated lookup declarations.</summary>
internal static class AsyncModelLookup
{
    internal static async Task<T?> GetByProviderKeyAsyncCore<T>(DataLinqKey key, IDataSourceAccess source, CancellationToken token = default)
        where T : IModel
    {
        var table = GetTable<T>(source);
        return (T?)await source.Provider.GetTableCache(table).GetProviderRowAsyncCore(key, source, token).ConfigureAwait(false);
    }

    internal static async Task<T?> GetByModelKeyAsyncCore<T>(IReadOnlyList<object?> modelValues, IDataSourceAccess source, CancellationToken token = default)
        where T : IModel
    {
        ArgumentNullException.ThrowIfNull(modelValues);
        var table = GetTable<T>(source);
        // Conversion is part of the managed read: user converters cannot re-enter
        // the transaction, and their supported mutable result is owned before await.
        using var read = DataSourceAccess.BeginRead(source, "normalize an asynchronous lookup key");
        try
        {
            var key = KeyFactory.CreateKeyFromModelValues(modelValues, table.PrimaryKeyColumns);
            return (T?)await source.Provider.GetTableCache(table)
                .GetProviderRowAsyncCore(key, source, token, read?.Step).ConfigureAwait(false);
        }
        catch (Exception failure) { read?.ReportFailure(failure); throw; }
    }

    private static TableDefinition GetTable<T>(IDataSourceAccess source) where T : IModel
    {
        ArgumentNullException.ThrowIfNull(source);
        DataSourceAccess.EnsureReadAllowed(source, "look up an asynchronous model");
        var table = source.Provider.Metadata.GetTableModel(typeof(T)).Table;
        SourceRowLoadingValidation.ValidatePrimaryKeyTable(table);
        return table;
    }
}
