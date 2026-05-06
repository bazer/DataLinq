using System;
using System.Data;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Core.Factories;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using ThrowAway.Extensions;

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

    [Test]
    [NotInParallel]
    public async Task Constructor_UseCacheDefaults_AreEffectiveWithoutMutatingMetadata()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        DatabaseCache.IsBrowserRuntime = static () => true;

        var metadata = CreateMetadata(includeExplicitCleanup: false, useCache: true);

        try
        {
            using var cache = new DatabaseCache(
                new FakeDatabaseProvider(metadata),
                DataLinqLoggingConfiguration.NullConfiguration);

            await Assert.That(metadata.CacheLimits.Count).IsEqualTo(0);
            await Assert.That(metadata.CacheCleanup.Count).IsEqualTo(0);
            await Assert.That(metadata.IndexCache.Count).IsEqualTo(0);

            await Assert.That(cache.Policy.DatabaseCacheLimits.Count).IsEqualTo(2);
            await Assert.That(cache.Policy.DatabaseCacheLimits[0].limitType).IsEqualTo(CacheLimitType.Megabytes);
            await Assert.That(cache.Policy.DatabaseCacheLimits[0].amount).IsEqualTo(256);
            await Assert.That(cache.Policy.DatabaseCacheLimits[1].limitType).IsEqualTo(CacheLimitType.Minutes);
            await Assert.That(cache.Policy.DatabaseCacheLimits[1].amount).IsEqualTo(30);

            await Assert.That(cache.Policy.CacheCleanup.Count).IsEqualTo(1);
            await Assert.That(cache.Policy.CacheCleanup[0].cleanupType).IsEqualTo(CacheCleanupType.Minutes);
            await Assert.That(cache.Policy.CacheCleanup[0].amount).IsEqualTo(10);

            var indexCachePolicy = cache.GetIndexCachePolicy();
            await Assert.That(indexCachePolicy.Item1).IsEqualTo(IndexCacheType.MaxAmountRows);
            await Assert.That(indexCachePolicy.amount).IsEqualTo(1000000);
        }
        finally
        {
            DatabaseCache.IsBrowserRuntime = previousBrowserRuntime;
        }
    }

    [Test]
    [NotInParallel]
    public async Task ProviderConstructor_UseCacheDefaults_DoesNotMutateMetadata()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        DatabaseCache.IsBrowserRuntime = static () => true;

        var metadata = CreateMetadata(includeExplicitCleanup: false, useCache: true);

        DatabaseDefinition.TryRemoveLoadedDatabase(typeof(CacheDefaultsDatabaseModel), out _);

        try
        {
            using var provider = new CacheDefaultsProvider(metadata);

            await Assert.That(provider.Metadata.CacheLimits.Count).IsEqualTo(0);
            await Assert.That(provider.Metadata.CacheCleanup.Count).IsEqualTo(0);
            await Assert.That(provider.Metadata.IndexCache.Count).IsEqualTo(0);
            await Assert.That(provider.State.Cache.Policy.DatabaseCacheLimits.Count).IsEqualTo(2);
            await Assert.That(provider.State.Cache.GetIndexCachePolicy().Item1).IsEqualTo(IndexCacheType.MaxAmountRows);
        }
        finally
        {
            DatabaseDefinition.TryRemoveLoadedDatabase(typeof(CacheDefaultsDatabaseModel), out _);
            DatabaseCache.IsBrowserRuntime = previousBrowserRuntime;
        }
    }

    [Test]
    [NotInParallel]
    public async Task Constructor_CacheDisabledDefaultPolicyKeepsLegacyCleanupOnly()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        DatabaseCache.IsBrowserRuntime = static () => true;

        var metadata = CreateMetadata(includeExplicitCleanup: false);

        try
        {
            using var cache = new DatabaseCache(
                new FakeDatabaseProvider(metadata),
                DataLinqLoggingConfiguration.NullConfiguration);

            await Assert.That(cache.Policy.DatabaseCacheLimits.Count).IsEqualTo(0);
            await Assert.That(cache.Policy.IndexCache.Count).IsEqualTo(0);
            await Assert.That(cache.Policy.CacheCleanup.Count).IsEqualTo(1);
            await Assert.That(cache.Policy.CacheCleanup[0].cleanupType).IsEqualTo(CacheCleanupType.Minutes);
            await Assert.That(cache.Policy.CacheCleanup[0].amount).IsEqualTo(5);
        }
        finally
        {
            DatabaseCache.IsBrowserRuntime = previousBrowserRuntime;
        }
    }

    private static DatabaseDefinition CreateMetadata(bool includeExplicitCleanup = true, bool useCache = false)
    {
        var draft = new MetadataDatabaseDraft(
            "cachetest",
            new CsTypeDeclaration("CacheTestDb", "DataLinq.Tests.Unit", ModelCsType.Class))
        {
            UseCache = useCache,
            CacheCleanup = includeExplicitCleanup
                ? [(CacheCleanupType.Minutes, 1)]
                : []
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private sealed class CacheDefaultsDatabaseModel
    {
    }

    private sealed class CacheDefaultsProvider : DataLinq.DatabaseProvider
    {
        public CacheDefaultsProvider(DatabaseDefinition metadata)
            : base(
                "Data Source=:memory:",
                typeof(CacheDefaultsDatabaseModel),
                DatabaseType.SQLite,
                DataLinqLoggingConfiguration.NullConfiguration,
                metadataFactory: () => metadata)
        {
        }

        public override IDatabaseProviderConstants Constants => throw new NotSupportedException();
        public override DatabaseAccess DatabaseAccess => throw new NotSupportedException();

        public override IDbCommand ToDbCommand(IQuery query) => throw new NotSupportedException();
        public override string GetLastIdQuery() => throw new NotSupportedException();
        public override string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments) => throw new NotSupportedException();
        public override string GetOperatorSql(Operator @operator) => throw new NotSupportedException();
        public override Sql GetParameter(Sql sql, string key, object? value) => throw new NotSupportedException();
        public override Sql GetParameterValue(Sql sql, string key) => throw new NotSupportedException();
        public override Sql GetParameterComparison(Sql sql, string field, Operator relation, string[] key) => throw new NotSupportedException();
        public override string GetParameterName(Operator relation, string[] key) => throw new NotSupportedException();
        public override Sql GetLimitOffset(Sql sql, int? limit, int? offset) => throw new NotSupportedException();
        public override Sql GetTableName(Sql sql, string tableName, string? alias = null) => throw new NotSupportedException();
        public override Sql GetCreateSql() => throw new NotSupportedException();
        public override DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) => throw new NotSupportedException();
        public override DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type) => throw new NotSupportedException();
        public override bool DatabaseExists(string? databaseName = null) => throw new NotSupportedException();
        public override bool TableExists(string tableName, string? databaseName = null) => throw new NotSupportedException();
        public override bool FileOrServerExists() => throw new NotSupportedException();
        public override IDataLinqDataWriter GetWriter() => throw new NotSupportedException();
        public override IDbConnection GetDbConnection() => throw new NotSupportedException();
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
