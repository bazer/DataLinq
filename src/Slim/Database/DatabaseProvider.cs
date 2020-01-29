using Slim.Interfaces;
using Slim.Metadata;
using Slim.Query;
using Slim.Mutation;
using System;
using System.Data;

namespace Slim
{
    public interface IDatabaseProvider
    {
        string Name { get; }

        string ConnectionString { get; }
        DatabaseMetadata Metadata { get; }
        State State { get; }
        IDbCommand ToDbCommand(IQuery query);

        Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite);

        DatabaseTransaction GetNewDatabaseTransaction(TransactionType type);

        string GetLastIdQuery();

        Sql GetParameter(Sql sql, string key, object value);

        Sql GetParameterValue(Sql sql, string key);

        Sql GetParameterComparison(Sql sql, string field, Query.Relation relation, string prefix);
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

        protected DatabaseProvider(string connectionString) : base(connectionString, typeof(T))
        {
        }

        protected DatabaseProvider(string connectionString, string databaseName) : base(connectionString, typeof(T), databaseName)
        {
        }
    }

    public abstract class DatabaseProvider : IDatabaseProvider
    {
        public string Name { get; }

        public string ConnectionString { get; }
        public DatabaseMetadata Metadata { get; }
        public State State { get; }
        protected string[] ProviderNames { get; set; }
        protected IDbConnection activeConnection;

        protected DatabaseProvider(string connectionString, Type databaseType = null, string name = null)
        {
            Name = name;
            ConnectionString = connectionString;

            if (databaseType != null)
            {
                Metadata = MetadataFromInterfaceFactory.ParseDatabase(databaseType);
                Metadata.DatabaseProvider = this;

                State = new State(Metadata);
            }

            if (Name == null && Metadata != null)
                Name = Metadata.Name;
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

        public abstract DatabaseTransaction GetNewDatabaseTransaction(TransactionType type);
    }
}