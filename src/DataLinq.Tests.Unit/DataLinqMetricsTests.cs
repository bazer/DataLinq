using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Diagnostics;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Tests.Unit;

public class DataLinqMetricsTests
{
    [Test]
    public async Task Snapshot_AggregatesProvidersAndTablesWithoutDoubleCounting()
    {
        DataLinqMetrics.Reset();

        var providerA = new FakeDatabaseProvider("provider-a", "employees", DatabaseType.MySQL);
        var providerB = new FakeDatabaseProvider("provider-b", "employees", DatabaseType.MySQL);

        var employeesA = DataLinqMetrics.RegisterTable(providerA, "employees");
        var deptEmpA = DataLinqMetrics.RegisterTable(providerA, "dept-emp");
        var employeesB = DataLinqMetrics.RegisterTable(providerB, "employees");

        DataLinqMetrics.RecordEntityQueryExecution(providerA);
        DataLinqMetrics.RecordEntityQueryExecution(providerA);
        DataLinqMetrics.RecordScalarQueryExecution(providerB);

        employeesA.RecordRowCacheHits(10);
        employeesA.RecordRowCacheMisses(2);
        employeesA.RecordDatabaseRowsLoaded(2);
        employeesA.RecordRowMaterialization();
        employeesA.RecordRowCacheStore();
        employeesA.RecordRelationReferenceCacheHit();
        employeesA.RecordRelationReferenceLoad();
        employeesA.RecordCacheNotificationSubscribe(5);
        employeesA.RecordCacheNotificationNotifySweep(snapshotEntries: 5, liveSubscribers: 4, currentQueueDepth: 1);

        deptEmpA.RecordRowCacheHits(3);
        deptEmpA.RecordRelationCollectionCacheHit();
        deptEmpA.RecordRelationCollectionLoad();
        deptEmpA.RecordCacheNotificationSubscribe(7);
        deptEmpA.RecordCacheNotificationCleanSweep(snapshotEntries: 7, requeuedSubscribers: 6, droppedSubscribers: 1, currentQueueDepth: 6);

        employeesB.RecordRowCacheMisses(4);
        employeesB.RecordDatabaseRowsLoaded(4);
        employeesB.RecordRowMaterialization();
        employeesB.RecordRowMaterialization();
        employeesB.RecordRowCacheStore();
        employeesB.RecordRelationReferenceLoad();
        employeesB.RecordCacheNotificationSubscribe(3);
        employeesB.RecordCacheNotificationCleanBusySkip();

        var snapshot = DataLinqMetrics.Snapshot();

        await Assert.That(snapshot.Providers.Length).IsEqualTo(2);

        var providerSnapshotA = snapshot.Providers.Single(x => x.ProviderInstanceId == "provider-a");
        var providerSnapshotB = snapshot.Providers.Single(x => x.ProviderInstanceId == "provider-b");

        await Assert.That(providerSnapshotA.DatabaseName).IsEqualTo("employees");
        await Assert.That(providerSnapshotB.DatabaseName).IsEqualTo("employees");
        await Assert.That(providerSnapshotA.Tables.Length).IsEqualTo(2);
        await Assert.That(providerSnapshotB.Tables.Length).IsEqualTo(1);

        await Assert.That(providerSnapshotA.Queries.EntityExecutions).IsEqualTo(2);
        await Assert.That(providerSnapshotA.Queries.ScalarExecutions).IsEqualTo(0);
        await Assert.That(providerSnapshotB.Queries.EntityExecutions).IsEqualTo(0);
        await Assert.That(providerSnapshotB.Queries.ScalarExecutions).IsEqualTo(1);

        await Assert.That(providerSnapshotA.RowCache.Hits).IsEqualTo(13);
        await Assert.That(providerSnapshotA.RowCache.Misses).IsEqualTo(2);
        await Assert.That(providerSnapshotA.RowCache.DatabaseRowsLoaded).IsEqualTo(2);
        await Assert.That(providerSnapshotA.RowCache.Materializations).IsEqualTo(1);
        await Assert.That(providerSnapshotA.RowCache.Stores).IsEqualTo(1);

        await Assert.That(providerSnapshotA.Relations.ReferenceCacheHits).IsEqualTo(1);
        await Assert.That(providerSnapshotA.Relations.ReferenceLoads).IsEqualTo(1);
        await Assert.That(providerSnapshotA.Relations.CollectionCacheHits).IsEqualTo(1);
        await Assert.That(providerSnapshotA.Relations.CollectionLoads).IsEqualTo(1);

        await Assert.That(providerSnapshotA.CacheNotifications.Subscriptions).IsEqualTo(2);
        await Assert.That(providerSnapshotA.CacheNotifications.ApproximateCurrentQueueDepth).IsEqualTo(7);
        await Assert.That(providerSnapshotA.CacheNotifications.LastNotifySnapshotEntries).IsEqualTo(5);
        await Assert.That(providerSnapshotA.CacheNotifications.LastNotifyLiveSubscribers).IsEqualTo(4);
        await Assert.That(providerSnapshotA.CacheNotifications.NotifySweeps).IsEqualTo(1);
        await Assert.That(providerSnapshotA.CacheNotifications.CleanSweeps).IsEqualTo(1);
        await Assert.That(providerSnapshotA.CacheNotifications.LastCleanSnapshotEntries).IsEqualTo(7);
        await Assert.That(providerSnapshotA.CacheNotifications.LastCleanRequeuedSubscribers).IsEqualTo(6);
        await Assert.That(providerSnapshotA.CacheNotifications.LastCleanDroppedSubscribers).IsEqualTo(1);
        await Assert.That(providerSnapshotA.CacheNotifications.CleanDroppedSubscribers).IsEqualTo(1);
        await Assert.That(providerSnapshotA.CacheNotifications.ApproximatePeakQueueDepth).IsEqualTo(7);

        await Assert.That(providerSnapshotB.RowCache.Hits).IsEqualTo(0);
        await Assert.That(providerSnapshotB.RowCache.Misses).IsEqualTo(4);
        await Assert.That(providerSnapshotB.RowCache.DatabaseRowsLoaded).IsEqualTo(4);
        await Assert.That(providerSnapshotB.RowCache.Materializations).IsEqualTo(2);
        await Assert.That(providerSnapshotB.RowCache.Stores).IsEqualTo(1);
        await Assert.That(providerSnapshotB.Relations.ReferenceLoads).IsEqualTo(1);
        await Assert.That(providerSnapshotB.CacheNotifications.Subscriptions).IsEqualTo(1);
        await Assert.That(providerSnapshotB.CacheNotifications.CleanBusySkips).IsEqualTo(1);
        await Assert.That(providerSnapshotB.CacheNotifications.ApproximatePeakQueueDepth).IsEqualTo(3);

        await Assert.That(snapshot.Queries.EntityExecutions).IsEqualTo(
            providerSnapshotA.Queries.EntityExecutions + providerSnapshotB.Queries.EntityExecutions);
        await Assert.That(snapshot.Queries.ScalarExecutions).IsEqualTo(
            providerSnapshotA.Queries.ScalarExecutions + providerSnapshotB.Queries.ScalarExecutions);
        await Assert.That(snapshot.RowCache.Hits).IsEqualTo(
            providerSnapshotA.RowCache.Hits + providerSnapshotB.RowCache.Hits);
        await Assert.That(snapshot.RowCache.Misses).IsEqualTo(
            providerSnapshotA.RowCache.Misses + providerSnapshotB.RowCache.Misses);
        await Assert.That(snapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(
            providerSnapshotA.RowCache.DatabaseRowsLoaded + providerSnapshotB.RowCache.DatabaseRowsLoaded);
        await Assert.That(snapshot.RowCache.Materializations).IsEqualTo(
            providerSnapshotA.RowCache.Materializations + providerSnapshotB.RowCache.Materializations);
        await Assert.That(snapshot.RowCache.Stores).IsEqualTo(
            providerSnapshotA.RowCache.Stores + providerSnapshotB.RowCache.Stores);
        await Assert.That(snapshot.Relations.ReferenceLoads).IsEqualTo(
            providerSnapshotA.Relations.ReferenceLoads + providerSnapshotB.Relations.ReferenceLoads);
        await Assert.That(snapshot.CacheNotifications.Subscriptions).IsEqualTo(
            providerSnapshotA.CacheNotifications.Subscriptions + providerSnapshotB.CacheNotifications.Subscriptions);
        await Assert.That(snapshot.CacheNotifications.CleanBusySkips).IsEqualTo(
            providerSnapshotA.CacheNotifications.CleanBusySkips + providerSnapshotB.CacheNotifications.CleanBusySkips);
        await Assert.That(snapshot.CacheNotifications.ApproximatePeakQueueDepth).IsEqualTo(7);
    }

    private sealed class FakeDatabaseProvider(string telemetryInstanceId, string databaseName, DatabaseType databaseType) : IDatabaseProvider
    {
        public string TelemetryInstanceId { get; } = telemetryInstanceId;
        public string DatabaseName { get; } = databaseName;
        public string ConnectionString => throw new NotSupportedException();
        public DatabaseDefinition Metadata => throw new NotSupportedException();
        public DatabaseAccess DatabaseAccess => throw new NotSupportedException();
        public State State => throw new NotSupportedException();
        public IDatabaseProviderConstants Constants => throw new NotSupportedException();
        public ReadOnlyAccess ReadOnlyAccess => throw new NotSupportedException();
        public DatabaseType DatabaseType { get; } = databaseType;

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
        public void Dispose() { }
    }
}
