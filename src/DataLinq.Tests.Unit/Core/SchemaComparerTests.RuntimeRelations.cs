using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Metadata;
using DataLinq.Validation;

namespace DataLinq.Tests.Unit.Core;

public partial class SchemaComparerTests
{
    [Test]
    [Arguments(DatabaseType.SQLite)]
    [Arguments(DatabaseType.MySQL)]
    [Arguments(DatabaseType.MariaDB)]
    public async Task RuntimeRelations_OmittedEndpointCannotHideObservedActionDisagreement(DatabaseType type)
    {
        var model = RelationSet(("fk", ReferentialAction.Cascade));
        var live = RelationSet();
        var rules = type == DatabaseType.SQLite ? SchemaIdentifierRules.SQLite : SchemaIdentifierRules.Server(type, 0);
        var limitations = new SchemaReadLimitations();
        limitations.Add(new("child", type == DatabaseType.SQLite ? "0" : "fk", SchemaReadAspect.ForeignKey,
            SchemaReadLimitationReason.UnrepresentedForeignKeyTarget, "Endpoint was omitted.", ["parent_id"], "parent", ["id"],
            onUpdate: ReferentialAction.Cascade, onDelete: ReferentialAction.Restrict));
        var selected = SchemaComparisonScope.Capture(model, type, null).Resolve(live, rules);
        var differences = new SchemaComparer(rules, limitations.Capture(rules, ["child"])).CompareRuntime(selected.Model, selected.Database);
        await Assert.That(differences.Single().Kind).IsEqualTo(SchemaDifferenceKind.ForeignKeyActionMismatch);
        await Assert.That(differences.Single().Message).Contains("Restrict");
    }

    [Test]
    public async Task RuntimeRelations_OmittedConstraintsMatchActionsBeforePairingDisagreements()
    {
        var model = RelationSet(("a", ReferentialAction.Cascade), ("b", ReferentialAction.Restrict));
        var live = RelationSet();
        var limitations = new SchemaReadLimitations();
        limitations.Add(new("child", "0", SchemaReadAspect.ForeignKey, SchemaReadLimitationReason.UnrepresentedForeignKeyTarget,
            "Restrict endpoint omitted.", ["parent_id"], "parent", ["id"], onUpdate: ReferentialAction.Restrict, onDelete: ReferentialAction.Restrict));
        limitations.Add(new("child", "1", SchemaReadAspect.ForeignKey, SchemaReadLimitationReason.UnrepresentedForeignKeyTarget,
            "Unknown actions.", ["parent_id"], "parent", ["id"]));
        var selected = SchemaComparisonScope.Capture(model, DatabaseType.SQLite, null).Resolve(live, SchemaIdentifierRules.SQLite);
        var evidence = limitations.Capture(SchemaIdentifierRules.SQLite, ["child"]);
        await Assert.That(new SchemaComparer(SchemaIdentifierRules.SQLite, evidence).CompareRuntime(selected.Model, selected.Database)).IsEmpty();
        // Removing the unknown constraint leaves one missing declaration; the
        // known RESTRICT relationship must match b rather than be consumed by a.
        var known = new SchemaReadLimitations();
        known.Add(new("child", "0", SchemaReadAspect.ForeignKey, SchemaReadLimitationReason.UnrepresentedForeignKeyTarget,
            "Restrict endpoint omitted.", ["parent_id"], "parent", ["id"], onUpdate: ReferentialAction.Restrict, onDelete: ReferentialAction.Restrict));
        var difference = new SchemaComparer(SchemaIdentifierRules.SQLite, known.Capture(SchemaIdentifierRules.SQLite, ["child"]))
            .CompareRuntime(selected.Model, selected.Database).Single();
        await Assert.That(difference.Kind).IsEqualTo(SchemaDifferenceKind.MissingForeignKey);
        await Assert.That(difference.Path).IsEqualTo("child.a");
    }

    [Test]
    public async Task RuntimeRelations_OneOmittedConstraintCannotHideTwoMissingDeclarations()
    {
        var model = RelationSet(("a", ReferentialAction.Restrict), ("b", ReferentialAction.Restrict));
        var live = RelationSet();
        var limitations = new SchemaReadLimitations();
        foreach (var message in new[] { "First diagnostic", "Second diagnostic for the same constraint" })
            limitations.Add(new("child", "0", SchemaReadAspect.ForeignKey, SchemaReadLimitationReason.UnrepresentedForeignKeyTarget,
                message, ["parent_id"], "parent", ["id"]));
        var selected = SchemaComparisonScope.Capture(model, DatabaseType.SQLite, null).Resolve(live, SchemaIdentifierRules.SQLite);
        var result = new SchemaComparer(SchemaIdentifierRules.SQLite, limitations.Capture(SchemaIdentifierRules.SQLite, ["child"]))
            .CompareRuntime(selected.Model, selected.Database);
        await Assert.That(result.Single().Kind).IsEqualTo(SchemaDifferenceKind.MissingForeignKey);
    }

    [Test]
    public async Task RuntimeRelations_SQLiteMatchesCatalogIdsByShapeAndKeepsDuplicateMultiplicity()
    {
        var model = RelationSet(("a", ReferentialAction.Unspecified), ("b", ReferentialAction.Cascade));
        var live = RelationSet(("0", ReferentialAction.Cascade), ("1", ReferentialAction.Restrict));
        await Assert.That(RuntimeCompare(model, live, SchemaIdentifierRules.SQLite)).IsEmpty();
        var changed = RelationSet(("0", ReferentialAction.Cascade), ("1", ReferentialAction.NoAction));
        await Assert.That(RuntimeCompare(model, changed, SchemaIdentifierRules.SQLite).Single().Kind)
            .IsEqualTo(SchemaDifferenceKind.ForeignKeyActionMismatch);
        var one = RelationSet(("0", ReferentialAction.Cascade));
        await Assert.That(RuntimeCompare(model, one, SchemaIdentifierRules.SQLite).Single().Kind).IsEqualTo(SchemaDifferenceKind.MissingForeignKey);
        await Assert.That(RuntimeCompare(one, model, SchemaIdentifierRules.SQLite).Single().Kind).IsEqualTo(SchemaDifferenceKind.ExtraForeignKey);
        await Assert.That(SchemaComparer.Compare(model, live, DatabaseType.SQLite).Count).IsEqualTo(4);
    }

    [Test]
    [Arguments(DatabaseType.MySQL)]
    [Arguments(DatabaseType.MariaDB)]
    public async Task RuntimeRelations_ServerRetainsNamesAndNormalizesOnlyUnspecifiedActions(DatabaseType type)
    {
        var model = RelationSet(("fk", ReferentialAction.Unspecified));
        var rules = SchemaIdentifierRules.Server(type, 0);
        await Assert.That(RuntimeCompare(model, RelationSet(("FK", ReferentialAction.Restrict)), rules)).IsEmpty();
        await Assert.That(RuntimeCompare(model, RelationSet(("fk", ReferentialAction.NoAction)), rules).Single().Kind)
            .IsEqualTo(SchemaDifferenceKind.ForeignKeyActionMismatch);
        await Assert.That(RuntimeCompare(model, RelationSet(("other", ReferentialAction.Restrict)), rules).Select(item => item.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.MissingForeignKey, SchemaDifferenceKind.ExtraForeignKey]);
    }

    private static DatabaseDefinition RelationSet(params (string Name, ReferentialAction Action)[] keys) => CreateDatabase(
        CreateTable("parent", [CreateColumn("id", typeof(int), false, primaryKey: true)]),
        CreateTable("child", [CreateColumn("id", typeof(int), false, primaryKey: true),
            CreateColumn("parent_id", typeof(int), false, attributes: keys.Select(key =>
                (System.Attribute)new ForeignKeyAttribute("parent", "id", key.Name, key.Action, key.Action)).ToArray())]));
}
