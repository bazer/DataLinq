using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class Int64TypedIdKeyBoundaryTests
{
    private const long FirstParentId = 5_000_000_101L;
    private const long SecondParentId = 5_000_000_102L;
    private const long FirstChildId = 6_000_000_201L;
    private const long SecondChildId = 6_000_000_202L;
    private const long ThirdChildId = 6_000_000_203L;

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Int64TypedIdRelation_ColdLoadWarmsCanonicalIndexAcrossProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<Int64TypedIdKeyDb>.Create(
            provider,
            nameof(Int64TypedIdRelation_ColdLoadWarmsCanonicalIndexAcrossProviders));

        Seed(databaseScope.Database);
        var database = databaseScope.Database;
        var parentTable = database.Provider.Metadata
            .GetTableModel(typeof(Int64TypedIdKeyParent))
            .Table;
        var childTable = database.Provider.Metadata
            .GetTableModel(typeof(Int64TypedIdKeyChild))
            .Table;
        var relation = parentTable.Model.RelationProperties[nameof(Int64TypedIdKeyParent.Children)];
        var relationIndex = relation.RelationPart.GetOtherSide().ColumnIndex;
        var childCache = database.Provider.GetTableCache(childTable);

        database.Provider.State.ClearCache();
        var parentId = new Int64BoundaryTypedId(FirstParentId);
        var parent = database.Query().Parents.Single(candidate => candidate.Id == parentId);
        var canonicalParentKey = parent.PrimaryKeys();

        DataLinqMetrics.Reset();
        try
        {
            var coldRows = parent.Children.ToArray();
            var coldSnapshot = DataLinqMetrics.Snapshot();

            var warmRows = childCache
                .GetRows(canonicalParentKey, relation, database.Provider.ReadOnlyAccess)
                .Cast<Int64TypedIdKeyChild>()
                .ToArray();
            var warmSnapshot = DataLinqMetrics.Snapshot();
            var relatedParent = coldRows[0].Parent;
            var referenceSnapshot = DataLinqMetrics.Snapshot();

            await Assert.That(canonicalParentKey.GetValue(0)).IsTypeOf<long>();
            await Assert.That(coldRows.Select(static child => child.Id).ToArray())
                .IsEquivalentTo(new[]
                {
                    new Int64BoundaryTypedId(FirstChildId),
                    new Int64BoundaryTypedId(SecondChildId)
                });
            await Assert.That(coldRows.Select(static child => child.ParentId).ToArray())
                .IsEquivalentTo(new[]
                {
                    new Int64BoundaryTypedId(FirstParentId),
                    new Int64BoundaryTypedId(FirstParentId)
                });
            await Assert.That(warmRows.Length).IsEqualTo(coldRows.Length);
            foreach (var coldRow in coldRows)
            {
                var warmRow = warmRows.Single(candidate => candidate.Id == coldRow.Id);
                await Assert.That(warmRow).IsSameReferenceAs(coldRow);
                await Assert.That(coldRow.PrimaryKeys().GetValue(0)).IsTypeOf<long>();
            }

            await Assert.That(childCache.IndicesCount
                .Single(item => item.index == relationIndex.Name)
                .count).IsEqualTo(1);

            await Assert.That(coldSnapshot.Commands.ReaderExecutions).IsEqualTo(1);
            await Assert.That(coldSnapshot.RowCache.Hits).IsEqualTo(0);
            await Assert.That(coldSnapshot.RowCache.Misses).IsEqualTo(coldRows.Length);
            await Assert.That(coldSnapshot.RowCache.Stores).IsEqualTo(coldRows.Length);
            await Assert.That(coldSnapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(coldRows.Length);
            await Assert.That(coldSnapshot.RowCache.Materializations).IsEqualTo(coldRows.Length);
            await Assert.That(coldSnapshot.Relations.CollectionLoads).IsEqualTo(1);

            await Assert.That(warmSnapshot.Commands.ReaderExecutions).IsEqualTo(1);
            await Assert.That(warmSnapshot.RowCache.Hits).IsEqualTo(coldRows.Length);
            await Assert.That(warmSnapshot.RowCache.Misses).IsEqualTo(coldRows.Length);
            await Assert.That(warmSnapshot.RowCache.Stores).IsEqualTo(coldRows.Length);
            await Assert.That(warmSnapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(coldRows.Length);
            await Assert.That(warmSnapshot.RowCache.Materializations).IsEqualTo(coldRows.Length);

            await Assert.That(relatedParent).IsSameReferenceAs(parent);
            await Assert.That(relatedParent.Id).IsEqualTo(parentId);
            await Assert.That(referenceSnapshot.Commands.ReaderExecutions).IsEqualTo(1);
            await Assert.That(referenceSnapshot.Relations.ReferenceLoads).IsEqualTo(1);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Int64TypedIdJoinedRowLocal_HydratesCanonicalPrimaryKeysAcrossProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<Int64TypedIdKeyDb>.Create(
            provider,
            nameof(Int64TypedIdJoinedRowLocal_HydratesCanonicalPrimaryKeysAcrossProviders));

        Seed(databaseScope.Database);
        var database = databaseScope.Database;
        var query = database.Query().Children.Join(
            database.Query().Parents,
            child => child.ParentId,
            parent => parent.Id,
            (child, parent) => new object?[]
            {
                child,
                parent,
                child.Id,
                child.ParentId,
                parent.Id,
                child.Name,
                parent.Name
            });
        var invocation = ExpressionQueryPlanParser.Convert(database, query);
        var projection = invocation.Template.Projection as QueryPlanProjection.JoinedRowLocal;
        var joinedSources = invocation.Template.Sources
            .Where(static source => source.Kind is QueryPlanSourceKind.RootTable or QueryPlanSourceKind.ExplicitJoin)
            .ToArray();

        database.Provider.State.ClearCache();
        var rows = query
            .ToList()
            .OrderBy(static row => ((Int64TypedIdKeyChild)row[0]!).Id.Value)
            .ToArray();

        var firstChild = (Int64TypedIdKeyChild)rows[0][0]!;
        var secondChild = (Int64TypedIdKeyChild)rows[1][0]!;
        var firstParent = (Int64TypedIdKeyParent)rows[0][1]!;
        var repeatedParent = (Int64TypedIdKeyParent)rows[1][1]!;
        var parentTable = database.Provider.Metadata.GetTableModel(typeof(Int64TypedIdKeyParent)).Table;
        var childTable = database.Provider.Metadata.GetTableModel(typeof(Int64TypedIdKeyChild)).Table;
        var parentCache = database.Provider.GetTableCache(parentTable);
        var childCache = database.Provider.GetTableCache(childTable);

        await Assert.That(projection).IsNotNull();
        await Assert.That(projection!.Disposition)
            .IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
        await Assert.That(joinedSources.Length).IsEqualTo(2);
        await Assert.That(joinedSources.All(static source =>
            source.Table.PrimaryKeyColumns.Count == 1 &&
            source.Table.PrimaryKeyColumns[0].HasScalarConverter &&
            source.Table.PrimaryKeyColumns[0].ProviderClrType == typeof(long)))
            .IsTrue();

        await Assert.That(rows.Length).IsEqualTo(3);
        await Assert.That(firstChild.Id).IsEqualTo(new Int64BoundaryTypedId(FirstChildId));
        await Assert.That(secondChild.Id).IsEqualTo(new Int64BoundaryTypedId(SecondChildId));
        await Assert.That(firstParent.Id).IsEqualTo(new Int64BoundaryTypedId(FirstParentId));
        await Assert.That(repeatedParent).IsSameReferenceAs(firstParent);
        await Assert.That(rows.Select(static row => row[2]).All(static value => value is Int64BoundaryTypedId))
            .IsTrue();
        await Assert.That(rows.Select(static row => row[3]).All(static value => value is Int64BoundaryTypedId))
            .IsTrue();
        await Assert.That(rows.Select(static row => row[4]).All(static value => value is Int64BoundaryTypedId))
            .IsTrue();
        await Assert.That(rows.Select(static row => ((Int64TypedIdKeyChild)row[0]!).PrimaryKeys().GetValue(0))
            .All(static value => value is long)).IsTrue();
        await Assert.That(rows.Select(static row => ((Int64TypedIdKeyParent)row[1]!).PrimaryKeys().GetValue(0))
            .All(static value => value is long)).IsTrue();

        await Assert.That(parentCache.GetRow(
            DataLinqKey.FromValue(FirstParentId),
            database.Provider.ReadOnlyAccess)).IsSameReferenceAs(firstParent);
        await Assert.That(childCache.GetRow(
            DataLinqKey.FromValue(FirstChildId),
            database.Provider.ReadOnlyAccess)).IsSameReferenceAs(firstChild);
        await Assert.That(childCache.GetRow(
            DataLinqKey.FromValue(SecondChildId),
            database.Provider.ReadOnlyAccess)).IsSameReferenceAs(secondChild);
    }

    private static void Seed(Database<Int64TypedIdKeyDb> database)
    {
        _ = database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO int64_typed_id_parents (id, name) VALUES " +
            "(5000000101, 'parent-a'), (5000000102, 'parent-b')");
        _ = database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO int64_typed_id_children (id, parent_id, name) VALUES " +
            "(6000000201, 5000000101, 'child-a'), " +
            "(6000000202, 5000000101, 'child-b'), " +
            "(6000000203, 5000000102, 'child-c')");
    }
}

public readonly record struct Int64BoundaryTypedId(long Value);

public sealed class Int64BoundaryTypedIdConverter
    : DataLinqScalarConverter<Int64BoundaryTypedId, long>
{
    public override long ToProvider(
        Int64BoundaryTypedId modelValue,
        in ScalarConversionContext context) =>
        modelValue.Value;

    public override Int64BoundaryTypedId FromProvider(
        long providerValue,
        in ScalarConversionContext context) =>
        new(providerValue);
}

[UseCache]
[Database("int64typedidkeyboundaries")]
public sealed partial class Int64TypedIdKeyDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<Int64TypedIdKeyParent> Parents { get; } = new(dataSource);
    public DbRead<Int64TypedIdKeyChild> Children { get; } = new(dataSource);
}

[Table("int64_typed_id_parents")]
public abstract partial class Int64TypedIdKeyParent(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<Int64TypedIdKeyParent, Int64TypedIdKeyDb>(rowData, dataSource),
      ITableModel<Int64TypedIdKeyDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "bigint", 20)]
    [Type(DatabaseType.MariaDB, "bigint", 20)]
    [ScalarConverter(typeof(Int64BoundaryTypedIdConverter))]
    [Column("id")]
    public abstract Int64BoundaryTypedId Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.MariaDB, "varchar", 40)]
    [Column("name")]
    public abstract string Name { get; }

    [Relation("int64_typed_id_children", "parent_id", "FK_int64_typed_id_child_parent")]
    public abstract IImmutableRelation<Int64TypedIdKeyChild> Children { get; }
}

[Table("int64_typed_id_children")]
public abstract partial class Int64TypedIdKeyChild(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<Int64TypedIdKeyChild, Int64TypedIdKeyDb>(rowData, dataSource),
      ITableModel<Int64TypedIdKeyDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "bigint", 20)]
    [Type(DatabaseType.MariaDB, "bigint", 20)]
    [ScalarConverter(typeof(Int64BoundaryTypedIdConverter))]
    [Column("id")]
    public abstract Int64BoundaryTypedId Id { get; }

    [ForeignKey("int64_typed_id_parents", "id", "FK_int64_typed_id_child_parent")]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "bigint", 20)]
    [Type(DatabaseType.MariaDB, "bigint", 20)]
    [ScalarConverter(typeof(Int64BoundaryTypedIdConverter))]
    [Column("parent_id")]
    public abstract Int64BoundaryTypedId ParentId { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.MariaDB, "varchar", 40)]
    [Column("name")]
    public abstract string Name { get; }

    [Relation("int64_typed_id_parents", "id", "FK_int64_typed_id_child_parent")]
    public abstract Int64TypedIdKeyParent Parent { get; }
}
