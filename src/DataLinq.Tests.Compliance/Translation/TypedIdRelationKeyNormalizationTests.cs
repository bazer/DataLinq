using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class TypedIdRelationKeyNormalizationTests
{
    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task IntegralTypedIdRelation_ColdLoadPopulatesWarmCanonicalKeyPathAcrossProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<TypedIdRelationKeyDb>.Create(
            provider,
            nameof(IntegralTypedIdRelation_ColdLoadPopulatesWarmCanonicalKeyPathAcrossProviders));

        var insertedParents = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO typedidrelationkeyparents (id, name) VALUES " +
            "(101, 'parent-a'), (102, 'parent-b')");
        var insertedChildren = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO typedidrelationkeychildren (id, parent_id, name) VALUES " +
            "(201, 101, 'child-a'), (202, 101, 'child-b'), (203, 102, 'child-c')");
        var database = databaseScope.Database;
        var parentTable = database.Provider.Metadata
            .GetTableModel(typeof(TypedIdRelationKeyParent))
            .Table;
        var childTable = database.Provider.Metadata
            .GetTableModel(typeof(TypedIdRelationKeyChild))
            .Table;
        var relation = parentTable.Model.RelationProperties[nameof(TypedIdRelationKeyParent.Children)];
        var relationIndex = relation.RelationPart.GetOtherSide().ColumnIndex;
        var childCache = database.Provider.GetTableCache(childTable);

        database.Provider.State.ClearCache();
        var parentId = new QueryTypedId(101);
        var parent = database.Query().Parents.Single(parent => parent.Id == parentId);
        var canonicalParentKey = parent.PrimaryKeys();

        DataLinqMetrics.Reset();
        try
        {
            var coldRows = parent.Children.ToArray();
            var coldSnapshot = DataLinqMetrics.Snapshot();

            var warmRows = childCache
                .GetRows(canonicalParentKey, relation, database.Provider.ReadOnlyAccess)
                .Cast<TypedIdRelationKeyChild>()
                .ToArray();
            var warmSnapshot = DataLinqMetrics.Snapshot();
            await Assert.That(coldRows.Length).IsEqualTo(2);
            var relatedParent = coldRows[0].Parent;
            var referenceSnapshot = DataLinqMetrics.Snapshot();

            await Assert.That(insertedParents).IsEqualTo(2);
            await Assert.That(insertedChildren).IsEqualTo(3);
            await Assert.That(parent.Id).IsEqualTo(new QueryTypedId(101));
            await Assert.That((object)parent.Id).IsTypeOf<QueryTypedId>();
            await Assert.That(canonicalParentKey.GetValue(0)).IsTypeOf<int>();
            await Assert.That(coldRows.Select(static child => child.Id).ToArray())
                .IsEquivalentTo(new[] { new QueryTypedId(201), new QueryTypedId(202) });
            await Assert.That(coldRows.Select(static child => child.ParentId).ToArray())
                .IsEquivalentTo(new[] { new QueryTypedId(101), new QueryTypedId(101) });
            await Assert.That(coldRows.Select(static child => (object)child.Id).All(static id => id is QueryTypedId))
                .IsTrue();
            await Assert.That(coldRows.Select(static child => (object)child.ParentId).All(static id => id is QueryTypedId))
                .IsTrue();
            await Assert.That(warmRows.Select(static child => child.Id).ToArray())
                .IsEquivalentTo(coldRows.Select(static child => child.Id).ToArray());
            await Assert.That(warmRows.Length).IsEqualTo(coldRows.Length);
            foreach (var coldRow in coldRows)
            {
                var warmRow = warmRows.Single(candidate => candidate.Id == coldRow.Id);
                await Assert.That(warmRow).IsSameReferenceAs(coldRow);
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
            await Assert.That(relatedParent.Id).IsEqualTo(new QueryTypedId(101));
            await Assert.That(referenceSnapshot.Commands.ReaderExecutions).IsEqualTo(1);
            await Assert.That(referenceSnapshot.Relations.ReferenceLoads).IsEqualTo(1);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }
}

[UseCache]
[Database("typedidrelationkeys")]
public sealed partial class TypedIdRelationKeyDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<TypedIdRelationKeyParent> Parents { get; } = new(dataSource);
    public DbRead<TypedIdRelationKeyChild> Children { get; } = new(dataSource);
}

[Table("typedidrelationkeyparents")]
public abstract partial class TypedIdRelationKeyParent(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<TypedIdRelationKeyParent, TypedIdRelationKeyDb>(rowData, dataSource),
      ITableModel<TypedIdRelationKeyDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(QueryTypedIdConverter))]
    [Column("id")]
    public abstract QueryTypedId Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.MariaDB, "varchar", 40)]
    [Column("name")]
    public abstract string Name { get; }

    [Relation("typedidrelationkeychildren", "parent_id", "FK_typedidrelationkey_child_parent")]
    public abstract IImmutableRelation<TypedIdRelationKeyChild> Children { get; }
}

[Table("typedidrelationkeychildren")]
public abstract partial class TypedIdRelationKeyChild(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<TypedIdRelationKeyChild, TypedIdRelationKeyDb>(rowData, dataSource),
      ITableModel<TypedIdRelationKeyDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(QueryTypedIdConverter))]
    [Column("id")]
    public abstract QueryTypedId Id { get; }

    [ForeignKey("typedidrelationkeyparents", "id", "FK_typedidrelationkey_child_parent")]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(QueryTypedIdConverter))]
    [Column("parent_id")]
    public abstract QueryTypedId ParentId { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.MariaDB, "varchar", 40)]
    [Column("name")]
    public abstract string Name { get; }

    [Relation("typedidrelationkeyparents", "id", "FK_typedidrelationkey_child_parent")]
    public abstract TypedIdRelationKeyParent Parent { get; }
}
