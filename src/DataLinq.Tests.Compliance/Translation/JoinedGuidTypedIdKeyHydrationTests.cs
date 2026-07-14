using System;
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

public sealed class JoinedGuidTypedIdKeyHydrationTests
{
    private static readonly Guid ParentGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid FirstChildGuid = Guid.Parse("01234567-89ab-cdef-1032-547698badcfe");
    private static readonly Guid SecondChildGuid = Guid.Parse("fedcba98-7654-3210-89ab-cdef01234567");

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task JoinedRowLocal_RawGuidTypedIdsHydrateCanonicalKeysAcrossProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<JoinedGuidTypedIdDb>.Create(
            provider,
            nameof(JoinedRowLocal_RawGuidTypedIdsHydrateCanonicalKeysAcrossProviders));
        var database = databaseScope.Database;
        var seed = SeedRawRows(database, provider.DatabaseType);

        await Assert.That(seed.InsertedParent).IsEqualTo(1);
        await Assert.That(seed.InsertedChildren).IsEqualTo(2);
        await Assert.That(database.Provider.DatabaseAccess.ExecuteScalar<string>(
                "SELECT HEX(id) FROM joined_guid_typed_id_parents WHERE name = 'parent'"))
            .IsEqualTo(seed.ParentHex);
        await Assert.That(database.Provider.DatabaseAccess.ExecuteScalar<string>(
                "SELECT HEX(id) FROM joined_guid_typed_id_children WHERE name = 'child-a'"))
            .IsEqualTo(seed.FirstChildHex);
        await Assert.That(database.Provider.DatabaseAccess.ExecuteScalar<string>(
                "SELECT HEX(id) FROM joined_guid_typed_id_children WHERE name = 'child-b'"))
            .IsEqualTo(seed.SecondChildHex);

        var query = database.Query().Children.Join(
            database.Query().Parents,
            child => child.ParentId!.Value,
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
        var expectedFormat = provider.DatabaseType == DatabaseType.MySQL
            ? GuidStorageFormat.Binary16LittleEndian
            : GuidStorageFormat.Binary16Rfc4122;

        JoinedGuidTypedIdConverter.Reset();
        try
        {
            database.Provider.State.ClearCache();
            var coldRows = query
                .ToList()
                .OrderBy(static row => ((JoinedGuidTypedIdChild)row[0]!).Name)
                .ToArray();
            var coldToProviderCalls = JoinedGuidTypedIdConverter.ToProviderCalls;
            var coldFromProviderCalls = JoinedGuidTypedIdConverter.FromProviderCalls;
            var warmRows = query
                .ToList()
                .OrderBy(static row => ((JoinedGuidTypedIdChild)row[0]!).Name)
                .ToArray();
            var warmToProviderCalls = JoinedGuidTypedIdConverter.ToProviderCalls;
            var warmFromProviderCalls = JoinedGuidTypedIdConverter.FromProviderCalls;

            var firstChild = (JoinedGuidTypedIdChild)coldRows[0][0]!;
            var secondChild = (JoinedGuidTypedIdChild)coldRows[1][0]!;
            var parent = (JoinedGuidTypedIdParent)coldRows[0][1]!;
            var repeatedParent = (JoinedGuidTypedIdParent)coldRows[1][1]!;
            var parentTable = database.Provider.Metadata
                .GetTableModel(typeof(JoinedGuidTypedIdParent))
                .Table;
            var childTable = database.Provider.Metadata
                .GetTableModel(typeof(JoinedGuidTypedIdChild))
                .Table;
            var parentCache = database.Provider.GetTableCache(parentTable);
            var childCache = database.Provider.GetTableCache(childTable);

            await Assert.That(projection).IsNotNull();
            await Assert.That(projection!.Disposition)
                .IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
            await Assert.That(joinedSources.Length).IsEqualTo(2);
            await Assert.That(joinedSources.All(source =>
                source.Table.PrimaryKeyColumns.Count == 1 &&
                source.Table.PrimaryKeyColumns[0].HasScalarConverter &&
                source.Table.PrimaryKeyColumns[0].ProviderClrType == typeof(Guid) &&
                source.Table.PrimaryKeyColumns[0].GetGuidStorageFor(provider.DatabaseType)?.Format == expectedFormat))
                .IsTrue();

            await Assert.That(coldRows.Length).IsEqualTo(2);
            await Assert.That(firstChild.Id).IsEqualTo(new JoinedGuidTypedId(FirstChildGuid));
            await Assert.That(secondChild.Id).IsEqualTo(new JoinedGuidTypedId(SecondChildGuid));
            await Assert.That(firstChild.ParentId).IsEqualTo(new JoinedGuidTypedId(ParentGuid));
            await Assert.That(secondChild.ParentId).IsEqualTo(new JoinedGuidTypedId(ParentGuid));
            await Assert.That(parent.Id).IsEqualTo(new JoinedGuidTypedId(ParentGuid));
            await Assert.That(repeatedParent).IsSameReferenceAs(parent);

            await Assert.That(coldRows.Select(static row => row[2]).All(static value => value is JoinedGuidTypedId))
                .IsTrue();
            await Assert.That(coldRows.Select(static row => row[3]).All(static value => value is JoinedGuidTypedId))
                .IsTrue();
            await Assert.That(coldRows.Select(static row => row[4]).All(static value => value is JoinedGuidTypedId))
                .IsTrue();
            await Assert.That(coldRows.SelectMany(static row => row).Any(static value => value is byte[]))
                .IsFalse();

            await Assert.That(firstChild.PrimaryKeys().GetValue(0)).IsEqualTo(FirstChildGuid);
            await Assert.That(firstChild.PrimaryKeys().GetValue(0)).IsTypeOf<Guid>();
            await Assert.That(secondChild.PrimaryKeys().GetValue(0)).IsEqualTo(SecondChildGuid);
            await Assert.That(secondChild.PrimaryKeys().GetValue(0)).IsTypeOf<Guid>();
            await Assert.That(parent.PrimaryKeys().GetValue(0)).IsEqualTo(ParentGuid);
            await Assert.That(parent.PrimaryKeys().GetValue(0)).IsTypeOf<Guid>();

            await Assert.That(parentCache.GetRow(
                DataLinqKey.FromValue(ParentGuid),
                database.Provider.ReadOnlyAccess)).IsSameReferenceAs(parent);
            await Assert.That(childCache.GetRow(
                DataLinqKey.FromValue(FirstChildGuid),
                database.Provider.ReadOnlyAccess)).IsSameReferenceAs(firstChild);
            await Assert.That(childCache.GetRow(
                DataLinqKey.FromValue(SecondChildGuid),
                database.Provider.ReadOnlyAccess)).IsSameReferenceAs(secondChild);

            await Assert.That(warmRows[0][0]).IsSameReferenceAs(firstChild);
            await Assert.That(warmRows[1][0]).IsSameReferenceAs(secondChild);
            await Assert.That(warmRows[0][1]).IsSameReferenceAs(parent);
            await Assert.That(warmRows[1][1]).IsSameReferenceAs(parent);

            // Immutable construction intentionally captures each model primary key once.
            // These three calls are the two child keys plus the one shared parent key; any
            // converter use in joined reader-key selection would add calls before cache lookup.
            await Assert.That(coldToProviderCalls).IsEqualTo(3);
            await Assert.That(warmToProviderCalls).IsEqualTo(coldToProviderCalls);

            // Cold materialization converts two child IDs, two parent FKs, and one parent ID.
            // The warm join must reuse those entities without another model materialization.
            await Assert.That(coldFromProviderCalls).IsEqualTo(5);
            await Assert.That(warmFromProviderCalls).IsEqualTo(coldFromProviderCalls);
        }
        finally
        {
            JoinedGuidTypedIdConverter.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GuidTypedIdRelation_RawRowsRollbackThenColdLoadWarmsCanonicalIndexAcrossProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<JoinedGuidTypedIdDb>.Create(
            provider,
            nameof(GuidTypedIdRelation_RawRowsRollbackThenColdLoadWarmsCanonicalIndexAcrossProviders));
        var database = databaseScope.Database;
        var seed = SeedRawRows(database, provider.DatabaseType);
        var parentTable = database.Provider.Metadata
            .GetTableModel(typeof(JoinedGuidTypedIdParent))
            .Table;
        var childTable = database.Provider.Metadata
            .GetTableModel(typeof(JoinedGuidTypedIdChild))
            .Table;
        var relation = parentTable.Model.RelationProperties[nameof(JoinedGuidTypedIdParent.Children)];
        var relationIndex = relation.RelationPart.GetOtherSide().ColumnIndex;
        var relationColumn = relationIndex.Columns.Single();
        var expectedFormat = provider.DatabaseType == DatabaseType.MySQL
            ? GuidStorageFormat.Binary16LittleEndian
            : GuidStorageFormat.Binary16Rfc4122;
        var childCache = database.Provider.GetTableCache(childTable);

        await Assert.That(seed.InsertedParent).IsEqualTo(1);
        await Assert.That(seed.InsertedChildren).IsEqualTo(2);
        await Assert.That(database.Provider.DatabaseAccess.ExecuteScalar<string>(
                "SELECT HEX(id) FROM joined_guid_typed_id_parents WHERE name = 'parent'"))
            .IsEqualTo(seed.ParentHex);
        await Assert.That(database.Provider.DatabaseAccess.ExecuteScalar<string>(
                "SELECT HEX(parent_id) FROM joined_guid_typed_id_children WHERE name = 'child-a'"))
            .IsEqualTo(seed.ParentHex);
        await Assert.That(
            relationColumn.HasScalarConverter &&
            relationColumn.ProviderClrType == typeof(Guid) &&
            relationColumn.GetGuidStorageFor(provider.DatabaseType)?.Format == expectedFormat)
            .IsTrue();

        database.Provider.State.ClearCache();
        using (var transaction = database.Transaction())
        {
            var deletedChild = transaction.Query().Children
                .Single(child => child.Name == "child-a");
            transaction.Delete(deletedChild);

            var transactionParent = transaction.Query().Parents
                .Single(parent => parent.Name == "parent");
            await Assert.That(transactionParent.Children.Select(static child => child.Name).ToArray())
                .IsEquivalentTo(["child-b"]);

            transaction.Rollback();
        }

        await Assert.That(childCache.IndicesCount
            .Single(item => item.index == relationIndex.Name)
            .count).IsEqualTo(0);

        var committedParent = database.Query().Parents
            .Single(parent => parent.Name == "parent");
        var canonicalParentKey = committedParent.PrimaryKeys();

        JoinedGuidTypedIdConverter.Reset();
        DataLinqMetrics.Reset();
        try
        {
            var coldRows = committedParent.Children
                .OrderBy(static child => child.Name)
                .ToArray();
            var coldSnapshot = DataLinqMetrics.Snapshot();
            var coldToProviderCalls = JoinedGuidTypedIdConverter.ToProviderCalls;
            var coldFromProviderCalls = JoinedGuidTypedIdConverter.FromProviderCalls;

            var warmRows = childCache
                .GetRows(canonicalParentKey, relation, database.Provider.ReadOnlyAccess)
                .Cast<JoinedGuidTypedIdChild>()
                .OrderBy(static child => child.Name)
                .ToArray();
            var warmSnapshot = DataLinqMetrics.Snapshot();
            var warmToProviderCalls = JoinedGuidTypedIdConverter.ToProviderCalls;
            var warmFromProviderCalls = JoinedGuidTypedIdConverter.FromProviderCalls;

            var relatedParents = coldRows.Select(static child => child.Parent).ToArray();
            var referenceSnapshot = DataLinqMetrics.Snapshot();

            await Assert.That(canonicalParentKey.GetValue(0)).IsTypeOf<Guid>();
            await Assert.That(canonicalParentKey.GetValue(0)).IsEqualTo(ParentGuid);
            await Assert.That(coldRows.Select(static child => child.Id).ToArray())
                .IsEquivalentTo(
                [
                    new JoinedGuidTypedId(FirstChildGuid),
                    new JoinedGuidTypedId(SecondChildGuid)
                ]);
            await Assert.That(coldRows.Select(static child => child.ParentId).ToArray())
                .IsEquivalentTo(new JoinedGuidTypedId?[]
                {
                    new JoinedGuidTypedId(ParentGuid),
                    new JoinedGuidTypedId(ParentGuid)
                });
            await Assert.That(coldRows.Select(static child => child.PrimaryKeys().GetValue(0))
                .All(static value => value is Guid)).IsTrue();
            await Assert.That(warmRows.Length).IsEqualTo(coldRows.Length);
            for (var index = 0; index < coldRows.Length; index++)
                await Assert.That(warmRows[index]).IsSameReferenceAs(coldRows[index]);

            await Assert.That(childCache.IndicesCount
                .Single(item => item.index == relationIndex.Name)
                .count).IsEqualTo(1);
            await Assert.That(relatedParents.All(parent => ReferenceEquals(parent, committedParent)))
                .IsTrue();

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

            await Assert.That(referenceSnapshot.Commands.ReaderExecutions).IsEqualTo(1);
            await Assert.That(referenceSnapshot.Relations.ReferenceLoads).IsEqualTo(coldRows.Length);
            await Assert.That(coldToProviderCalls).IsEqualTo(3);
            await Assert.That(coldFromProviderCalls).IsEqualTo(4);
            await Assert.That(warmToProviderCalls).IsEqualTo(coldToProviderCalls);
            await Assert.That(warmFromProviderCalls).IsEqualTo(coldFromProviderCalls);
            await Assert.That(JoinedGuidTypedIdConverter.ToProviderCalls).IsEqualTo(5);
            await Assert.That(JoinedGuidTypedIdConverter.FromProviderCalls).IsEqualTo(4);
        }
        finally
        {
            DataLinqMetrics.Reset();
            JoinedGuidTypedIdConverter.Reset();
        }
    }

    private static RawGuidSeed SeedRawRows(
        Database<JoinedGuidTypedIdDb> database,
        DatabaseType databaseType)
    {
        var parentHex = PhysicalHex(
            databaseType,
            littleEndian: "33221100554477668899AABBCCDDEEFF",
            rfc4122: "00112233445566778899AABBCCDDEEFF");
        var firstChildHex = PhysicalHex(
            databaseType,
            littleEndian: "67452301AB89EFCD1032547698BADCFE",
            rfc4122: "0123456789ABCDEF1032547698BADCFE");
        var secondChildHex = PhysicalHex(
            databaseType,
            littleEndian: "98BADCFE5476103289ABCDEF01234567",
            rfc4122: "FEDCBA987654321089ABCDEF01234567");

        var insertedParent = database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO joined_guid_typed_id_parents (id, name) VALUES " +
            $"(X'{parentHex}', 'parent')");
        var insertedChildren = database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO joined_guid_typed_id_children (id, parent_id, name) VALUES " +
            $"(X'{firstChildHex}', X'{parentHex}', 'child-a'), " +
            $"(X'{secondChildHex}', X'{parentHex}', 'child-b')");

        return new RawGuidSeed(
            insertedParent,
            insertedChildren,
            parentHex,
            firstChildHex,
            secondChildHex);
    }

    private static string PhysicalHex(
        DatabaseType databaseType,
        string littleEndian,
        string rfc4122) => databaseType switch
    {
        DatabaseType.MySQL => littleEndian,
        DatabaseType.SQLite or DatabaseType.MariaDB => rfc4122,
        _ => throw new ArgumentOutOfRangeException(
            nameof(databaseType),
            databaseType,
            "The joined typed UUID key test only supports SQLite, MySQL, and MariaDB.")
    };

    private sealed record RawGuidSeed(
        int InsertedParent,
        int InsertedChildren,
        string ParentHex,
        string FirstChildHex,
        string SecondChildHex);
}

public readonly record struct JoinedGuidTypedId(Guid Value);

public sealed class JoinedGuidTypedIdConverter
    : DataLinqScalarConverter<JoinedGuidTypedId, Guid>
{
    public static int ToProviderCalls { get; private set; }
    public static int FromProviderCalls { get; private set; }

    public static void Reset()
    {
        ToProviderCalls = 0;
        FromProviderCalls = 0;
    }

    public override Guid ToProvider(
        JoinedGuidTypedId modelValue,
        in ScalarConversionContext context)
    {
        ToProviderCalls++;
        return modelValue.Value;
    }

    public override JoinedGuidTypedId FromProvider(
        Guid providerValue,
        in ScalarConversionContext context)
    {
        FromProviderCalls++;
        return new JoinedGuidTypedId(providerValue);
    }
}

[UseCache]
[Database("joinedguidtypedidkeys")]
public sealed partial class JoinedGuidTypedIdDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<JoinedGuidTypedIdParent> Parents { get; } = new(dataSource);
    public DbRead<JoinedGuidTypedIdChild> Children { get; } = new(dataSource);
}

[Table("joined_guid_typed_id_parents")]
public abstract partial class JoinedGuidTypedIdParent(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<JoinedGuidTypedIdParent, JoinedGuidTypedIdDb>(rowData, dataSource),
      ITableModel<JoinedGuidTypedIdDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "BLOB")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16Rfc4122)]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16LittleEndian)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Binary16Rfc4122)]
    [ScalarConverter(typeof(JoinedGuidTypedIdConverter))]
    [Column("id")]
    public abstract JoinedGuidTypedId Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.MariaDB, "varchar", 40)]
    [Column("name")]
    public abstract string Name { get; }

    [Relation("joined_guid_typed_id_children", "parent_id", "FK_joined_guid_typed_id_child_parent")]
    public abstract IImmutableRelation<JoinedGuidTypedIdChild> Children { get; }
}

[Table("joined_guid_typed_id_children")]
public abstract partial class JoinedGuidTypedIdChild(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<JoinedGuidTypedIdChild, JoinedGuidTypedIdDb>(rowData, dataSource),
      ITableModel<JoinedGuidTypedIdDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "BLOB")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16Rfc4122)]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16LittleEndian)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Binary16Rfc4122)]
    [ScalarConverter(typeof(JoinedGuidTypedIdConverter))]
    [Column("id")]
    public abstract JoinedGuidTypedId Id { get; }

    [Nullable]
    [ForeignKey("joined_guid_typed_id_parents", "id", "FK_joined_guid_typed_id_child_parent")]
    [Type(DatabaseType.SQLite, "BLOB")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16Rfc4122)]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16LittleEndian)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Binary16Rfc4122)]
    [ScalarConverter(typeof(JoinedGuidTypedIdConverter))]
    [Column("parent_id")]
    public abstract JoinedGuidTypedId? ParentId { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.MariaDB, "varchar", 40)]
    [Column("name")]
    public abstract string Name { get; }

    [Relation("joined_guid_typed_id_parents", "id", "FK_joined_guid_typed_id_child_parent")]
    public abstract JoinedGuidTypedIdParent Parent { get; }
}
