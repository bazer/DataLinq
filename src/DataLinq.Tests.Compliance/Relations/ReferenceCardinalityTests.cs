using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class ReferenceCardinalityTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.SQLiteOnly)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.SqliteProviders))]
    public async Task NonPrimaryReference_RejectsDuplicateTargetsColdWarmAndAfterInvalidation(TestProviderDescriptor provider)
    {
        // Model metadata correctly requires a unique target. Deliberately recreate only
        // this temporary database's target table without that constraint to model schema
        // drift/corrupt reference data. Drop its empty child table first so SQLite does
        // not reject the deliberately invalid target schema before navigation can run.
        // No child is persisted or constraints disabled globally.
        using var scope = TemporaryModelTestDatabase<ReferenceCardinalityDb>.Create(provider, "reference_cardinality");
        var database = scope.Database;
        database.Provider.DatabaseAccess.ExecuteNonQuery("DROP TABLE reference_cardinality_children");
        database.Provider.DatabaseAccess.ExecuteNonQuery("DROP TABLE reference_cardinality_parents");
        database.Provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE reference_cardinality_parents (id INTEGER PRIMARY KEY, code INTEGER NOT NULL)");
        database.Provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO reference_cardinality_parents (id, code) VALUES (1, 7), (2, 7)");
        var child = new ImmutableReferenceCardinalityChild(new MutableReferenceCardinalityChild
        {
            Id = 1, RequiredCode = 7, OptionalCode = 7
        }.GetRowData(), database.Provider.ReadOnlyAccess);
        var property = database.Provider.Metadata.GetTableModel(typeof(ReferenceCardinalityChild)).Model
            .RelationProperties[nameof(ReferenceCardinalityChild.RequiredParent)];
        var cache = database.Provider.GetTableCache(property.RelationPart.GetOtherSide().ColumnIndex.Table);
        var holder = new ImmutableForeignKey<ReferenceCardinalityParent, int>(7, database.Provider.ReadOnlyAccess, property);
        for (var pass = 0; pass < 2; pass++)
        {
            await Assert.That(() => holder.Value).Throws<InvalidOperationException>();
            await Assert.That(() => child.RequiredParent).Throws<InvalidOperationException>();
            await Assert.That(() => child.OptionalParent).Throws<InvalidOperationException>();
        }

        database.Provider.DatabaseAccess.ExecuteNonQuery("DELETE FROM reference_cardinality_parents WHERE id = 2");
        cache.ClearCache();
        await Assert.That(child.RequiredParent.Id).IsEqualTo(1);
        await Assert.That(child.OptionalParent).IsSameReferenceAs(child.RequiredParent);
        database.Provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO reference_cardinality_parents (id, code) VALUES (2, 7)");
        cache.ClearCache();
        await Assert.That(() => child.RequiredParent).Throws<InvalidOperationException>();
        await Assert.That(() => child.OptionalParent).Throws<InvalidOperationException>();
    }
}

[Database("reference_cardinality"), UseCache, IndexCache(IndexCacheType.All)]
public sealed partial class ReferenceCardinalityDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<ReferenceCardinalityParent> Parents { get; } = new(source);
    public DbRead<ReferenceCardinalityChild> Children { get; } = new(source);
}

[Table("reference_cardinality_parents"), Index("UX_reference_code", IndexCharacteristic.Unique, "code")]
public abstract partial class ReferenceCardinalityParent(IRowData row, IDataSourceAccess source)
    : Immutable<ReferenceCardinalityParent, ReferenceCardinalityDb>(row, source), ITableModel<ReferenceCardinalityDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [Column("code")]
    public abstract int Code { get; }
    [Relation("reference_cardinality_children", "required_code", "FK_cardinality_required")]
    public abstract IImmutableRelation<ReferenceCardinalityChild> RequiredChildren { get; }
    [Relation("reference_cardinality_children", "optional_code", "FK_cardinality_optional")]
    public abstract IImmutableRelation<ReferenceCardinalityChild> OptionalChildren { get; }
}

[Table("reference_cardinality_children")]
public abstract partial class ReferenceCardinalityChild(IRowData row, IDataSourceAccess source)
    : Immutable<ReferenceCardinalityChild, ReferenceCardinalityDb>(row, source), ITableModel<ReferenceCardinalityDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [ForeignKey("reference_cardinality_parents", "code", "FK_cardinality_required"), Column("required_code")]
    public abstract int RequiredCode { get; }
    [ForeignKey("reference_cardinality_parents", "code", "FK_cardinality_optional"), Column("optional_code")]
    public abstract int OptionalCode { get; }
    [Relation("reference_cardinality_parents", "code", "FK_cardinality_required")]
    public abstract ReferenceCardinalityParent RequiredParent { get; }
    [Relation("reference_cardinality_parents", "code", "FK_cardinality_optional")]
    public abstract ReferenceCardinalityParent? OptionalParent { get; }
}
