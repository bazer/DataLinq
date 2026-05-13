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
    public async Task Constructor_DoesNotCreateHistorySnapshotUntilRequested()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        DatabaseCache.IsBrowserRuntime = static () => true;

        try
        {
            using var cache = new DatabaseCache(
                new FakeDatabaseProvider(CreateMetadata()),
                DataLinqLoggingConfiguration.NullConfiguration);

            await Assert.That(cache.History.Count).IsEqualTo(0u);

            _ = cache.GetLatestSnapshot();

            await Assert.That(cache.History.Count).IsEqualTo(1u);
        }
        finally
        {
            DatabaseCache.IsBrowserRuntime = previousBrowserRuntime;
        }
    }

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
            await Assert.That(cache.CleanupScheduler).IsNull();
        }
        finally
        {
            DatabaseCache.IsBrowserRuntime = previousBrowserRuntime;
        }
    }

    [Test]
    [NotInParallel]
    public async Task Constructor_MultipleCleanupIntervals_StartsOneCoordinatedScheduler()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        var previousTimeProviderFactory = DatabaseCache.TimeProviderFactory;
        DatabaseCache.IsBrowserRuntime = static () => false;
        DatabaseCache.TimeProviderFactory = static () => TimeProvider.System;

        try
        {
            var cache = new DatabaseCache(
                new FakeDatabaseProvider(CreateMetadataWithMultipleCleanupIntervals()),
                DataLinqLoggingConfiguration.NullConfiguration);
            var scheduler = cache.CleanupScheduler;

            try
            {
                await Assert.That(cache.CleanCacheWorker).IsNull();
                await Assert.That(scheduler).IsNotNull();
                await Assert.That(scheduler!.ActiveScheduleCount).IsEqualTo(2);
                await Assert.That(scheduler.BackgroundWorkerCount).IsEqualTo(1);
            }
            finally
            {
                cache.Dispose();
            }

            await Assert.That(scheduler!.IsRunning).IsFalse();
            await Assert.That(scheduler.BackgroundWorkerCount).IsEqualTo(0);
        }
        finally
        {
            DatabaseCache.TimeProviderFactory = previousTimeProviderFactory;
            DatabaseCache.IsBrowserRuntime = previousBrowserRuntime;
        }
    }

    [Test]
    [NotInParallel]
    public async Task CleanupScheduler_RunDueScheduledCleanup_IsDeterministicWithoutBackgroundWorker()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        DatabaseCache.IsBrowserRuntime = static () => true;

        try
        {
            using var cache = new DatabaseCache(
                new FakeDatabaseProvider(CreateMetadataWithMultipleCleanupIntervals()),
                DataLinqLoggingConfiguration.NullConfiguration);
            using var scheduler = new CacheCleanupScheduler(
                cache,
                cache.Policy.CacheCleanup,
                TimeProvider.System);

            scheduler.RunDueScheduledCleanup(new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero));

            await Assert.That(scheduler.ActiveScheduleCount).IsEqualTo(2);
            await Assert.That(scheduler.BackgroundWorkerCount).IsEqualTo(0);
            await Assert.That(scheduler.IsRunning).IsFalse();
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

    [Test]
    [NotInParallel]
    public async Task Constructor_ExplicitCachePolicy_ReusesFrozenMetadataCollections()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        DatabaseCache.IsBrowserRuntime = static () => true;

        var metadata = CreateMetadataWithExplicitCachePolicy();
        var table = metadata.TableModels[0].Table;

        try
        {
            using var cache = new DatabaseCache(
                new FakeDatabaseProvider(metadata),
                DataLinqLoggingConfiguration.NullConfiguration);

            await Assert.That(ReferenceEquals(cache.Policy.DatabaseCacheLimits, metadata.CacheLimits)).IsTrue();
            await Assert.That(ReferenceEquals(cache.Policy.CacheCleanup, metadata.CacheCleanup)).IsTrue();
            await Assert.That(ReferenceEquals(cache.Policy.IndexCache, metadata.IndexCache)).IsTrue();
            await Assert.That(ReferenceEquals(cache.Policy.GetTableCacheLimits(table), table.CacheLimits)).IsTrue();
            await Assert.That(ReferenceEquals(cache.Policy.GetTableIndexCache(table), table.IndexCache)).IsTrue();
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

    private static DatabaseDefinition CreateMetadataWithExplicitCachePolicy()
    {
        var draft = new MetadataDatabaseDraft(
            "cachetest",
            new CsTypeDeclaration("CacheTestDb", "DataLinq.Tests.Unit", ModelCsType.Class))
        {
            CacheLimits = [(CacheLimitType.Rows, 200)],
            CacheCleanup = [(CacheCleanupType.Seconds, 15)],
            IndexCache = [(IndexCacheType.All, null)],
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Items",
                    new MetadataModelDraft(new CsTypeDeclaration("CacheTestItem", "DataLinq.Tests.Unit", ModelCsType.Class))
                    {
                        OriginalInterfaces =
                        [
                            new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)
                        ],
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id") { PrimaryKey = true })
                            {
                                Attributes = [new PrimaryKeyAttribute(), new ColumnAttribute("id")]
                            }
                        ]
                    },
                    new MetadataTableDraft("items")
                    {
                        CacheLimits = [(CacheLimitType.Rows, 50)],
                        IndexCache = [(IndexCacheType.MaxAmountRows, 100)]
                    })
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private static DatabaseDefinition CreateMetadataWithMultipleCleanupIntervals()
    {
        var draft = new MetadataDatabaseDraft(
            "cachetest",
            new CsTypeDeclaration("CacheTestDb", "DataLinq.Tests.Unit", ModelCsType.Class))
        {
            CacheCleanup =
            [
                (CacheCleanupType.Seconds, 30),
                (CacheCleanupType.Minutes, 5)
            ]
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
