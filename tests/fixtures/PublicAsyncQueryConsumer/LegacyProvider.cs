using System;
using System.Data;
using DataLinq;
using DataLinq.Cache;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

// No async members: this is an external source compatibility/dispatch probe.
// Every synchronous route throws so a default cannot silently use database I/O.
internal sealed class LegacyProvider : IDatabaseProvider
{
    public string TelemetryInstanceId => "legacy";
    public string DatabaseName => throw new Exception("sync");
    public string ConnectionString => throw new Exception("sync");
    public DatabaseDefinition Metadata => throw new Exception("sync");
    public DatabaseAccess DatabaseAccess => throw new Exception("sync");
    public State State => throw new Exception("sync");
    public IDatabaseProviderConstants Constants => throw new Exception("sync");
    public ReadOnlyAccess ReadOnlyAccess => throw new Exception("sync");
    public DatabaseType DatabaseType => throw new Exception("sync");
    public IDbCommand ToDbCommand(IQuery query) => throw new Exception("sync");
    public Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite) => throw new Exception("sync");
    public DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) => throw new Exception("sync");
    public DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type) => throw new Exception("sync");
    public string GetLastIdQuery() => throw new Exception("sync");
    public string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments) => throw new Exception("sync");
    public TableCache GetTableCache(TableDefinition table) => throw new Exception("sync");
    public string GetOperatorSql(Operator @operator) => throw new Exception("sync");
    public Sql GetParameter(Sql sql, string key, object? value) => throw new Exception("sync");
    public Sql GetParameterValue(Sql sql, string key) => throw new Exception("sync");
    public string GetParameterName(Operator relation, string[] key) => throw new Exception("sync");
    public Sql GetParameterComparison(Sql sql, string field, Operator @operator, string[] prefix) => throw new Exception("sync");
    public Sql GetLimitOffset(Sql sql, int? limit, int? offset) => throw new Exception("sync");
    public bool DatabaseExists(string? databaseName = null) => throw new Exception("sync");
    public bool FileOrServerExists() => throw new Exception("sync");
    public IDataLinqDataWriter GetWriter() => throw new Exception("sync");
    public Sql GetTableName(Sql sql, string tableName, string? alias = null) => throw new Exception("sync");
    public M Commit<M>(Func<Transaction, M> func) => throw new Exception("sync");
    public void Commit(Action<Transaction> action) => throw new Exception("sync");
    public bool TableExists(string tableName, string? databaseName = null) => throw new Exception("sync");
    public IDbConnection GetDbConnection() => throw new Exception("sync");
    public Sql GetCreateSql() => throw new Exception("sync");
    public void Dispose() => throw new Exception("sync");
}

internal sealed class LegacyTransaction() : DatabaseTransaction(TransactionType.ReadAndWrite)
{
    public override void Commit() => throw new Exception("sync");
    public override void Rollback() => throw new Exception("sync");
    public override void Dispose() => throw new Exception("sync");
    public override int ExecuteNonQuery(IDbCommand command) => throw new Exception("sync");
    public override int ExecuteNonQuery(string query) => throw new Exception("sync");
    public override object? ExecuteScalar(IDbCommand command) => throw new Exception("sync");
    public override object? ExecuteScalar(string query) => throw new Exception("sync");
    public override T ExecuteScalar<T>(IDbCommand command) => throw new Exception("sync");
    public override T ExecuteScalar<T>(string query) => throw new Exception("sync");
    public override IDataLinqDataReader ExecuteReader(IDbCommand command) => throw new Exception("sync");
    public override IDataLinqDataReader ExecuteReader(string query) => throw new Exception("sync");
}
