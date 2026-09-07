using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.MariaDB;
using DataLinq.Mutation;
using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataLinq.Tests.Compliance;

public sealed class CacheIndexPublicationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task InvalidationRejectsLateEmptyAndNonemptyRelationKeySets(TestProviderDescriptor descriptor)
    {
        foreach (var initiallyEmpty in new[] { false, true })
            foreach (var commitMutation in new[] { false, true })
            {
                using var scope = TemporaryModelTestDatabase<CacheIndexPublicationDb>.Create(descriptor, $"index_publication_{initiallyEmpty}_{commitMutation}");
                scope.Database.Insert(new MutableCacheIndexParent { Id = 1 });
                if (!initiallyEmpty)
                    scope.Database.Insert(new MutableCacheIndexChild { Id = 1, ParentId = 1 });
                using var gate = new PublicationLogger();
                var logging = new DataLinqLoggingConfiguration(gate);
                using IDatabaseProvider provider = scope.Connection.DatabaseType switch
                {
                    DatabaseType.SQLite => new SQLiteProvider<CacheIndexPublicationDb>(scope.Connection.ConnectionString, logging),
                    DatabaseType.MySQL => new MySqlProvider<CacheIndexPublicationDb>(scope.Connection.ConnectionString, scope.Connection.DataSourceName, logging),
                    DatabaseType.MariaDB => new MariaDBProvider<CacheIndexPublicationDb>(scope.Connection.ConnectionString, scope.Connection.DataSourceName, logging),
                    _ => throw new NotSupportedException()
                };
                var parent = provider.Metadata.TableModels.Single(model => model.Table.DbName == "publication_parents");
                var relation = parent.Model.RelationProperties[nameof(CacheIndexParent.Children)];
                var table = relation.RelationPart.GetOtherSide().ColumnIndex.Table;
                var cache = provider.GetTableCache(table);
                gate.Arm();
                var read = Task.Factory.StartNew(() => cache.GetRows(1, relation, provider.ReadOnlyAccess).ToArray(),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                try
                {
                    gate.WaitUntilBlocked();
                    if (commitMutation)
                        provider.Commit(transaction => { transaction.Insert(new MutableCacheIndexChild { Id = 2, ParentId = 1 }); });
                    else
                    {
                        provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO publication_children (id, parent_id) VALUES (2, 1)");
                        cache.ClearCache();
                    }

                    gate.Release();
                    _ = await read.WaitAsync(TimeSpan.FromSeconds(20));
                    var current = cache.GetRows(1, relation, provider.ReadOnlyAccess).Cast<CacheIndexChild>().Select(row => row.Id).Order().ToArray();
                    await Assert.That(current).IsEquivalentTo(initiallyEmpty ? new[] { 2 } : new[] { 1, 2 });
                    await Assert.That(cache.IndicesCount.Any(index => index.count != 0)).IsTrue();
                }
                finally
                {
                    gate.Release();
                    await read.WaitAsync(TimeSpan.FromSeconds(20));
                }
            }
    }

    // This existing log point runs after the SQL cursor is disposed and before
    // relation keys are published, so even an empty result can be paused exactly.
    internal sealed class PublicationLogger : ILoggerFactory, ILogger
    {
        private readonly ManualResetEventSlim blocked = new();
        private readonly ManualResetEventSlim released = new();
        private int armed;
        internal void Arm() => Volatile.Write(ref armed, 1);
        internal void Release() => released.Set();
        internal void WaitUntilBlocked()
        {
            if (!blocked.Wait(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("The relation load did not reach publication.");
        }
        public ILogger CreateLogger(string categoryName) => categoryName == "DataLinq.Cache" ? this : NullLogger.Instance;
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id != EventIds.LoadRowsFromDatabase || Interlocked.Exchange(ref armed, 0) != 1)
                return;
            blocked.Set();
            if (!released.Wait(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("The relation publication gate was not released.");
        }
        public void Dispose()
        {
            blocked.Dispose();
            released.Dispose();
        }
    }
}

[Database("cache_index_publication"), UseCache, IndexCache(IndexCacheType.All)]
public sealed partial class CacheIndexPublicationDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<CacheIndexParent> Parents { get; } = new(source);
    public DbRead<CacheIndexChild> Children { get; } = new(source);
}

[Table("publication_parents")]
public abstract partial class CacheIndexParent(IRowData rowData, IDataSourceAccess source)
    : Immutable<CacheIndexParent, CacheIndexPublicationDb>(rowData, source), ITableModel<CacheIndexPublicationDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [Relation("publication_children", "parent_id", "FK_publication_child")]
    public abstract IImmutableRelation<CacheIndexChild> Children { get; }
}

[Table("publication_children")]
public abstract partial class CacheIndexChild(IRowData rowData, IDataSourceAccess source)
    : Immutable<CacheIndexChild, CacheIndexPublicationDb>(rowData, source), ITableModel<CacheIndexPublicationDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [ForeignKey("publication_parents", "id", "FK_publication_child"), Column("parent_id")]
    public abstract int ParentId { get; }
    [Relation("publication_parents", "id", "FK_publication_child")]
    public abstract CacheIndexParent Parent { get; }
}
