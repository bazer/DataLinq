using DataLinq.Metadata;
using DataLinq.Query;
using DataLinq.Mutation;
using System;
using System.Data;
using DataLinq.Cache;

namespace DataLinq.Interfaces
{
    public interface IDatabaseProvider : IDisposable
    {
        string DatabaseName { get; }
        string ConnectionString { get; }
        DatabaseMetadata Metadata { get; }
        State State { get; }
        IDatabaseProviderConstants Constants { get; }
        IDbCommand ToDbCommand(IQuery query);

        Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite);

        DatabaseTransaction GetNewDatabaseTransaction(TransactionType type);

        DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type);

        string GetLastIdQuery();

        TableCache GetTableCache(TableMetadata table);

        Sql GetParameter(Sql sql, string key, object value);

        Sql GetParameterValue(Sql sql, string key);

        Sql GetParameterComparison(Sql sql, string field, Query.Relation relation, string prefix);

        string GetExists(string databaseName);

        void CreateDatabase(string databaseName);
        IDataLinqDataWriter GetWriter();
    }
}