using System;
using System.Data;
using System.Linq;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq;


/// <summary>
/// The main interface for working with the database.
/// </summary>
/// <typeparam name="T">The type of the database model.</typeparam>
public abstract class Database<T> : IDisposable
    where T : class, IDatabaseModel
{
    /// <summary>
    /// Gets the type of the database.
    /// </summary>
    public DatabaseType DatabaseType => Provider.DatabaseType;

    /// <summary>
    /// Gets the database provider.
    /// </summary>
    public DatabaseProvider<T> Provider { get; }
    

    /// <summary>
    /// Initializes a new instance of the <see cref="Database{T}"/> class.
    /// </summary>
    /// <param name="provider">The database provider.</param>
    public Database(DatabaseProvider<T> provider)
    {
        this.Provider = provider;
    }

    /// <summary>
    /// Checks if the file or server exists.
    /// </summary>
    /// <returns><c>true</c> if the file or server exists; otherwise, <c>false</c>.</returns>
    public bool FileOrServerExists()
    {
        return Provider.FileOrServerExists();
    }

    /// <summary>
    /// Checks if the database exists.
    /// </summary>
    /// <param name="databaseName">The name of the database.</param>
    /// <returns><c>true</c> if the database exists; otherwise, <c>false</c>.</returns>
    public bool Exists(string? databaseName = null)
    {
        return Provider.DatabaseExists(databaseName);
    }

    /// <summary>
    /// Starts a new transaction.
    /// </summary>
    /// <param name="transactionType">The type of the transaction.</param>
    /// <returns>The new transaction.</returns>
    public Transaction<T> Transaction(TransactionType transactionType = TransactionType.ReadAndWrite)
    {
        return new Transaction<T>(this.Provider, transactionType);
    }

    /// <summary>
    /// Attaches a transaction to the database.
    /// </summary>
    /// <param name="dbTransaction">The database transaction.</param>
    /// <param name="transactionType">The type of the transaction.</param>
    /// <returns>The attached transaction.</returns>
    public Transaction<T> AttachTransaction(IDbTransaction dbTransaction, TransactionType transactionType = TransactionType.ReadAndWrite)
    {
        return new Transaction<T>(this.Provider, dbTransaction, transactionType);
    }

    /// <summary>
    /// Queries the database.
    /// </summary>
    /// <returns>The query result.</returns>
    public T Query()
    {
        return Provider.TypedReadOnlyAccess.Query();
    }

    /// <summary>
    /// Creates a new SQL query from the specified table name and alias.
    /// </summary>
    /// <param name="tableName">The name of the table.</param>
    /// <param name="alias">The alias of the table.</param>
    /// <returns>The new SQL query.</returns>
    public SqlQuery From(string tableName, string? alias = null)
    {
        if (alias == null)
            (tableName, alias) = QueryUtils.ParseTableNameAndAlias(tableName);

        var table = Provider.TypedReadOnlyAccess.Provider.Metadata.TableModels.Single(x => x.Table.DbName == tableName).Table;

        return new SqlQuery(table, Provider.TypedReadOnlyAccess, alias);
    }

    /// <summary>
    /// Creates a new SQL query from the specified table and alias.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="alias">The alias of the table.</param>
    /// <returns>The new SQL query.</returns>
    public SqlQuery From(TableDefinition table, string? alias = null)
    {
        return new SqlQuery(table, Provider.TypedReadOnlyAccess, alias);
    }

    /// <summary>
    /// Creates a new SQL query from the specified model type.
    /// </summary>
    /// <typeparam name="V">The type of the model.</typeparam>
    /// <returns>The new SQL query.</returns>
    public SqlQuery<V> From<V>() where V : IModel
    {
        return Provider.TypedReadOnlyAccess.From<V>();
    }

    /// <summary>
    /// Inserts a new model into the database.
    /// </summary>
    /// <typeparam name="M">The type of the model.</typeparam>
    /// <param name="model">The model to insert.</param>
    /// <param name="transactionType">The type of the transaction.</param>
    /// <returns>The inserted model.</returns>
    public M Insert<M>(Mutable<M> model, TransactionType transactionType = TransactionType.ReadAndWrite) where M : IImmutableInstance
    {
        return Commit(transaction => transaction.Insert(model), transactionType);
    }

    /// <summary>
    /// Updates an existing model in the database.
    /// </summary>
    /// <typeparam name="M">The type of the model.</typeparam>
    /// <param name="model">The model to update.</param>
    /// <param name="transactionType">The type of the transaction.</param>
    /// <returns>The updated model.</returns>
    public M Update<M>(Mutable<M> model, TransactionType transactionType = TransactionType.ReadAndWrite) where M : IImmutableInstance
    {
        return Commit(transaction => transaction.Update(model), transactionType);
    }

    /// <summary>
    /// Inserts or updates a model in the database.
    /// </summary>
    /// <typeparam name="M">The type of the model.</typeparam>
    /// <param name="model">The model to insert or update.</param>
    /// <param name="transactionType">The type of the transaction.</param>
    /// <returns>The inserted or updated model.</returns>
    public M InsertOrUpdate<M>(Mutable<M> model, TransactionType transactionType = TransactionType.ReadAndWrite) where M : IImmutableInstance
    {
        return Commit(transaction => transaction.InsertOrUpdate(model), transactionType);
    }

    /// <summary>
    /// Deletes a model from the database.
    /// </summary>
    /// <typeparam name="M">The type of the model.</typeparam>
    /// <param name="model">The model to delete.</param>
    /// <param name="transactionType">The type of the transaction.</param>
    public void Delete<M>(M model, TransactionType transactionType = TransactionType.ReadAndWrite) where M : IModelInstance
    {
        Commit(transaction => transaction.Delete(model), transactionType);
    }

    /// <summary>
    /// Commits a transaction with the specified action.
    /// </summary>
    /// <param name="func">The action to perform in the transaction.</param>
    /// <param name="transactionType">The type of the transaction.</param>
    public void Commit(Action<Transaction> func, TransactionType transactionType = TransactionType.ReadAndWrite)
    {
        using var transaction = Transaction(transactionType);
        func(transaction);
        transaction.Commit();
    }

    /// <summary>
    /// Commits a transaction with the specified function.
    /// </summary>
    /// <typeparam name="M">The type of the model.</typeparam>
    /// <param name="func">The function to perform in the transaction.</param>
    /// <param name="transactionType">The type of the transaction.</param>
    /// <returns>The result of the function.</returns>
    public M Commit<M>(Func<Transaction, M> func, TransactionType transactionType = TransactionType.ReadAndWrite) where M : IImmutableInstance
    {
        using var transaction = Transaction(transactionType);
        var result = func(transaction);
        transaction.Commit();

        return result;
    }

    /// <summary>
    /// Disposes the database provider.
    /// </summary>
    public void Dispose()
    {
        Provider.Dispose();
    }
}
