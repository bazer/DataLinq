using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Validation;

namespace DataLinq.Tests.Unit.Core;

public partial class SchemaComparerTests
{
    [Test]
    [Arguments("årows", "Årows")]
    [Arguments("σrows", "Σrows")]
    [Arguments("𐐨rows", "𐐀rows")]
    public async Task RuntimeSQLite_NonAsciiIdentitiesRemainDistinct(string lower, string upper)
    {
        var rules = SchemaIdentifierRules.SQLite;
        foreach (var comparer in new[] { rules.DatabaseNames, rules.TableNames, rules.ColumnNames, rules.IndexNames, rules.ConstraintNames })
        {
            await Assert.That(comparer.Equals(lower, upper)).IsFalse();
            await Assert.That(comparer.Compare(lower, upper) == 0).IsFalse();
            await Assert.That(comparer.Equals(lower, lower.Replace("rows", "ROWS"))).IsTrue();
            await Assert.That(comparer.GetHashCode(lower)).IsEqualTo(comparer.GetHashCode(lower.Replace("rows", "ROWS")));
        }
        var model = RuntimeNamedTables(lower, upper);
        await Assert.That(RuntimeCompare(model, model, rules)).IsEmpty();
        var selected = SchemaComparisonScope.Capture(model, DatabaseType.SQLite, [lower, lower.Replace("rows", "ROWS")]).Resolve(model, rules);
        await Assert.That(selected.Model.Single().Table.DbName).IsEqualTo(lower);
        await Assert.That(selected.Database.Single().Table.DbName).IsEqualTo(lower);
    }

    [Test]
    public async Task RuntimeSQLite_AsciiCollisionsAreStillRejected()
    {
        var model = RuntimeNamedTables("årows", "åROWS");
        await Assert.That(() => RuntimeCompare(model, model, SchemaIdentifierRules.SQLite)).Throws<ArgumentException>();
        await Assert.That(() => SchemaComparisonScope.Capture(model, DatabaseType.SQLite, ["årows"])).Throws<ArgumentException>();
        var scope = SchemaComparisonScope.Capture(RuntimeNamedTables("årows"), DatabaseType.SQLite, null);
        await Assert.That(() => scope.Resolve(model, SchemaIdentifierRules.SQLite)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RuntimeSQLite_NonAsciiColumnAndIndexNamesDoNotCollide()
    {
        var model = CreateDatabase(CreateTable("rows", [
            CreateColumn("id", typeof(int), false, primaryKey: true),
            CreateColumn("åvalue", typeof(int), false) with { PropertyName = "First" },
            CreateColumn("Åvalue", typeof(int), false) with { PropertyName = "Second" }], [
            new IndexAttribute("åindex", IndexCharacteristic.Simple, IndexType.BTREE, "åvalue"),
            new IndexAttribute("Åindex", IndexCharacteristic.Simple, IndexType.BTREE, "Åvalue")]));
        await Assert.That(RuntimeCompare(model, model, SchemaIdentifierRules.SQLite)).IsEmpty();
    }

    [Test]
    public async Task RuntimeSQLite_IndexRenameAndForeignKeyTargetCaseChangesAreDrift()
    {
        var model = RuntimeRelations("åparent", "child", "id", "parent_id", "åindex", "fk_parent");
        var live = RuntimeRelations("Åparent", "child", "id", "parent_id", "Åindex", "fk_parent");
        var selected = SchemaComparisonScope.Capture(model, DatabaseType.SQLite, ["CHILD"]).Resolve(live, SchemaIdentifierRules.SQLite);
        var differences = new SchemaComparer(SchemaIdentifierRules.SQLite).CompareRuntime(selected.Model, selected.Database);
        await Assert.That(differences.Select(difference => difference.Kind).ToArray()).IsEquivalentTo([
            SchemaDifferenceKind.MissingIndex, SchemaDifferenceKind.ExtraIndex,
            SchemaDifferenceKind.MissingForeignKey, SchemaDifferenceKind.ExtraForeignKey]);
    }
}
