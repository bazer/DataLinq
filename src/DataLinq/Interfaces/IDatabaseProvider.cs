using System;
using System.Data;
using DataLinq.Cache;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Interfaces;

public interface IDatabaseProvider : IDisposable
{
    string DatabaseName { get; }
    string ConnectionString { get; }
    DatabaseMetadata Metadata { get; }
    DatabaseAccess DatabaseAccess { get; }
    State State { get; }
    IDatabaseProviderConstants Constants { get; }
    ReadOnlyAccess ReadOnlyAccess { get; }

    IDbCommand ToDbCommand(IQuery query);

    Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite);


    DatabaseTransaction GetNewDatabaseTransaction(TransactionType type);

    DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type);

    string GetLastIdQuery();

    TableCache GetTableCache(TableMetadata table);

    Sql GetParameter(Sql sql, string key, object? value);

    Sql GetParameterValue(Sql sql, string key);

    Sql GetParameterComparison(Sql sql, string field, Query.Relation relation, string[] prefix);

    Sql GetLimitOffset(Sql sql, int? limit, int? offset);

    string GetExists(string databaseName);

    IDataLinqDataWriter GetWriter();
}