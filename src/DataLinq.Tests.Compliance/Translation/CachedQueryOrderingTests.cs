using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class CachedQueryOrderingTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EntityHydrationPreservesDatabaseCollationAndPageOrder(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<CachedQueryOrderingDb>.Create(provider, nameof(EntityHydrationPreservesDatabaseCollationAndPageOrder));
        var database = scope.Database;
        if (provider.ServerTarget is not null)
        {
            database.Provider.DatabaseAccess.ExecuteNonQuery("ALTER TABLE ordering_scalar MODIFY name VARCHAR(40) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL");
            database.Provider.DatabaseAccess.ExecuteNonQuery("ALTER TABLE ordering_composite MODIFY name VARCHAR(40) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL");
        }

        string[] names = ["a", "B", "ä", "A", "b", "Z"];
        for (var i = 0; i < names.Length; i++)
        {
            database.Insert(new MutableOrderingScalar { Id = i + 1, Name = names[i] });
            database.Insert(new MutableOrderingComposite { Id = i + 1, Part = 1, Name = names[i] });
        }

        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            // Prove this fixture distinguishes database order from a CLR re-sort.
            var serverNames = new SqlQuery<OrderingScalar>(database.Provider.ReadOnlyAccess)
                .OrderBy("name").SelectQuery().ReadRows()
                .Select(row => (string)row.GetValue(database.Provider.Metadata.GetTableModel(typeof(OrderingScalar)).Table.GetColumnByDbName("name"))!).ToArray();
            await Assert.That(serverNames.SequenceEqual(names.OrderBy(name => name))).IsFalse();

            await Check<OrderingScalar>(database);
            await Check<OrderingComposite>(database);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static async Task Check<T>(Database<CachedQueryOrderingDb> database)
    {
        foreach (var ascending in new[] { true, false })
        foreach (var paged in new[] { false, true })
        {
            SqlQuery<T> Query()
            {
                var query = new SqlQuery<T>(database.Provider.ReadOnlyAccess).OrderBy("name", ascending: ascending).OrderBy("id");
                return paged ? query.Limit(4, 1) : query;
            }

            var expected = Query().SelectQuery().ReadKeys().ToArray();
            foreach (var cacheState in new[] { "cold", "warm", "mixed" })
            {
                database.Provider.State.ClearCache();
                if (cacheState == "warm")
                    _ = new SqlQuery<T>(database.Provider.ReadOnlyAccess).SelectQuery().Execute().ToArray();
                else if (cacheState == "mixed")
                    _ = new SqlQuery<T>(database.Provider.ReadOnlyAccess).OrderBy("id").Limit(3).SelectQuery().Execute().ToArray();

                var actual = Query().SelectQuery().Execute().Select(row => row.PrimaryKeys()).ToArray();
                await Assert.That(actual.SequenceEqual(expected)).IsTrue();
            }
        }
    }
}

[Database("cachedqueryordering")]
[UseCache]
public sealed partial class CachedQueryOrderingDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<OrderingScalar> Scalars { get; } = new(dataSource);
    public DbRead<OrderingComposite> Composites { get; } = new(dataSource);
}

[Table("ordering_scalar")]
[UseCache]
public abstract partial class OrderingScalar(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<OrderingScalar, CachedQueryOrderingDb>(rowData, dataSource), ITableModel<CachedQueryOrderingDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [Column("name"), Type(DatabaseType.MySQL, "varchar", 40), Type(DatabaseType.MariaDB, "varchar", 40)]
    public abstract string Name { get; }
}

[Table("ordering_composite")]
[UseCache]
public abstract partial class OrderingComposite(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<OrderingComposite, CachedQueryOrderingDb>(rowData, dataSource), ITableModel<CachedQueryOrderingDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [PrimaryKey, Column("part")]
    public abstract int Part { get; }
    [Column("name"), Type(DatabaseType.MySQL, "varchar", 40), Type(DatabaseType.MariaDB, "varchar", 40)]
    public abstract string Name { get; }
}
