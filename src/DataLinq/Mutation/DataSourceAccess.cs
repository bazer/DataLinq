using System.Collections.Generic;
using System.Data;
using DataLinq.Interfaces;

namespace DataLinq.Mutation;

public abstract class DataSourceAccess : IDataSourceAccess
{

    /// <summary>
    /// Gets the database provider.
    /// </summary>
    public IDatabaseProvider Provider { get; }

    /// <summary>
    /// Gets or sets the database transaction.
    /// </summary>
    public abstract DatabaseAccess DatabaseAccess { get; }

    protected DataSourceAccess(IDatabaseProvider provider)
    {
        Provider = provider;
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
