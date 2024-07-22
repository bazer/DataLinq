using System.Collections.Generic;
using System.Data;
using System.Linq;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Mutation;

public class ReadOnlyAccess : DataSourceAccess
{
    public override DatabaseAccess DatabaseAccess => Provider.DatabaseAccess;

    public ReadOnlyAccess(IDatabaseProvider provider) : base(provider)
    {
    }

    //private T GetModelFromCache<T>(T model) where T : IModel
    //{
    //    var metadata = model.Metadata();
    //    var keys = model.PrimaryKeys(metadata);

    //    return (T)Provider.GetTableCache(metadata.Table).GetRow(keys, this);
    //}

    /// <summary>
    /// Gets models from a query.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <returns>The models returned by the query.</returns>
    public override IEnumerable<T> GetFromQuery<T>(string query)
    {
        var table = Provider.Metadata.TableModels.Single(x => x.Model.CsType == typeof(T)).Table;

        return Provider
            .DatabaseAccess
            .ReadReader(query)
            .Select(x => new RowData(x, table, table.Columns))
            .Select(x => InstanceFactory.NewImmutableRow(x, Provider, null))
            .Cast<T>();
    }

    /// <summary>
    /// Gets models from a command.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="dbCommand">The command to execute.</param>
    /// <returns>The models returned by the command.</returns>
    public override IEnumerable<T> GetFromCommand<T>(IDbCommand dbCommand)
    {
        var table = Provider.Metadata.TableModels.Single(x => x.Model.CsType == typeof(T)).Table;

        return Provider
            .DatabaseAccess
            .ReadReader(dbCommand)
            .Select(x => new RowData(x, table, table.Columns))
            .Select(x => InstanceFactory.NewImmutableRow(x, Provider, null))
            .Cast<T>();
    }
}


/// <summary>
/// Represents a database transaction.
/// </summary>
/// <typeparam name="T">The type of the database model.</typeparam>
public class ReadOnlyAccess<T> : ReadOnlyAccess where T : class, IDatabaseModel
{
    /// <summary>
    /// Gets the database for the transaction.
    /// </summary>
    protected T Database { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Transaction{T}"/> class.
    /// </summary>
    /// <param name="databaseProvider">The database provider.</param>
    /// <param name="type">The type of the transaction.</param>
    public ReadOnlyAccess(DatabaseProvider<T> databaseProvider) : base(databaseProvider)
    {
        Database = InstanceFactory.NewDatabase<T>(this);
    }

    /// <summary>
    /// Gets the schema.
    /// </summary>
    /// <returns>The schema.</returns>
    public T Query() => Database;

    /// <summary>
    /// Creates a new SQL query from the specified table name.
    /// </summary>
    /// <param name="tableName">The name of the table.</param>
    /// <returns>The SQL query.</returns>
    public SqlQuery From(string tableName)
    {
        var table = Provider.Metadata.TableModels.Single(x => x.Table.DbName == tableName).Table;

        return new SqlQuery(table, this);
    }

    /// <summary>
    /// Creates a new SQL query from the specified table metadata.
    /// </summary>
    /// <param name="table">The table metadata.</param>
    /// <returns>The SQL query.</returns>
    public SqlQuery From(TableMetadata table)
    {
        return new SqlQuery(table, this);
    }

    /// <summary>
    /// Creates a new SQL query from the specified model type.
    /// </summary>
    /// <typeparam name="V">The type of the model.</typeparam>
    /// <returns>The SQL query.</returns>
    public SqlQuery<V> From<V>() where V : IModel
    {
        return new SqlQuery<V>(this);
    }
}