using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Core.Factories;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit;

public class CacheNotificationManagerTests
{
    private readonly TableCache.CacheNotificationManager manager;

    public CacheNotificationManagerTests()
    {
        DataLinqMetrics.Reset();
        var provider = new FakeDatabaseProvider();
        var metricsHandle = DataLinqMetrics.RegisterTable(provider, "test-table");
        manager = new TableCache.CacheNotificationManager(metricsHandle);
    }

    [Test]
    [NotInParallel]
    public async Task Clean_RemovesDeadSubscribers()
    {
        var liveSubscriber = new MockSubscriber();
        manager.Subscribe(liveSubscriber);
        var weakReference = SubscribeAndForget();

        await Assert.That(GetSubscriberCount()).IsEqualTo(2);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        await Assert.That(weakReference.TryGetTarget(out _)).IsFalse();
        manager.Clean();

        await Assert.That(GetSubscriberCount()).IsEqualTo(1);
        await Assert.That(liveSubscriber.ClearCacheCallCount).IsEqualTo(0);

        manager.Notify();

        await Assert.That(liveSubscriber.ClearCacheCallCount).IsEqualTo(1);
        await Assert.That(GetSubscriberCount()).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task SubscribeDuringNotify_DoesNotLoseSubscriber()
    {
        using var waitHandle = new ManualResetEventSlim(false);
        var subscriberA = new MockSubscriber(waitHandle, delayMs: 100);
        var subscriberB = new MockSubscriber();

        manager.Subscribe(subscriberA);

        var notifyTask = Task.Run(manager.Notify);
        await Assert.That(waitHandle.Wait(TimeSpan.FromSeconds(2))).IsTrue();

        manager.Subscribe(subscriberB);
        await notifyTask;

        manager.Notify();

        await Assert.That(subscriberA.ClearCacheCallCount).IsEqualTo(1);
        await Assert.That(subscriberB.ClearCacheCallCount).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task SubscribeAndNotify_NotifiesLiveSubscriber()
    {
        var subscriber = new MockSubscriber();
        manager.Subscribe(subscriber);

        manager.Notify();

        await Assert.That(subscriber.ClearCacheCallCount).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task Notify_DoesNotNotifyGarbageCollectedSubscriber()
    {
        var liveSubscriber = new MockSubscriber();
        manager.Subscribe(liveSubscriber);
        var weakReference = SubscribeAndForget();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        manager.Notify();

        await Assert.That(weakReference.TryGetTarget(out _)).IsFalse();
        await Assert.That(liveSubscriber.ClearCacheCallCount).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task Clean_DoesNotDropLiveSubscribers()
    {
        var subscriber = new MockSubscriber();
        manager.Subscribe(subscriber);

        manager.Clean();

        await Assert.That(GetSubscriberCount()).IsEqualTo(1);
        await Assert.That(subscriber.ClearCacheCallCount).IsEqualTo(0);

        manager.Notify();

        await Assert.That(subscriber.ClearCacheCallCount).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task GetMemoryEstimate_TableWideSubscription_CountsNotificationBytes()
    {
        var subscriber = new MockSubscriber();

        manager.Subscribe(subscriber);

        var estimate = manager.GetMemoryEstimate();

        await Assert.That(estimate.NotificationBytes).IsGreaterThan(0);
        await Assert.That(estimate.RelationObjectBytes).IsEqualTo(0);

        manager.Notify();

        await Assert.That(manager.GetMemoryEstimate().NotificationBytes).IsLessThan(estimate.NotificationBytes);
    }

    [Test]
    [NotInParallel]
    public async Task GetMemoryEstimate_RelationSubscription_CountsRetainedKeysWithoutRetainingSubscriber()
    {
        var table = CreateNotificationTable();
        var relationKey = new RelationCacheKey(
            table.ColumnIndices.Single(x => x.Name == "idx_memory_notification_rows_name"),
            DataLinqKey.FromValue("dept-1"));
        var weakReference = SubscribeRelationAndForget(
            relationKey,
            [DataLinqKey.FromValue(1), DataLinqKey.FromValues([2, "dept-1"])]);
        var occupiedEstimate = manager.GetMemoryEstimate();

        await Assert.That(occupiedEstimate.NotificationBytes).IsGreaterThan(0);
        await Assert.That(occupiedEstimate.RelationObjectBytes).IsGreaterThan(0);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        await Assert.That(weakReference.TryGetTarget(out _)).IsFalse();
        manager.Clean();

        var cleanedEstimate = manager.GetMemoryEstimate();
        await Assert.That(cleanedEstimate.RelationObjectBytes).IsLessThan(occupiedEstimate.RelationObjectBytes);
        await Assert.That(cleanedEstimate.NotificationBytes).IsLessThan(occupiedEstimate.NotificationBytes);
    }

    private int GetSubscriberCount()
    {
        var subscribersField = typeof(TableCache.CacheNotificationManager).GetField("_subscribers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var queue = subscribersField!.GetValue(manager)!;
        return (int)queue.GetType().GetProperty("Count")!.GetValue(queue)!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference<MockSubscriber> SubscribeAndForget()
    {
        var subscriber = new MockSubscriber();
        manager.Subscribe(subscriber);
        return new WeakReference<MockSubscriber>(subscriber);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference<MockSubscriber> SubscribeRelationAndForget(
        RelationCacheKey relationKey,
        IReadOnlyCollection<DataLinqKey> loadedPrimaryKeys)
    {
        var subscriber = new MockSubscriber();
        manager.Subscribe(subscriber, null, relationKey, loadedPrimaryKeys);
        return new WeakReference<MockSubscriber>(subscriber);
    }

    private static TableDefinition CreateNotificationTable()
    {
        var draft = new MetadataDatabaseDraft(
            "NotificationMemoryDb",
            new CsTypeDeclaration("NotificationMemoryDb", "DataLinq.Tests.Unit", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration("NotificationMemoryRow", "DataLinq.Tests.Unit", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "integer")]
                                })
                            {
                                Attributes = [new PrimaryKeyAttribute(), new ColumnAttribute("id")],
                                CsSize = sizeof(int)
                            },
                            new MetadataValuePropertyDraft(
                                "Name",
                                new CsTypeDeclaration(typeof(string)),
                                new MetadataColumnDraft("name")
                                {
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "text")]
                                })
                            {
                                Attributes =
                                [
                                    new ColumnAttribute("name"),
                                    new IndexAttribute("idx_memory_notification_rows_name", IndexCharacteristic.Simple)
                                ]
                            }
                        ]
                    },
                    new MetadataTableDraft("memory_notification_rows"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException().TableModels.Single().Table;
    }

    private sealed class MockSubscriber(ManualResetEventSlim? waitHandle = null, int delayMs = 0) : ICacheNotification
    {
        private int clearCacheCallCount;

        public int ClearCacheCallCount => clearCacheCallCount;

        public void Clear()
        {
            if (delayMs > 0)
                Thread.Sleep(delayMs);

            Interlocked.Increment(ref clearCacheCallCount);
            waitHandle?.Set();
        }
    }

    private sealed class FakeDatabaseProvider : IDatabaseProvider
    {
        public string TelemetryInstanceId { get; } = Guid.NewGuid().ToString("N");
        public string DatabaseName => "test-db";
        public string ConnectionString => throw new NotSupportedException();
        public DatabaseDefinition Metadata => throw new NotSupportedException();
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
        public void Dispose() { }
    }
}
