using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class CachePublicationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task InvalidationDuringConstructionCannotRepublishOldRows(TestProviderDescriptor provider)
    {
        foreach (var route in new[] { "scalar", "batch", "text-key" })
            foreach (var invalidation in new[] { "clear", "precise", "commit" })
                foreach (var warmBeforeRelease in new[] { false, true })
                {
                    using var scope = TemporaryModelTestDatabase<CachePublicationDb>.Create(provider,
                        $"publication_{route}_{invalidation}_{warmBeforeRelease}");
                    var database = scope.Database;
                    database.Insert(new MutableCachePublicationRow { Id = 1, Name = "old" });
                    database.Insert(new MutableCachePublicationRow { Id = 2, Name = "old" });
                    database.Insert(new MutableCachePublicationTextRow { Id = "one", Name = "old" });
                    database.Cache.Clear();
                    using var gate = CachePublicationGate.Install(database.Provider.TelemetryInstanceId);
                    var read = Task.Factory.StartNew(() =>
                    {
                        if (route == "text-key")
                            return database.Query().TextRows.Single(row => row.Id == "one").Name;
                        if (route == "batch")
                            return database.Query().Rows.Where(row => row.Name == "old").ToArray().Single(row => row.Id == 1).Name;
                        return database.Query().Rows.Single(row => row.Id == 1).Name;
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    try
                    {
                        gate.WaitUntilBlocked();
                        if (invalidation == "commit")
                        {
                            if (route == "text-key")
                            {
                                var change = database.Query().TextRows.Single(row => row.Id == "one").Mutate();
                                change.Name = "new";
                                database.Update(change);
                            }
                            else
                            {
                                var change = database.Query().Rows.Single(row => row.Id == 1).Mutate();
                                change.Name = "new";
                                database.Update(change);
                            }
                        }
                        else
                        {
                            database.Provider.DatabaseAccess.ExecuteNonQuery(route == "text-key"
                                ? "UPDATE publication_text_rows SET name = 'new' WHERE id = 'one'"
                                : "UPDATE publication_rows SET name = 'new' WHERE id = 1");
                            if (invalidation == "clear")
                                database.Cache.Clear();
                            else if (route == "text-key")
                                database.Cache.Invalidate<CachePublicationTextRow, string>("one");
                            else
                                database.Cache.Invalidate<CachePublicationRow, int>(1);
                        }

                        IImmutableInstance? fresh = null;
                        if (warmBeforeRelease)
                        {
                            // Also cover an old load resuming after a new value is cached.
                            fresh = route == "text-key"
                                ? database.Query().TextRows.Single(row => row.Id == "one")
                                : database.Query().Rows.Single(row => row.Id == 1);
                            await Assert.That(fresh["Name"]).IsEqualTo("new");
                        }
                        gate.Release();
                        _ = await read.WaitAsync(TimeSpan.FromSeconds(20));
                        IImmutableInstance subsequent = route == "text-key"
                            ? database.Query().TextRows.Single(row => row.Id == "one")
                            : database.Query().Rows.Single(row => row.Id == 1);
                        await Assert.That(subsequent["Name"]).IsEqualTo("new");
                        if (fresh is not null)
                            await Assert.That(subsequent).IsSameReferenceAs(fresh);
                    }
                    finally
                    {
                        gate.Release();
                        await read.WaitAsync(TimeSpan.FromSeconds(20));
                    }
                }
    }
}

internal sealed class CachePublicationGate : IDisposable
{
    private static readonly ConcurrentDictionary<string, CachePublicationGate> Gates = new();
    private readonly string providerId;
    private readonly ManualResetEventSlim blocked = new();
    private readonly ManualResetEventSlim released = new();
    private int armed = 1;

    private CachePublicationGate(string providerId) => this.providerId = providerId;

    internal static CachePublicationGate Install(string providerId)
    {
        var gate = new CachePublicationGate(providerId);
        if (!Gates.TryAdd(providerId, gate))
            throw new InvalidOperationException("A gate already owns this isolated provider.");
        return gate;
    }

    internal static void OnConstruction(IDataSourceAccess source)
    {
        if (!Gates.TryGetValue(source.Provider.TelemetryInstanceId, out var gate) || Interlocked.Exchange(ref gate.armed, 0) != 1)
            return;
        gate.blocked.Set();
        if (!gate.released.Wait(TimeSpan.FromSeconds(20)))
            throw new TimeoutException("The publication test did not release its model constructor.");
    }

    internal void WaitUntilBlocked()
    {
        if (!blocked.Wait(TimeSpan.FromSeconds(20)))
            throw new TimeoutException("The read did not reach model construction.");
    }

    internal void Release() => released.Set();
    public void Dispose()
    {
        Gates.TryRemove(providerId, out _);
        blocked.Dispose();
        released.Dispose();
    }
}

[Database("cache_publication"), UseCache]
public sealed partial class CachePublicationDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<CachePublicationRow> Rows { get; } = new(source);
    public DbRead<CachePublicationTextRow> TextRows { get; } = new(source);
}

[Table("publication_rows")]
public abstract partial class CachePublicationRow : Immutable<CachePublicationRow, CachePublicationDb>, ITableModel<CachePublicationDb>
{
    protected CachePublicationRow(IRowData rowData, IDataSourceAccess source) : base(rowData, source) => CachePublicationGate.OnConstruction(source);
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [Column("name"), Type(DatabaseType.MySQL, "varchar", 40), Type(DatabaseType.MariaDB, "varchar", 40)]
    public abstract string Name { get; }
}

[Table("publication_text_rows")]
public abstract partial class CachePublicationTextRow : Immutable<CachePublicationTextRow, CachePublicationDb>, ITableModel<CachePublicationDb>
{
    protected CachePublicationTextRow(IRowData rowData, IDataSourceAccess source) : base(rowData, source) => CachePublicationGate.OnConstruction(source);
    [PrimaryKey, Column("id"), Type(DatabaseType.MySQL, "varchar", 40), Type(DatabaseType.MariaDB, "varchar", 40)]
    public abstract string Id { get; }
    [Column("name"), Type(DatabaseType.MySQL, "varchar", 40), Type(DatabaseType.MariaDB, "varchar", 40)]
    public abstract string Name { get; }
}
