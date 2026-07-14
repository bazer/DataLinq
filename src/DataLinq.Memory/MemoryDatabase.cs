using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Memory;

/// <summary>
/// Internal vertical-spike host for one generated database over the memory read source.
/// The public preview API remains deliberately unfrozen while the backend contract is proven.
/// </summary>
internal sealed class MemoryDatabase<TDatabase>
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    private readonly MemoryProviderStore store;
    private readonly MemoryReadSource readSource;

    internal MemoryDatabase()
    {
        var metadata = ResolveMetadata();

        store = new MemoryProviderStore(metadata);
        readSource = new MemoryReadSource(metadata, store);
        Model = InstanceFactory.NewReadDatabase<TDatabase>(readSource);
    }

    internal TDatabase Model { get; }

    internal DatabaseDefinition Metadata => readSource.Metadata;

    internal IDataLinqReadSource ReadSource => readSource;

    internal MemoryDiagnostics Diagnostics => readSource.GetDiagnostics();

    internal IReadOnlyList<string> SupportedCapabilityTokens =>
        MemoryQueryPlanBackend.SupportedCapabilityTokens;

    /// <summary>
    /// Seeds one table exactly once from dense table-ordinal canonical provider values.
    /// This is intentionally an internal spike API, not the eventual model-valued seed surface.
    /// </summary>
    internal MemoryDatabase<TDatabase> SeedCanonical<TModel>(
        params object?[][] canonicalProviderRows)
        where TModel : class, ITableModel<TDatabase>
    {
        var table = GetTable<TModel>();
        store.SeedCanonical(table, canonicalProviderRows);
        return this;
    }

    /// <summary>
    /// Seeds one table exactly once from dense table-ordinal model values.
    /// Every cell is normalized through the shared model-to-canonical conversion boundary before
    /// the table state is published. This remains an internal spike API rather than the eventual
    /// generated-accessor seed surface.
    /// </summary>
    internal MemoryDatabase<TDatabase> SeedModelValues<TModel>(
        params object?[][] modelRows)
        where TModel : class, ITableModel<TDatabase>
    {
        var table = GetTable<TModel>();
        store.SeedModelValues(table, modelRows);
        return this;
    }

    internal TModel? Find<TModel>(
        DataLinqKey canonicalProviderKey,
        CancellationToken cancellationToken = default)
        where TModel : class, ITableModel<TDatabase>
    {
        var table = GetTable<TModel>();
        var request = new SourcePrimaryKeyRowRequest(
            table,
            [canonicalProviderKey],
            cancellationToken);
        var result = readSource.Load(request);

        return result.Rows.Length switch
        {
            0 => null,
            1 => (TModel)readSource.Materialize(result.Rows[0]),
            _ => throw new InvalidOperationException(
                $"Memory primary-key lookup returned more than one row for table '{table.DbName}'.")
        };
    }

    /// <summary>
    /// Executes the same root entity request as the generated query surface while allowing focused
    /// cancellation tests until the public expression-query API carries a cancellation token.
    /// </summary>
    internal IEnumerable<TModel> Scan<TModel>(CancellationToken cancellationToken = default)
        where TModel : class, ITableModel<TDatabase>
    {
        var query = new DbRead<TModel>(readSource);
        return Execute(query, cancellationToken);
    }

    /// <summary>
    /// Executes a focused generated query with an explicit token until the public query API carries
    /// cancellation. This remains an internal spike surface and does not bypass plan validation.
    /// </summary>
    internal IEnumerable<TResult> Execute<TResult>(
        IQueryable<TResult> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Provider is not ExpressionQueryPlanProvider provider)
        {
            throw new InvalidOperationException(
                "The memory query did not use the DataLinq expression-plan provider.");
        }

        var invocation = provider.Parse(query.Expression, typeof(TResult));
        var request = ValidatedQueryExecutionRequest.Prepare(
            new QueryExecutionRequest(
                invocation,
                new QueryExecutionContext(readSource, cancellationToken)));

        return ExpressionQueryPlanExecutor.ExecuteEnumerable<TResult>(request);
    }

    /// <summary>
    /// Executes a focused scalar query with an explicit token until the public query API carries
    /// cancellation. This remains an internal spike surface and uses the ordinary parser and gate.
    /// </summary>
    internal TResult Execute<TResult>(
        Expression<Func<TResult>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var invocation = ExpressionQueryPlanParser.Convert(Metadata, query.Body, typeof(TResult));
        var request = ValidatedQueryExecutionRequest.Prepare(
            new QueryExecutionRequest(
                invocation,
                new QueryExecutionContext(readSource, cancellationToken)));

        return ExpressionQueryPlanExecutor.Execute<TResult>(request);
    }

    internal int GetStoredRowCount<TModel>()
        where TModel : class, ITableModel<TDatabase> =>
        store.GetRowCount(GetTable<TModel>());

    /// <summary>
    /// Test-only canonical-row inspection hook for representation-boundary assertions.
    /// The returned rows are read-only objects owned by the store.
    /// </summary>
    internal IReadOnlyList<CanonicalProviderValueRow> GetCanonicalRowsForTest<TModel>()
        where TModel : class, ITableModel<TDatabase> =>
        store.GetRows(GetTable<TModel>());

    internal int GetMaterializedRowCount<TModel>()
        where TModel : class, ITableModel<TDatabase> =>
        readSource.GetMaterializedRowCount(GetTable<TModel>());

    /// <summary>
    /// Test-only cache eviction hook. Callers must not race it with active materialization.
    /// </summary>
    internal void ClearMaterializedRowsForTest<TModel>()
        where TModel : class, ITableModel<TDatabase> =>
        readSource.ClearMaterializedRowsForTest(GetTable<TModel>());

    /// <summary>
    /// Test-only cold-start hook. Tests must isolate global metadata registry mutation.
    /// </summary>
    internal static void ResetGeneratedMetadataForTest() =>
        DatabaseDefinition.TryRemoveLoadedDatabase(typeof(TDatabase), out _);

    private TableDefinition GetTable<TModel>()
        where TModel : class, ITableModel<TDatabase> =>
        Metadata.GetTableModel(typeof(TModel)).Table;

    private static DatabaseDefinition ResolveMetadata()
        => DatabaseDefinition.ResolveLoadedDatabase(
            typeof(TDatabase),
            () => MetadataFromTypeFactory
                .ParseDatabaseFromDatabaseModel<TDatabase>()
                .ValueOrException(),
            TDatabase.SetDataLinqGeneratedMetadata);
}
