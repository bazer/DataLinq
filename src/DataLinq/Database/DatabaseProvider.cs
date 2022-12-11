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
    public interface IDatabaseProviderRegister
    {
        static bool HasBeenRegistered { get; }
        static void RegisterProvider() => throw new NotImplementedException();
    }

    public interface IDatabaseProvider : IDisposable
    {
        string DatabaseName { get; }
        string ConnectionString { get; }
        DatabaseMetadata Metadata { get; }
        State State { get; }
        IDbCommand ToDbCommand(IQuery query);
        
        Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite);

        DatabaseTransaction GetNewDatabaseTransaction(TransactionType type);

        string GetLastIdQuery();

        TableCache GetTableCache(TableMetadata table);

        Sql GetParameter(Sql sql, string key, object value);

        Sql GetParameterValue(Sql sql, string key);

        Sql GetParameterComparison(Sql sql, string field, Query.Relation relation, string prefix);
        
        string GetExists(string databaseName);
        
        void CreateDatabase(string databaseName);
    }

    public abstract class DatabaseProvider<T> : DatabaseProvider
        where T : class, IDatabaseModel
    {
        //public T Read()
        //{
        //    return new Transaction<T>(this, TransactionType.NoTransaction).Schema;
        //}

        //public Transaction<T> Transaction(TransactionType transactionType = TransactionType.ReadAndWrite)
        //{
        //    return new Transaction<T>(this, transactionType);
        //}

        protected DatabaseProvider(string connectionString, DatabaseType databaseType) : base(connectionString, typeof(T), databaseType)
        {
        }

        protected DatabaseProvider(string connectionString, DatabaseType databaseType, string databaseName) : base(connectionString, typeof(T), databaseType, databaseName)
        {
        }
    }

    public abstract class DatabaseProvider : IDatabaseProvider
    {
        public string DatabaseName { get; }
        public DatabaseType DatabaseType { get; }

        public string ConnectionString { get; }
        public DatabaseMetadata Metadata { get; }
        public State State { get; }
        
        protected string[] ProviderNames { get; set; }
        protected IDbConnection activeConnection;

        private static object lockObject = new object();

        public TableCache GetTableCache(TableMetadata table) => State.Cache.TableCaches.Single(x => x.Table == table);

        protected DatabaseProvider(string connectionString, Type type, DatabaseType databaseType, string databaseName = null)
        {
            DatabaseType = databaseType;
            DatabaseName = databaseName;
            ConnectionString = connectionString;

            lock (lockObject)
            {
                if (DatabaseMetadata.LoadedDatabases.TryGetValue(type, out var metadata))
                    Metadata = metadata;
                else
                {
                    Metadata = MetadataFromInterfaceFactory.ParseDatabaseFromDatabaseModel(type);
                    //Metadata.DatabaseProvider = this;

                    DatabaseMetadata.LoadedDatabases.TryAdd(type, Metadata);
                }
            }

            //if (Name == null)
            //    Name = Metadata.Name;

            State = new State(this);
        }

        public Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite)
        {
            return new Transaction(this, transactionType);
        }

        public abstract IDbCommand ToDbCommand(IQuery query);

        public abstract string GetLastIdQuery();

        public abstract Sql GetParameter(Sql sql, string key, object value);

        public abstract Sql GetParameterValue(Sql sql, string key);

        public abstract Sql GetParameterComparison(Sql sql, string field, Query.Relation relation, string key);

        public abstract Sql GetCreateSql();

        public abstract DatabaseTransaction GetNewDatabaseTransaction(TransactionType type);

        public abstract string GetExists(string databaseName = null);
        public abstract void CreateDatabase(string databaseName = null);
        //public abstract void RegisterProvider();

        public void Dispose()
        {
            State.Dispose();
        }
    }
}