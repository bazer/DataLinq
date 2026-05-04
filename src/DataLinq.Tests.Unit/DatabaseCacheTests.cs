using System;
using System.Data;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Tests.Unit;

public class DatabaseCacheTests
{
    [Test]
    [NotInParallel]
    public async Task Constructor_DoesNotStartCleanupWorker_InBrowserRuntime()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        DatabaseCache.IsBrowserRuntime = static () => true;

        try
        {
            using var cache = new DatabaseCache(
                new FakeDatabaseProvider(CreateMetadata()),
                DataLinqLoggingConfiguration.NullConfiguration);

            await Assert.That(cache.CleanCacheWorker).IsNull();
        }
        finally
        {
            DatabaseCache.IsBrowserRuntime = previousBrowserRuntime;
        }
    }

    private static DatabaseDefinition CreateMetadata()
    {
        var definition = new DatabaseDefinition(
            "cachetest",
            new CsTypeDeclaration("CacheTestDb", "DataLinq.Tests.Unit", ModelCsType.Class));

        definition.CacheCleanup.Add((CacheCleanupType.Minutes, 1));
        return definition;
    }

    private sealed class FakeDatabaseProvider(DatabaseDefinition metadata) : IDatabaseProvider
    {
        public string TelemetryInstanceId { get; } = Guid.NewGuid().ToString("N");
        public string DatabaseName => metadata.DbName;
        public string ConnectionString => "Data Source=:memory:";
        public DatabaseDefinition Metadata => metadata;
        public DatabaseAccess DatabaseAccess => throw new NotSupportedException();
        public State State => throw new NotSupportedException();
        public IDatabaseProviderConstants Constants => throw new NotSupportedException();
        public ReadOnlyAccess ReadOnlyAccess => throw new NotSupportedException();
        public DatabaseType DatabaseType => DatabaseType.SQLite;

        public IDbCommand ToDbCommand(IQuery query) => throw new NotSupportedException();
        public Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite) => throw new NotSupportedException();
        public DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) => throw new NotSupportedException();
        public DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type) => throw new NotSupportedException();
        public string GetLastIdQuery() => throw new NotSupportedException();
        public string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments) => throw new NotSupportedException();
        public TableCache GetTableCache(TableDefinition table) => throw new NotSupportedException();
        public string GetOperatorSql(Operator @operator) => throw new NotSupportedException();
        public Sql GetParameter(Sql sql, string key, object? value) => throw new NotSupportedException();
        public Sql GetParameterValue(Sql sql, string key) => throw new NotSupportedException();
        public string GetParameterName(Operator relation, string[] key) => throw new NotSupportedException();
        public Sql GetParameterComparison(Sql sql, string field, Operator @operator, string[] prefix) => throw new NotSupportedException();
        public Sql GetLimitOffset(Sql sql, int? limit, int? offset) => throw new NotSupportedException();
        public bool DatabaseExists(string? databaseName = null) => throw new NotSupportedException();
        public bool FileOrServerExists() => throw new NotSupportedException();
        public IDataLinqDataWriter GetWriter() => throw new NotSupportedException();
        public Sql GetTableName(Sql sql, string tableName, string? alias = null) => throw new NotSupportedException();
        public M Commit<M>(Func<Transaction, M> func) => throw new NotSupportedException();
        public void Commit(Action<Transaction> action) => throw new NotSupportedException();
        public bool TableExists(string tableName, string? databaseName = null) => throw new NotSupportedException();
        public IDbConnection GetDbConnection() => throw new NotSupportedException();
        public Sql GetCreateSql() => throw new NotSupportedException();
        public void Dispose()
        {
        }
    }
}
