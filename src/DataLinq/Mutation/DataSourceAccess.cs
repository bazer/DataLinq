using System.Collections.Generic;
using System.Data;
using System.Threading;
using DataLinq.Cache;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Sql;

namespace DataLinq.Mutation;

public abstract class DataSourceAccess : IDataSourceAccess, IDataLinqSourceRowServices, IDataLinqQueryPlanServices
{
    private IModelMaterializationServices? materializationServices;
    private IQueryPlanBackend? queryPlanBackend;
    private ISourceRowLoader? rowLoader;

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
        string operation)
    {
        if (dataSource is Transaction transaction)
            transaction.EnsureCanRead(operation);
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
    {
        get
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
