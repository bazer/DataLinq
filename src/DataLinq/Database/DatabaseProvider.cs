using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq;

/// <summary>
/// Provides a generic abstract database provider for a specific type of database model.
/// </summary>
/// <typeparam name="T">The type of the database model.</typeparam>
public abstract class DatabaseProvider<T> : DatabaseProvider
    where T : class, IDatabaseModel
{
    //public static DatabaseProvider<T> GetPrimaryProvider()
    //{
    //    return (DatabaseProvider<T>)GetPrimaryProvider(typeof(T));
    //}

    public ReadOnlyAccess<T> TypedReadOnlyAccess { get; set; }

    /// <summary>
    /// Initializes a new instance of the DatabaseProvider with the specified connection string and database type.
    /// </summary>
    /// <param name="connectionString">The connection string to the database.</param>
    /// <param name="databaseType">The type of the database.</param>
    protected DatabaseProvider(string connectionString, DatabaseType databaseType, DataLinqLoggingConfiguration loggingConfiguration) : base(connectionString, typeof(T), databaseType, loggingConfiguration)
    {
        TypedReadOnlyAccess = new ReadOnlyAccess<T>(this);
    }

    /// <summary>
    /// Initializes a new instance of the DatabaseProvider with the specified connection string, database type, and database name.
    /// </summary>
    /// <param name="connectionString">The connection string to the database.</param>
    /// <param name="databaseType">The type of the database.</param>
    /// <param name="databaseName">The name of the database.</param>
    protected DatabaseProvider(string connectionString, DatabaseType databaseType, DataLinqLoggingConfiguration loggingConfiguration, string? databaseName) : base(connectionString, typeof(T), databaseType, loggingConfiguration, databaseName)
    {
        TypedReadOnlyAccess = new ReadOnlyAccess<T>(this);
    }
}

/// <summary>
/// Abstract base class for database providers, encapsulating common database operations and properties.
/// </summary>
public abstract class DatabaseProvider : IDatabaseProvider, IDisposable
{
    public string DatabaseName { get; protected set; }
    public Type CsModelType { get; protected set; }
    public DatabaseType DatabaseType { get; }
    public DataLinqLoggingConfiguration LoggingConfiguration { get; }
    public abstract IDatabaseProviderConstants Constants { get; }

    public string ConnectionString { get; }
    public abstract DatabaseAccess DatabaseAccess { get; }
    public virtual ReadOnlyAccess ReadOnlyAccess { get; }
    public DatabaseDefinition Metadata { get; }
    public State State { get; }

    private static readonly object lockObject = new();

    /// <summary>
    /// Retrieves the table cache for a given table metadata.
    /// </summary>
    /// <param name="table">The metadata of the table to retrieve the cache for.</param>
    /// <returns>The table cache for the specified table.</returns>
    public TableCache GetTableCache(TableDefinition table) => State.Cache.TableCaches[table];

    /// <summary>
    /// Initializes a new instance of the DatabaseProvider class with the specified connection string, type of the model, database type, and optional database name.
    /// </summary>
    /// <param name="connectionString">The connection string to the database.</param>
    /// <param name="type">The type of the model that the database contains.</param>
    /// <param name="databaseType">The type of the database.</param>
    /// <param name="databaseName">The name of the database (optional).</param>
    protected DatabaseProvider(string connectionString, Type type, DatabaseType databaseType, DataLinqLoggingConfiguration loggingConfiguration, string? databaseName = null)
    {
        lock (lockObject)
        {
            if (DatabaseDefinition.LoadedDatabases.TryGetValue(type, out var metadata))
            {
                Metadata = metadata;
            }
            else
            {
                Metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(type);
                DatabaseDefinition.LoadedDatabases.TryAdd(type, Metadata);

                if (Metadata.UseCache)
                {
                    if (!Metadata.CacheLimits.Any())
                    {
                        Metadata.CacheLimits.Add((CacheLimitType.Megabytes, 256));
                        Metadata.CacheLimits.Add((CacheLimitType.Minutes, 30));
                    }

                    if (!Metadata.CacheCleanup.Any())
                    {
                        Metadata.CacheCleanup.Add((CacheCleanupType.Minutes, 10));
                    }

                    if (!Metadata.IndexCache.Any())
                    {
                        Metadata.IndexCache.Add((IndexCacheType.MaxAmountRows, 1000000));
                    }
                }
            }
        }

        CsModelType = type;
        DatabaseType = databaseType;
        LoggingConfiguration = loggingConfiguration;
        DatabaseName = databaseName ?? Metadata.DbName;
        ConnectionString = connectionString;
        State = new State(this, loggingConfiguration);

        this.ReadOnlyAccess = new ReadOnlyAccess(this);
    }

    

    /// <summary>
    /// Starts a new database transaction with the specified transaction type.
    /// </summary>
    /// <param name="transactionType">The type of the transaction.</param>
    /// <returns>A new Transaction object.</returns>
    public Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite)
    {
        return new Transaction(this, transactionType);
    }

    public M Commit<M>(Func<Transaction, M> func)
    {
        using var transaction = StartTransaction();
        var result = func(transaction);
        transaction.Commit();

        return result;
    }

    public void Commit(Action<Transaction> action)
    {
        using var transaction = StartTransaction();
        action(transaction);
        transaction.Commit();
    }

    /// <summary>
    /// Attaches an existing database transaction to this provider with the specified transaction type.
    /// </summary>
    /// <param name="dbTransaction">The existing database transaction.</param>
    /// <param name="transactionType">The type of the transaction.</param>
    /// <returns>A new Transaction object that wraps the provided IDbTransaction.</returns>
    public Transaction AttachTransaction(IDbTransaction dbTransaction, TransactionType transactionType = TransactionType.ReadAndWrite)
    {
        return new Transaction(this, dbTransaction, transactionType);
    }

    // Abstract methods definitions:
    public abstract IDbCommand ToDbCommand(IQuery query);
    public abstract string GetLastIdQuery();
    public abstract string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments);
    public abstract string GetOperatorSql(Operator @operator);
    public abstract Sql GetParameter(Sql sql, string key, object? value);
    public abstract Sql GetParameterValue(Sql sql, string key);
    public abstract Sql GetParameterComparison(Sql sql, string field, Query.Operator relation, string[] key);
    public abstract string GetParameterName(Operator relation, string[] key);
    public abstract Sql GetLimitOffset(Sql sql, int? limit, int? offset);
    public abstract Sql GetTableName(Sql sql, string tableName, string? alias = null);
    public abstract Sql GetCreateSql();
    public abstract DatabaseTransaction GetNewDatabaseTransaction(TransactionType type);
    public abstract DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type);
    public abstract bool DatabaseExists(string? databaseName = null);
    public abstract bool TableExists(string tableName, string? databaseName = null);
    public abstract bool FileOrServerExists();
    //public abstract void CreateDatabase(string? databaseName = null);
    public abstract IDataLinqDataWriter GetWriter();
    public abstract IDbConnection GetDbConnection();

    /// <summary>
    /// Releases all resources used by the DatabaseProvider.
    /// </summary>
    public void Dispose()
    {
        State.Dispose();
    }
}
