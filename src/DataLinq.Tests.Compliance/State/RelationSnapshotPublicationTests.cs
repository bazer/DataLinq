using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Cache;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.MariaDB;
using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class RelationSnapshotPublicationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DelayedNotificationForOldLoadPreservesTheNewSnapshot(TestProviderDescriptor descriptor)
    {
        using var scope = TemporaryModelTestDatabase<CacheIndexPublicationDb>.Create(descriptor, "relation_delayed_notification");
        scope.Database.Insert(new MutableCacheIndexParent { Id = 1 });
        scope.Database.Insert(new MutableCacheIndexChild { Id = 1, ParentId = 1 });
        var relation = scope.Database.Query().Parents.Single(row => row.Id == 1).Children;
        var cache = scope.Database.Provider.GetTableCache(scope.Database.Provider.Metadata.TableModels
            .Single(model => model.Table.DbName == "publication_children").Table);
        using var blocker = new BlockingNotification();
        cache.SubscribeToChanges(blocker);
        _ = relation.Values;
        scope.Database.Provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO publication_children (id, parent_id) VALUES (2, 1)");
        var invalidate = Task.Factory.StartNew(cache.ClearCache,
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            blocker.WaitUntilBlocked();
            relation.Clear();
            var fresh = relation.ToFrozenDictionary();
            blocker.Release();
            await invalidate.WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(relation.ToFrozenDictionary()).IsSameReferenceAs(fresh);
            await Assert.That(fresh.Values.Select(row => row.Id).Order().ToArray()).IsEquivalentTo([1, 2]);
        }
        finally
        {
            blocker.Release();
            await invalidate.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    private sealed class BlockingNotification : ICacheNotification, IDisposable
    {
        private readonly ManualResetEventSlim blocked = new();
        private readonly ManualResetEventSlim released = new();
        internal void WaitUntilBlocked()
        {
            if (!blocked.Wait(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("The notification did not reach its barrier.");
        }
        internal void Release() => released.Set();
        public void Clear()
        {
            blocked.Set();
            if (!released.Wait(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("The notification was not released.");
        }
        public void Dispose()
        {
            blocked.Dispose();
            released.Dispose();
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ChangeBeforeFirstSubscriptionCannotPublishAnObsoleteSnapshot(TestProviderDescriptor descriptor)
    {
        foreach (var initiallyEmpty in new[] { false, true })
            foreach (var invalidation in new[] { "table-clear", "commit", "relation-clear" })
                foreach (var warmBeforeRelease in new[] { false, true })
                {
                    using var scope = TemporaryModelTestDatabase<CacheIndexPublicationDb>.Create(descriptor, $"relation_snapshot_{initiallyEmpty}_{invalidation}_{warmBeforeRelease}");
                    scope.Database.Insert(new MutableCacheIndexParent { Id = 1 });
                    if (!initiallyEmpty)
                        scope.Database.Insert(new MutableCacheIndexChild { Id = 1, ParentId = 1 });
                    using var gate = new CacheIndexPublicationTests.PublicationLogger();
                    var logging = new DataLinqLoggingConfiguration(gate);
                    using IDatabaseProvider provider = scope.Connection.DatabaseType switch
                    {
                        DatabaseType.SQLite => new SQLiteProvider<CacheIndexPublicationDb>(scope.Connection.ConnectionString, logging),
                        DatabaseType.MySQL => new MySqlProvider<CacheIndexPublicationDb>(scope.Connection.ConnectionString, scope.Connection.DataSourceName, logging),
                        DatabaseType.MariaDB => new MariaDBProvider<CacheIndexPublicationDb>(scope.Connection.ConnectionString, scope.Connection.DataSourceName, logging),
                        _ => throw new NotSupportedException()
                    };
                    var parent = provider.Metadata.TableModels.Single(model => model.Table.DbName == "publication_parents");
                    var property = parent.Model.RelationProperties[nameof(CacheIndexParent.Children)];
                    var cache = provider.GetTableCache(property.RelationPart.GetOtherSide().ColumnIndex.Table);
                    IImmutableRelation<CacheIndexChild> relation = new ImmutableRelation<CacheIndexChild, int>(1, provider.ReadOnlyAccess, property);
                    gate.Arm();
                    var read = Task.Factory.StartNew(relation.ToFrozenDictionary,
                        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    try
                    {
                        gate.WaitUntilBlocked();
                        if (invalidation == "relation-clear")
                            relation.Clear();
                        else if (invalidation == "commit")
                            provider.Commit(transaction => { transaction.Insert(new MutableCacheIndexChild { Id = 2, ParentId = 1 }); });
                        else
                        {
                            provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO publication_children (id, parent_id) VALUES (2, 1)");
                            cache.ClearCache();
                        }

                        var fresh = warmBeforeRelease ? relation.ToFrozenDictionary() : null;
                        gate.Release();
                        _ = await read.WaitAsync(TimeSpan.FromSeconds(20));
                        var loadsBefore = DataLinqMetrics.Snapshot().Providers.Single(item => item.ProviderInstanceId == provider.TelemetryInstanceId)
                            .Tables.Single(item => item.TableName == "publication_children").Relations.CollectionLoads;
                        var subsequent = relation.ToFrozenDictionary();
                        var loadsAfter = DataLinqMetrics.Snapshot().Providers.Single(item => item.ProviderInstanceId == provider.TelemetryInstanceId)
                            .Tables.Single(item => item.TableName == "publication_children").Relations.CollectionLoads;
                        var expected = invalidation == "relation-clear"
                            ? initiallyEmpty ? Array.Empty<int>() : [1]
                            : initiallyEmpty ? [2] : new[] { 1, 2 };
                        await Assert.That(subsequent.Values.Select(row => row.Id).Order().ToArray()).IsEquivalentTo(expected);
                        if (fresh is not null)
                            await Assert.That(subsequent).IsSameReferenceAs(fresh);
                        await Assert.That(loadsAfter - loadsBefore).IsEqualTo(warmBeforeRelease ? 0L : 1L);
                    }
                    finally
                    {
                        gate.Release();
                        await read.WaitAsync(TimeSpan.FromSeconds(20));
                    }
                }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ReferenceChangeBeforeSubscriptionCannotPublishObsoleteOrMissingValue(TestProviderDescriptor descriptor)
    {
        foreach (var initiallyMissing in new[] { false, true })
            foreach (var warmBeforeRelease in new[] { false, true })
            {
                using var scope = TemporaryModelTestDatabase<CacheIndexPublicationDb>.Create(descriptor, $"reference_snapshot_{initiallyMissing}_{warmBeforeRelease}");
                if (!initiallyMissing)
                    scope.Database.Insert(new MutableCacheIndexParent { Id = 1 });
                using var gate = new CacheIndexPublicationTests.PublicationLogger();
                var logging = new DataLinqLoggingConfiguration(gate);
                using IDatabaseProvider provider = scope.Connection.DatabaseType switch
                {
                    DatabaseType.SQLite => new SQLiteProvider<CacheIndexPublicationDb>(scope.Connection.ConnectionString, logging),
                    DatabaseType.MySQL => new MySqlProvider<CacheIndexPublicationDb>(scope.Connection.ConnectionString, scope.Connection.DataSourceName, logging),
                    DatabaseType.MariaDB => new MariaDBProvider<CacheIndexPublicationDb>(scope.Connection.ConnectionString, scope.Connection.DataSourceName, logging),
                    _ => throw new NotSupportedException()
                };
                var child = provider.Metadata.TableModels.Single(model => model.Table.DbName == "publication_children");
                var property = child.Model.RelationProperties[nameof(CacheIndexChild.Parent)];
                var cache = provider.GetTableCache(property.RelationPart.GetOtherSide().ColumnIndex.Table);
                var reference = new ImmutableForeignKey<CacheIndexParent, int>(1, provider.ReadOnlyAccess, property);
                gate.Arm();
                var read = Task.Factory.StartNew(() => reference.Value,
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                try
                {
                    gate.WaitUntilBlocked();
                    provider.DatabaseAccess.ExecuteNonQuery(initiallyMissing
                        ? "INSERT INTO publication_parents (id) VALUES (1)"
                        : "DELETE FROM publication_parents WHERE id = 1");
                    cache.ClearCache();
                    var fresh = warmBeforeRelease ? reference.Value : null;
                    gate.Release();
                    _ = await read.WaitAsync(TimeSpan.FromSeconds(20));
                    var subsequent = reference.Value;
                    await Assert.That(subsequent?.Id).IsEqualTo(initiallyMissing ? 1 : (int?)null);
                    if (warmBeforeRelease)
                        await Assert.That(ReferenceEquals(subsequent, fresh)).IsTrue();
                }
                finally
                {
                    gate.Release();
                    await read.WaitAsync(TimeSpan.FromSeconds(20));
                }
            }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ConcurrentClearAndAllReadShapesAlwaysReturnValidSnapshots(TestProviderDescriptor descriptor)
    {
        using var scope = TemporaryModelTestDatabase<CacheIndexPublicationDb>.Create(descriptor, "relation_snapshot_read_clear");
        scope.Database.Insert(new MutableCacheIndexParent { Id = 1 });
        for (var id = 1; id <= 3; id++)
            scope.Database.Insert(new MutableCacheIndexChild { Id = id, ParentId = 1 });
        var relation = scope.Database.Query().Parents.Single(row => row.Id == 1).Children;
        _ = relation.Values;
        using var start = new Barrier(5);
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(() =>
        {
            if (!start.SignalAndWait(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("Readers did not start.");
            for (var iteration = 0; iteration < 4000; iteration++)
            {
                var values = relation.Values;
                if (values.IsDefault || values.Length != 3 || relation.Count != 3 ||
                    relation.Keys.Length != 3 || relation.ToFrozenDictionary().Count != 3 ||
                    relation.AsEnumerable().Count() != 3 || relation.ToArray().Length != 3 ||
                    !relation.ContainsKey(DataLinqKey.FromValue(1)) || relation.Get(DataLinqKey.FromValue(1))?.Id != 1)
                    throw new InvalidOperationException("A relation exposed an incomplete snapshot.");
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        var clear = Task.Factory.StartNew(() =>
        {
            if (!start.SignalAndWait(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("Clear did not start.");
            for (var iteration = 0; iteration < 12000; iteration++)
            {
                relation.Clear();
                Thread.Yield();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await Task.WhenAll(readers.Append(clear)).WaitAsync(TimeSpan.FromSeconds(40));
    }
}
