using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Validation;

namespace DataLinq.Tests.Unit.Core;

public partial class SchemaComparerTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task RuntimeIdentifiers_TableAndEmbeddedNamesUseDifferentRules(int mode)
    {
        var model = RuntimeRelations("Parent", "Child", "ID", "parentID", "IX_Parent", "FK_Parent");
        var live = RuntimeRelations("parent", "child", "id", "parentid", "ix_parent", "fk_parent");
        var rules = SchemaIdentifierRules.Server(DatabaseType.MySQL, mode);
        var differences = RuntimeCompare(model, live, rules);
        if (mode == 0)
            await Assert.That(differences.Select(difference => difference.Kind).ToArray())
                .IsEquivalentTo([SchemaDifferenceKind.MissingTable, SchemaDifferenceKind.MissingTable, SchemaDifferenceKind.ExtraTable, SchemaDifferenceKind.ExtraTable]);
        else await Assert.That(differences).IsEmpty();
        // Existing CLI behavior is deliberately not changed by runtime rules.
        await Assert.That(SchemaComparer.Compare(model, live, DatabaseType.MySQL).Count).IsEqualTo(4);
    }

    [Test]
    public async Task RuntimeIdentifiers_ColumnsAndIndexesIgnoreCaseOnCaseSensitiveServer()
    {
        var model = RuntimeRelations("parent", "child", "ID", "parentID", "IX_Parent", "FK_Parent");
        var live = RuntimeRelations("parent", "child", "id", "parentid", "ix_parent", "fk_parent");
        await Assert.That(RuntimeCompare(model, live, SchemaIdentifierRules.Server(DatabaseType.MariaDB, 0))).IsEmpty();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task RuntimeIdentifiers_UnselectedForeignKeyTargetRetainsItsIdentityAndDoesNotExpandScope(int mode)
    {
        var model = RuntimeRelations("Parent", "child", "ID", "ParentID", "IX_Parent", "FK_Parent");
        var live = RuntimeRelations("parent", "child", "id", "parentid", "ix_parent", "fk_parent");
        var rules = SchemaIdentifierRules.Server(DatabaseType.MySQL, mode);
        var scope = SchemaComparisonScope.Capture(model, DatabaseType.MySQL, ["child", "child"]);
        var selected = scope.Resolve(live, rules);
        await Assert.That(selected.Model.Count).IsEqualTo(1);
        await Assert.That(selected.Database.Count).IsEqualTo(1);
        await Assert.That(selected.Model[0]).IsSameReferenceAs(model.TableModels.Single(table => table.Table.DbName == "child"));
        var differences = new SchemaComparer(rules).CompareRuntime(selected.Model, selected.Database);
        if (mode == 0)
            await Assert.That(differences.Select(difference => difference.Kind).ToArray())
                .IsEquivalentTo([SchemaDifferenceKind.MissingForeignKey, SchemaDifferenceKind.ExtraForeignKey]);
        else await Assert.That(differences).IsEmpty();
        await Assert.That(model.TableModels.Length).IsEqualTo(2);
        await Assert.That(live.TableModels.Length).IsEqualTo(2);
    }

    [Test]
    public async Task RuntimeIdentifiers_SqlLiteralsDefaultsAndCommentsRemainCaseSensitive()
    {
        DatabaseDefinition Data(string value, string name) => CreateDatabase(CreateTable("values",
            [CreateColumn("value", typeof(string), false, primaryKey: true, defaultValue: value, attributes: [new CommentAttribute(DatabaseType.MySQL, value)])],
            [new CheckAttribute(DatabaseType.MySQL, name, $"value <> '{value}'"), new CommentAttribute(DatabaseType.MySQL, value)]));
        var differences = RuntimeCompare(Data("A", "CK_Value"), Data("a", "ck_value"), SchemaIdentifierRules.Server(DatabaseType.MySQL, 1));
        await Assert.That(differences.Select(difference => difference.Kind).ToArray()).IsEquivalentTo([
            SchemaDifferenceKind.ColumnDefaultMismatch, SchemaDifferenceKind.ColumnCommentMismatch,
            SchemaDifferenceKind.MissingCheck, SchemaDifferenceKind.ExtraCheck, SchemaDifferenceKind.TableCommentMismatch]);
        await Assert.That(differences.Single(difference => difference.Kind == SchemaDifferenceKind.MissingCheck).Path).IsEqualTo("values.CK_Value");
    }

    [Test]
    [Arguments(DatabaseType.MySQL)]
    [Arguments(DatabaseType.MariaDB)]
    public async Task RuntimeScope_ServerIncludeRequiresExactModelSpellingBeforeObservation(DatabaseType type)
    {
        var model = CreateDatabase(("Orders", ["id"]));
        var failure = await Assert.That(() => SchemaComparisonScope.Capture(model, type, ["orders"])).Throws<ArgumentException>();
        await Assert.That(failure!.Message).Contains("exact compiled-model spelling 'Orders'");
        var input = new[] { "Orders", "Orders" };
        var scope = SchemaComparisonScope.Capture(model, type, input);
        input[0] = "missing";
        var selected = scope.Resolve(CreateDatabase(("orders", ["id"]), ("ignored", ["id"])), SchemaIdentifierRules.Server(type, 1));
        await Assert.That(selected.Model.Count).IsEqualTo(1);
        await Assert.That(selected.Database.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RuntimeScope_SQLiteIncludeUsesKnownNamesAndMissingLiveObjectRemainsDifference()
    {
        var model = CreateDatabase(("Orders", ["id"]), ("Ignored", ["id"]));
        var scope = SchemaComparisonScope.Capture(model, DatabaseType.SQLite, ["orders", "ORDERS"]);
        var selected = scope.Resolve(CreateDatabase(("unselected", ["id"])), SchemaIdentifierRules.SQLite);
        await Assert.That(selected.Model.Count).IsEqualTo(1);
        await Assert.That(selected.Database.Count).IsEqualTo(0);
        var differences = new SchemaComparer(SchemaIdentifierRules.SQLite).CompareRuntime(selected.Model, selected.Database);
        await Assert.That(differences.Single().Kind).IsEqualTo(SchemaDifferenceKind.MissingTable);
        await Assert.That(differences.Single().Path).IsEqualTo("Orders");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task RuntimeScope_RejectsRelevantCollisionsAndPreservesCaseSensitiveDistinctTables(int mode)
    {
        var duplicate = RuntimeNamedTables("Orders", "orders", "Other");
        var rules = SchemaIdentifierRules.Server(DatabaseType.MySQL, mode);
        var full = SchemaComparisonScope.Capture(duplicate, DatabaseType.MySQL, null);
        if (mode == 0)
        {
            var resolved = full.Resolve(duplicate, rules);
            await Assert.That(new SchemaComparer(rules).CompareRuntime(resolved.Model, resolved.Database)).IsEmpty();
            await Assert.That(resolved.Model.Count).IsEqualTo(3);
        }
        else
        {
            var failure = await Assert.That(() => full.Resolve(duplicate, rules)).Throws<ArgumentException>();
            await Assert.That(failure!.Message).Contains("'Orders' and 'orders'");
            var selected = SchemaComparisonScope.Capture(duplicate, DatabaseType.MySQL, ["Orders"]);
            await Assert.That(() => selected.Resolve(duplicate, rules)).Throws<ArgumentException>();
            var liveCollision = SchemaComparisonScope.Capture(RuntimeNamedTables("Orders"), DatabaseType.MySQL, ["Orders"]);
            await Assert.That(() => liveCollision.Resolve(duplicate, rules)).Throws<InvalidOperationException>();
        }
        var unrelated = SchemaComparisonScope.Capture(duplicate, DatabaseType.MySQL, ["Other"]).Resolve(duplicate, rules);
        await Assert.That(unrelated.Model.Count).IsEqualTo(1);
        await Assert.That(new SchemaComparer(rules).CompareRuntime(unrelated.Model, unrelated.Database)).IsEmpty();
    }

    [Test]
    public async Task RuntimeIdentifiers_SettingParsingIsStrictAndCultureIndependent()
    {
        foreach (var invalid in new object?[] { null, DBNull.Value, "", "01", " 1", "1.0", true, 1.5, 1m, -1, 3, new object() })
            await Assert.That(() => SchemaIdentifierRules.Server(DatabaseType.MySQL, invalid)).Throws<InvalidOperationException>();
        foreach (var valid in new object[] { 1, 1L, 1UL, (byte)1, "1" })
            await Assert.That(SchemaIdentifierRules.Server(DatabaseType.MariaDB, valid).TableNames.Equals("Åccount", "åccount")).IsTrue();
        await Assert.That(SchemaIdentifierRules.Server(DatabaseType.MySQL, 0).TableNames.Equals("Åccount", "åccount")).IsFalse();
    }

    [Test]
    public async Task RuntimeScope_ForeignKeyCannotResolveAnAmbiguousUnselectedTarget()
    {
        var model = CreateDatabase(
            CreateTable("Parent", [CreateColumn("id", typeof(int), false, primaryKey: true)]),
            CreateTable("parent", [CreateColumn("id", typeof(int), false, primaryKey: true)]) with
            { CsPropertyName = "SecondParent", Model = new MetadataModelDraft(new CsTypeDeclaration("SecondParent", "DataLinq.Tests", ModelCsType.Class))
                { ValueProperties = [CreateColumn("id", typeof(int), false, primaryKey: true)] } },
            CreateTable("child", [CreateColumn("parent_id", typeof(int), false, primaryKey: true,
                attributes: [new ForeignKeyAttribute("Parent", "id", "fk_parent")])]));
        var scope = SchemaComparisonScope.Capture(model, DatabaseType.MySQL, ["child"]);
        var error = await Assert.That(() => scope.Resolve(model, SchemaIdentifierRules.Server(DatabaseType.MySQL, 1))).Throws<ArgumentException>();
        await Assert.That(error!.Message).Contains("referenced table");
        var selected = scope.Resolve(model, SchemaIdentifierRules.Server(DatabaseType.MySQL, 0));
        await Assert.That(selected.Model.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RuntimeIdentifiers_RejectsColumnAndIndexCollisionsByKind()
    {
        var columns = CreateDatabase(CreateTable("values", [
            CreateColumn("ID", typeof(int), false, primaryKey: true) with { PropertyName = "First" },
            CreateColumn("id", typeof(int), false) with { PropertyName = "Second" }]));
        var indexes = CreateDatabase(CreateTable("values", [CreateColumn("id", typeof(int), false, primaryKey: true)], [
            new IndexAttribute("IX_Value", IndexCharacteristic.Simple, IndexType.BTREE, "id"),
            new IndexAttribute("ix_value", IndexCharacteristic.Simple, IndexType.BTREE, "id")]));
        var rules = SchemaIdentifierRules.Server(DatabaseType.MySQL, 0);
        var columnFailure = await Assert.That(() => RuntimeCompare(columns, columns, rules)).Throws<ArgumentException>();
        await Assert.That(columnFailure!.Message).Contains("column in 'values'");
        var indexFailure = await Assert.That(() => RuntimeCompare(indexes, indexes, rules)).Throws<ArgumentException>();
        await Assert.That(indexFailure!.Message).Contains("index in 'values'");
    }

    [Test]
    public async Task RuntimeScope_EmptyIncludeKeepsAllTablesAndViewsInStableOrder()
    {
        var model = CreateDatabase(CreateTable("Z_table", [CreateColumn("id", typeof(int), false, primaryKey: true)]),
            CreateTable("A_view", TableType.View, [CreateColumn("id", typeof(int), false)]));
        var scope = SchemaComparisonScope.Capture(model, DatabaseType.SQLite, []);
        var selected = scope.Resolve(model, SchemaIdentifierRules.SQLite);
        await Assert.That(selected.Model.Select(table => table.Table.DbName).SequenceEqual(["A_view", "Z_table"])).IsTrue();
        await Assert.That(selected.Database.Count).IsEqualTo(2);
        await Assert.That(new SchemaComparer(SchemaIdentifierRules.SQLite).CompareRuntime(selected.Model, selected.Database)).IsEmpty();
    }

    private static System.Collections.Generic.IReadOnlyList<SchemaDifference> RuntimeCompare(DatabaseDefinition model, DatabaseDefinition database, SchemaIdentifierRules rules)
    {
        var scope = SchemaComparisonScope.Capture(model, rules.DatabaseType, null).Resolve(database, rules);
        return new SchemaComparer(rules).CompareRuntime(scope.Model, scope.Database);
    }

    private static DatabaseDefinition RuntimeRelations(string parent, string child, string id, string parentId, string index, string constraint) => CreateDatabase(
        CreateTable(parent, [CreateColumn(id, typeof(int), false, primaryKey: true)]),
        CreateTable(child, [CreateColumn(parentId, typeof(int), false, primaryKey: true, attributes: [new ForeignKeyAttribute(parent, id, constraint)])],
            [new IndexAttribute(index, IndexCharacteristic.Simple, IndexType.BTREE, parentId)]));

    private static DatabaseDefinition RuntimeNamedTables(params string[] names) => CreateDatabase(names.Select((name, index) =>
        new MetadataTableModelDraft("Table" + index,
            new MetadataModelDraft(new CsTypeDeclaration("Table" + index, "DataLinq.Tests", ModelCsType.Class))
            { ValueProperties = [CreateColumn("id", typeof(int), false, primaryKey: true)] },
            new MetadataTableDraft(name) { Type = TableType.Table })).ToArray());
}
