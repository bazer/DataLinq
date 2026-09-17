using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using DataLinq.Cache;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Metadata;

namespace DataLinq.Mutation;

public abstract class DataSourceAccess :
    IDataSourceAccess,
    IDataLinqSourceRowServices,
    IDataLinqIndexRowServices,
    IDataLinqQueryPlanServices,
    IExactPrimaryKeyTerminalExecutionServices
{
    private IModelMaterializationServices? materializationServices;
    private IQueryPlanBackend? queryPlanBackend;
    private DataSourceAccessSourceRowLoader? rowLoader;

    /// <summary>
    /// Gets the database provider.
    /// </summary>
    public IDatabaseProvider Provider { get; }

    /// <summary>
    /// Gets or sets the database transaction.
    /// </summary>
    public abstract IDatabaseAccess DatabaseAccess { get; }

    protected DataSourceAccess(IDatabaseProvider provider)
    {
        Provider = provider;
    }

    internal static void EnsureReadAllowed(
        IDataSourceAccess dataSource,
        string operation,
        TransactionOperationGate.Step? owner = null)
    {
        if (dataSource is Transaction transaction)
            transaction.EnsureCanRead(operation, owner);
        else if (owner is not null)
            throw new InvalidOperationException("An owned read requires its original transaction source.");
    }

    internal static TransactionReadScope? BeginRead(
        IDataSourceAccess dataSource,
        string operation,
        TransactionOperationGate.Step? owner = null,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAllowed(dataSource, operation, owner);
        cancellationToken.ThrowIfCancellationRequested();
        return owner is null && dataSource is Transaction transaction
            ? new TransactionReadScope(transaction, operation)
            : null;
    }

    internal static IEnumerable<T> ReadSequence<T>(
        IDataSourceAccess source,
        string operation,
        Func<TransactionOperationGate.Step?, IEnumerable<T>> rows,
        TransactionOperationGate.Step? owner = null,
        CancellationToken cancellationToken = default)
    {
        // Private composition shares the outer owner's lifetime. Database-root
        // reads remain independent and retain their existing execution path.
        if (owner is not null)
        {
            EnsureReadAllowed(source, operation, owner);
            return rows(owner);
        }
        if (source is not Transaction)
            return rows(owner);

        return new GuardedEnumerable<T>(Read());

        IEnumerable<T> Read()
        {
            using var scope = BeginRead(source, operation, cancellationToken: cancellationToken);
            using var iterator = rows(scope!.Step).GetEnumerator();
            while (true)
            {
                EnsureReadAllowed(source, operation, scope.Step);
                cancellationToken.ThrowIfCancellationRequested();
                var hasRow = iterator.MoveNext();
                cancellationToken.ThrowIfCancellationRequested();
                if (!hasRow)
                    yield break;
                yield return iterator.Current;
            }
        }
    }

    // This service bundle is private dispatch state, not a replacement model source.
    internal IDataLinqSourceRowServices GetOwnedRowServices(TransactionOperationGate.Step owner)
        => new OwnedRowServices(this, owner);

    internal IDataLinqIndexRowServices GetOwnedIndexRowServices(TransactionOperationGate.Step owner)
        => new OwnedRowServices(this, owner);

    private sealed class OwnedRowServices : IDataLinqSourceRowServices, IDataLinqIndexRowServices
    {
        public DatabaseDefinition Metadata { get; }
        public ISourceRowLoader RowLoader { get; }
        public ISourceIndexRowLoader IndexRowLoader => (ISourceIndexRowLoader)RowLoader;
        public IModelMaterializationServices MaterializationServices { get; }

        internal OwnedRowServices(DataSourceAccess source, TransactionOperationGate.Step owner)
        {
            Metadata = ((IDataSourceAccess)source).Metadata;
            RowLoader = new DataSourceAccessSourceRowLoader(source, owner);
            MaterializationServices = new ModelMaterializationServices(
                $"sql:{source.Provider.DatabaseType}",
                new ReadSourceModelMaterializationRuntime(
                    source, new DataSourceAccessMaterializationCache(source, owner)));
        }
    }

    IModelMaterializationServices IDataLinqReadServices.MaterializationServices
    {
        get
        {
            var services = materializationServices;
            if (services is not null)
                return services;

            var runtime = new ReadSourceModelMaterializationRuntime(
                this,
                new DataSourceAccessMaterializationCache(this));
            var created = new ModelMaterializationServices(
                $"sql:{Provider.DatabaseType}",
                runtime);

            return Interlocked.CompareExchange(
                ref materializationServices,
                created,
                comparand: null) ?? created;
        }
    }

    ISourceRowLoader IDataLinqSourceRowServices.RowLoader
        => GetOrCreateRowLoader();

    ISourceIndexRowLoader IDataLinqIndexRowServices.IndexRowLoader
        => GetOrCreateRowLoader();

    private DataSourceAccessSourceRowLoader GetOrCreateRowLoader()
    {
        var loader = rowLoader;
        if (loader is not null)
            return loader;

        var created = new DataSourceAccessSourceRowLoader(this);
        return Interlocked.CompareExchange(
            ref rowLoader,
            created,
            comparand: null) ?? created;
    }

    IQueryPlanBackend IDataLinqQueryPlanServices.QueryPlanBackend
    {
        get
        {
            var backend = queryPlanBackend;
            if (backend is not null)
                return backend;

            IQueryPlanBackend created = new SqlQueryPlanBackend(this);
            return Interlocked.CompareExchange(
                ref queryPlanBackend,
                created,
                comparand: null) ?? created;
        }
    }

    IImmutableInstance? IExactPrimaryKeyTerminalExecutionServices.ExecuteExactPrimaryKeyTerminal(
        TableDefinition table,
        object? canonicalProviderKey,
        QueryPlanResultKind resultKind) =>
        SqlQueryPlanBackend.ExecuteTerminalPrimaryKeyLookup(
            this,
            table,
            canonicalProviderKey,
            resultKind);

    /// <summary>
    /// Gets models from a query.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <returns>The models returned by the query.</returns>
    public abstract IEnumerable<T> GetFromQuery<T>(string query) where T : IModel;

    /// <summary>
    /// Gets models from a command.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="dbCommand">The command to execute.</param>
    /// <returns>The models returned by the command.</returns>
    public abstract IEnumerable<T> GetFromCommand<T>(IDbCommand dbCommand) where T : IModel;
}
