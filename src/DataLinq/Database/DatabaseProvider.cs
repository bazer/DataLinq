using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Query;
using DataLinq.Mutation;
using System;
using System.Data;
using System.Linq;
using DataLinq.Cache;

namespace DataLinq
{
    /// <summary>
    /// Provides a generic abstract database provider for a specific type of database model.
    /// </summary>
    /// <typeparam name="T">The type of the database model.</typeparam>
    public abstract class DatabaseProvider<T> : DatabaseProvider
        where T : class, IDatabaseModel
    {
        /// <summary>
        /// Initializes a new instance of the DatabaseProvider with the specified connection string and database type.
        /// </summary>
        /// <param name="connectionString">The connection string to the database.</param>
        /// <param name="databaseType">The type of the database.</param>
        protected DatabaseProvider(string connectionString, DatabaseType databaseType) : base(connectionString, typeof(T), databaseType)
        {
        }

        /// <summary>
        /// Initializes a new instance of the DatabaseProvider with the specified connection string, database type, and database name.
        /// </summary>
        /// <param name="connectionString">The connection string to the database.</param>
        /// <param name="databaseType">The type of the database.</param>
        /// <param name="databaseName">The name of the database.</param>
        protected DatabaseProvider(string connectionString, DatabaseType databaseType, string databaseName) : base(connectionString, typeof(T), databaseType, databaseName)
        {
        }
    }

    /// <summary>
    /// Abstract base class for database providers, encapsulating common database operations and properties.
    /// </summary>
    public abstract class DatabaseProvider : IDatabaseProvider, IDisposable
    {
        public string DatabaseName { get; }
        public DatabaseType DatabaseType { get; }
        public abstract IDatabaseProviderConstants Constants { get; }

        public string ConnectionString { get; }
        public DatabaseMetadata Metadata { get; }
        public State State { get; }

        private static readonly object lockObject = new();

        /// <summary>
        /// Retrieves the table cache for a given table metadata.
        /// </summary>
        /// <param name="table">The metadata of the table to retrieve the cache for.</param>
        /// <returns>The table cache for the specified table.</returns>
        public TableCache GetTableCache(TableMetadata table) => State.Cache.TableCaches.Single(x => x.Table == table);

        /// <summary>
        /// Initializes a new instance of the DatabaseProvider class with the specified connection string, type of the model, database type, and optional database name.
        /// </summary>
        /// <param name="connectionString">The connection string to the database.</param>
        /// <param name="type">The type of the model that the database contains.</param>
        /// <param name="databaseType">The type of the database.</param>
        /// <param name="databaseName">The name of the database (optional).</param>
        protected DatabaseProvider(string connectionString, Type type, DatabaseType databaseType, string? databaseName = null)
        {
            lock (lockObject)
            {
                if (DatabaseMetadata.LoadedDatabases.TryGetValue(type, out var metadata))
                {
                    Metadata = metadata;
                }
                else
                {
                    Metadata = MetadataFromInterfaceFactory.ParseDatabaseFromDatabaseModel(type);
                    DatabaseMetadata.LoadedDatabases.TryAdd(type, Metadata);
                }
            }

            DatabaseType = databaseType;
            DatabaseName = databaseName ?? Metadata.DbName;
            ConnectionString = connectionString;
            State = new State(this);
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
        public abstract Sql GetParameter(Sql sql, string key, object value);
        public abstract Sql GetParameterValue(Sql sql, string key);
        public abstract Sql GetParameterComparison(Sql sql, string field, Query.Relation relation, string key);
        public abstract Sql GetCreateSql();
        public abstract DatabaseTransaction GetNewDatabaseTransaction(TransactionType type);
        public abstract DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type);
        public abstract string GetExists(string? databaseName = null);
        public abstract bool FileOrServerExists();
        public abstract void CreateDatabase(string? databaseName = null);
        public abstract IDataLinqDataWriter GetWriter();

        /// <summary>
        /// Releases all resources used by the DatabaseProvider.
        /// </summary>
        public void Dispose()
        {
            State.Dispose();
        }
    }
}
