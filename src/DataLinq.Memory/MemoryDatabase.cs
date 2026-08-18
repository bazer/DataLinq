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
/// Hosts one isolated, read-only generated database over an explicitly seeded memory store.
/// </summary>
/// <typeparam name="TDatabase">The generated database-model type.</typeparam>
public sealed class MemoryDatabase<TDatabase>
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    private const string LookupSourceName = "memory.lookup";
    private readonly MemoryProviderStore store;
    private readonly MemoryReadSource readSource;

    /// <summary>
    /// Creates an empty memory database. Each instance owns an independent store and
    /// materialization cache.
    /// </summary>
    /// <remarks>
    /// Construct the database before creating generated mutable seed rows. Construction binds the
    /// generated metadata used by their runtime-owned accessors.
    /// </remarks>
    public MemoryDatabase()
    {
        var metadata = ResolveMetadata();

        store = new MemoryProviderStore(metadata);
        readSource = new MemoryReadSource(metadata, store);
        Model = InstanceFactory.NewReadDatabase<TDatabase>(readSource);
    }

    internal TDatabase Model { get; }

    /// <summary>
    /// Gets the generated read-only query model for this memory database.
    /// </summary>
    public TDatabase Query() => Model;

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

    /// <summary>
    /// Seeds one table exactly once from generated mutable model values.
    /// The rows are snapshotted during this call, normalized through the shared scalar-conversion
    /// boundary, and published atomically as read-only table state.
    /// </summary>
    /// <typeparam name="TModel">The generated immutable table-model type.</typeparam>
    /// <param name="rows">Generated mutable rows for the table.</param>
    /// <returns>This database, so multiple table seeds can be chained.</returns>
    /// <remarks>
    /// Mutating a source row concurrently with this call is unsupported. Mutations made after the
    /// call returns cannot affect the published memory state. Construct this database before the
    /// generated mutable rows so their metadata accessors are initialized.
    /// </remarks>
    public MemoryDatabase<TDatabase> Seed<TModel>(
        IEnumerable<Mutable<TModel>> rows)
        where TModel : class, IImmutableInstance, ITableModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(rows);
        var table = GetTable<TModel>();
        store.SeedModels(table, rows);
        return this;
    }

    /// <summary>
    /// Finds one generated immutable model by its public model-side primary-key value.
    /// </summary>
    /// <typeparam name="TModel">The generated immutable table-model type.</typeparam>
    /// <param name="modelPrimaryKey">
    /// The non-null model-side value for the table's single primary-key column.
    /// </param>
    /// <returns>The materialized model, or <see langword="null"/> when the key is absent.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="modelPrimaryKey"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="MemoryLookupException">
    /// The table does not have exactly one primary-key column, the model-side key cannot be
    /// normalized, or scalar conversion fails while materializing a matching canonical row.
    /// </exception>
    /// <remarks>
    /// The experimental preview supports exactly one primary-key column. Converter-backed keys are
    /// normalized once through the shared model-to-canonical conversion boundary before the memory
    /// index is probed. Composite primary keys are not supported by this public preview method.
    /// </remarks>
    public TModel? Find<TModel>(object modelPrimaryKey)
        where TModel : class, IImmutableInstance, ITableModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(modelPrimaryKey);
        var table = GetTable<TModel>();
        if (table.PrimaryKeyColumns.Count != 1)
        {
            throw new MemoryLookupException(
                $"Memory primary-key lookup for table '{table.DbName}' requires exactly one " +
                $"primary-key column, but generated metadata declares {table.PrimaryKeyColumns.Count}.");
        }

        var column = table.PrimaryKeyColumns[0];
        object? canonicalProviderValue;
        try
        {
            canonicalProviderValue = ModelValueConverter.ToCanonicalProviderValue(
                column,
                modelPrimaryKey,
                LookupSourceName);
        }
        catch (ModelValueConversionException)
        {
            var expectedType = column.ModelClrType?.FullName ?? column.ModelCsType.Name;
            throw new MemoryLookupException(
                $"Memory primary-key lookup could not normalize the supplied model value for " +
                $"column '{table.DbName}.{column.DbName}'. Expected model CLR type " +
                $"'{expectedType}'. The supplied value is not included in this diagnostic.");
        }

        if (canonicalProviderValue is null)
        {
            throw new MemoryLookupException(
                $"Memory primary-key lookup normalized the supplied model value to null for " +
                $"column '{table.DbName}.{column.DbName}', which is not a valid primary key.");
        }

        try
        {
            return FindCanonical<TModel>(DataLinqKey.FromValue(canonicalProviderValue));
        }
        catch (ModelValueConversionException exception)
        {
            var convertedColumn = exception.Column;
            var expectedType =
                convertedColumn.ModelClrType?.FullName ?? convertedColumn.ModelCsType.Name;
            throw new MemoryLookupException(
                $"Memory primary-key lookup found a row for key column " +
                $"'{table.DbName}.{column.DbName}', but could not capture canonical identity from " +
                $"model column '{convertedColumn.Table.DbName}.{convertedColumn.DbName}'. Expected " +
                $"model CLR type '{expectedType}'. Supplied key and row values are not included in " +
                $"this diagnostic.");
        }
        catch (ProviderValueMaterializationException exception)
        {
            var materializedColumn = exception.Column;
            var expectedType =
                materializedColumn.ModelClrType?.FullName ?? materializedColumn.ModelCsType.Name;
            throw new MemoryLookupException(
                $"Memory primary-key lookup found a row for key column " +
                $"'{table.DbName}.{column.DbName}', but could not materialize column " +
                $"'{materializedColumn.Table.DbName}.{materializedColumn.DbName}'. Expected model " +
                $"CLR type '{expectedType}'. Supplied key and row values are not included in this " +
                $"diagnostic.");
        }
    }

    internal TModel? FindCanonical<TModel>(
        DataLinqKey canonicalProviderKey,
        CancellationToken cancellationToken = default)
        where TModel : class, IImmutableInstance, ITableModel<TDatabase>
    {
        var table = GetTable<TModel>();
        var row = readSource.LoadSingle(
            table,
            in canonicalProviderKey,
            cancellationToken);

        return row is null
            ? null
            : (TModel)readSource.Materialize(row);
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

    private static DatabaseDefinition ResolveMetadata() =>
        DatabaseDefinition.ResolveLoadedDatabase(
            typeof(TDatabase),
            CreateMetadata,
            BindMetadata);

    private static DatabaseDefinition CreateMetadata() =>
        MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel<TDatabase>()
            .ValueOrException();

    private static void BindMetadata(DatabaseDefinition metadata) =>
        TDatabase.SetDataLinqGeneratedMetadata(metadata);
}

/// <summary>
/// Reports a primary-key lookup request that the experimental memory preview cannot perform.
/// </summary>
public sealed class MemoryLookupException : InvalidOperationException
{
    internal MemoryLookupException(string message)
        : base(message)
    {
    }
}
