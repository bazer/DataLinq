using System;
using System.Collections.Concurrent;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Diagnostics;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

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

    private int GetSubscriberCount()
    {
        var subscribersField = typeof(TableCache.CacheNotificationManager).GetField("_subscribers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var queue = (ConcurrentQueue<WeakReference<ICacheNotification>>)subscribersField!.GetValue(manager)!;
        return queue.Count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference<MockSubscriber> SubscribeAndForget()
    {
        var subscriber = new MockSubscriber();
        manager.Subscribe(subscriber);
        return new WeakReference<MockSubscriber>(subscriber);
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
