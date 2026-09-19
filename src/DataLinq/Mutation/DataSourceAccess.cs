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

public abstract partial class DataSourceAccess :
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
        TransactionOperationGate.Step? owner = null,
        ExecutionOperationKind operationKind = ExecutionOperationKind.Unknown)
    {
        if (dataSource is Transaction transaction)
            transaction.EnsureCanRead(operation, owner, operationKind);
        else if (owner is not null)
            throw new InvalidOperationException("An owned read requires its original transaction source.");
    }

    internal static TransactionReadScope? BeginRead(
        IDataSourceAccess dataSource,
        string operation,
        TransactionOperationGate.Step? owner = null,
        CancellationToken cancellationToken = default,
        ExecutionOperationKind operationKind = ExecutionOperationKind.Unknown)
    {
        EnsureReadAllowed(dataSource, operation, owner, operationKind);
        cancellationToken.ThrowIfCancellationRequested();
        return owner is null && dataSource is Transaction transaction
            ? new TransactionReadScope(transaction, operation, operationKind)
            : null;
    }

    internal static IEnumerable<T> ReadSequence<T>(
        IDataSourceAccess source,
        string operation,
        Func<TransactionOperationGate.Step?, IEnumerable<T>> rows,
        TransactionOperationGate.Step? owner = null,
        CancellationToken cancellationToken = default,
        ExecutionOperationKind operationKind = ExecutionOperationKind.Query)
    {
        // Private composition shares the outer owner's lifetime. All iterator calls
        // need diagnostic isolation, including database-root and private reads;
        // GuardedEnumerable ends each scope before yielding control to the consumer.
        if (owner is not null)
        {
            EnsureReadAllowed(source, operation, owner);
        }

        return new GuardedEnumerable<T>(Read);

        IEnumerable<T> Read(IHelperTrackedReader reader)
        {
            var identity = ReadExecutionIdentity.Capture(source, operationKind, owner);
            using var scope = BeginRead(source, operation, owner, cancellationToken, identity.Operation);
            scope?.RegisterReader(reader);
            var step = owner ?? scope?.Step;
            IEnumerator<T>? iterator = null;
            ExecutionFailures? failures = null;
            try
            {
                try { iterator = rows(step).GetEnumerator(); }
                catch (Exception failure) { Record(failure); throw; }
                while (true)
                {
                    bool hasRow;
                    T row = default!;
                    try
                    {
                        EnsureReadAllowed(source, operation, step);
                        cancellationToken.ThrowIfCancellationRequested();
                        hasRow = iterator.MoveNext();
                        cancellationToken.ThrowIfCancellationRequested();
                        if (hasRow) row = iterator.Current;
                    }
                    catch (Exception failure) { Record(failure); throw; }
                    if (!hasRow)
                        yield break;
                    yield return row;
                }
            }
            finally
            {
                try { iterator?.Dispose(); }
                catch (Exception failure) { Record(failure, ExecutionFailureStage.Cleanup); }
                if (failures?.Primary is { } primary)
                {
                    var transaction = source as Transaction;
                    ExecutionFailureContexts.Attach(primary, failures.Snapshot(new(),
                        transaction is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted,
                        transaction is null ? ExecutionRecoveryActions.None : ExecutionRecoveryActions.Dispose,
                        transaction?.TransactionID, identity.Operation, identity.ProviderInstanceId));
                    scope?.ReportFailure(primary);
                    failures.ThrowIfAny();
                }
            }

            void Record(Exception failure, ExecutionFailureStage stage = ExecutionFailureStage.RowLoading) =>
                (failures ??= new()).AddReported(failure, stage,
                    failure is OperationCanceledException canceled && canceled.CancellationToken == cancellationToken && cancellationToken.IsCancellationRequested
                        ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown, identity.Operation);
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
