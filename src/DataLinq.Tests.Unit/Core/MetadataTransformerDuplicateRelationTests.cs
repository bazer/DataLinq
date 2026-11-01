using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class MetadataTransformerDuplicateRelationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DuplicateEndpoints_PreserveBothNavigationNamesActionsAndGraph(bool composite, bool updateNames)
    {
        var source = Create(["fk_first", "fk_second"], "Source", composite);
        // Reverse enumeration to prove constraint identity determines the match.
        var destination = Create(["fk_second", "fk_first"], "Database", composite);
        var transformer = new MetadataTransformer(new MetadataTransformerOptions { UpdateConstraintNames = updateNames });
        var merged = transformer.TransformDatabaseSnapshot(source, destination);
        await AssertMerged(merged, source, ["fk_first", "fk_second"]);
        await AssertGraph(merged);
        // A merged snapshot must remain copyable and usable as the next destination.
        var repeated = transformer.TransformDatabaseSnapshot(source, merged);
        await AssertMerged(repeated, source, ["fk_first", "fk_second"]);
        await AssertGraph(repeated);
        await Assert.That(destination.GetTableModel("children").Model.RelationProperties.Keys).Contains("DatabaseParent0");
        await Assert.That(source.IsFrozen && destination.IsFrozen).IsTrue();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExactIdentityIsReservedBeforeTheOneRemainingRename(bool composite, bool updateNames)
    {
        var source = Create(["fk_first", "fk_second"], "Source", composite);
        var destination = Create(["fk_renamed", "fk_second"], "Database", composite);
        var transformer = new MetadataTransformer(new MetadataTransformerOptions { UpdateConstraintNames = updateNames });
        var merged = transformer.TransformDatabaseSnapshot(source, destination);
        await AssertMerged(merged, source, [updateNames ? "fk_first" : "fk_renamed", "fk_second"]);
        await AssertGraph(merged);
        var childFile = new ModelFileFactory(new ModelFileFactoryOptions()).CreateModelFiles(merged).Single(file => file.path == "Child.cs").contents;
        var name = updateNames ? "fk_first" : "fk_renamed";
        await Assert.That(childFile).Contains($"\"{name}\"");
        await Assert.That(childFile).DoesNotContain($"\"{(updateNames ? "fk_renamed" : "fk_first")}\"");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AddedOrRemovedConstraint_PreservesOnlyTheSurvivingIdentities(bool added)
    {
        var source = Create(added ? ["fk_first"] : ["fk_first", "fk_second"], "Source", false);
        var destination = Create(added ? ["fk_second", "fk_first"] : ["fk_second"], "Database", false);
        var merged = new MetadataTransformer(new()).TransformDatabaseSnapshot(source, destination);
        var child = merged.GetTableModel("children").Model;
        await Assert.That(child.RelationProperties.Count).IsEqualTo(added ? 2 : 1);
        await Assert.That(child.RelationProperties.Keys).Contains(added ? "SourceParent0" : "SourceParent1");
        if (added) await Assert.That(child.RelationProperties.Keys).Contains("DatabaseParent0");
        await AssertGraph(merged);
    }

    [Test]
    public async Task AddedConstraint_CollidingNavigationNameReturnsDiagnostic()
    {
        var source = Create(["fk_first"], "Database", false);
        var destination = Create(["fk_second", "fk_first"], "Database", false);
        var result = new MetadataTransformer(new()).TryTransformDatabaseSnapshot(source, destination);
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("duplicate property 'DatabaseParent0'");
        await Assert.That(destination.GetTableModel("children").Model.RelationProperties.Count).IsEqualTo(2);
        await Assert.That(source.GetTableModel("children").Model.RelationProperties.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AmbiguousRename_ReturnsActionableFailureWithoutMutatingInputs(bool composite)
    {
        var source = Create(["old_first", "old_second"], "Source", composite);
        var destination = Create(["new_first", "new_second"], "Database", composite);
        var result = new MetadataTransformer(new()).TryTransformDatabaseSnapshot(source, destination);
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        foreach (var name in new[] { "old_first", "old_second", "new_first", "new_second", "--fresh", "[Relation]" })
            await Assert.That(failure.Message).Contains(name);
        await Assert.That(failure.GetMostRelevantSourceLocation()?.File.Name).IsEqualTo("SourceRelations.cs");
        await Assert.That(destination.GetTableModel("children").Model.RelationProperties.Keys).Contains("DatabaseParent0");
        await Assert.That(source.GetTableModel("children").Model.RelationProperties.Keys).Contains("SourceParent0");
    }

    [Test]
    public async Task SelfReferences_PreserveBothDirections()
    {
        var type = new CsTypeDeclaration("Node", "DuplicateRelations", ModelCsType.Class);
        var draft = new MetadataDatabaseDraft("SelfDb", new("SelfDb", "DuplicateRelations", ModelCsType.Class))
        {
            TableModels = [new("Nodes", new(type)
            {
                ValueProperties = [Column("Id", "id", true, []), Column("ParentId", "parent_id", false, [new ForeignKeyAttribute("nodes", "id", "fk_self")])],
                RelationProperties = [
                    new("Parent", type) { Attributes = [new RelationAttribute("nodes", "id", "fk_self")] },
                    new("Children", new("IImmutableRelation<Node>", "DataLinq.Instances", ModelCsType.Interface)) { Attributes = [new RelationAttribute("nodes", "parent_id", "fk_self")] }
                ]
            }, new("nodes"))]
        };
        var source = new MetadataDefinitionFactory().Build(draft).ValueOrException();
        var merged = new MetadataTransformer(new()).TransformDatabaseSnapshot(source, source);
        await Assert.That(merged.GetTableModel("nodes").Model.RelationProperties.Count).IsEqualTo(2);
        await AssertGraph(merged);
    }

    private static async Task AssertMerged(DatabaseDefinition merged, DatabaseDefinition source, string[] constraints)
    {
        foreach (var table in merged.TableModels)
        {
            var sourceModel = source.GetTableModel(table.Table.DbName).Model;
            await Assert.That(table.Model.RelationProperties.Keys.ToArray()).IsEquivalentTo(sourceModel.RelationProperties.Keys.ToArray());
            foreach (var property in table.Model.RelationProperties.Values)
            {
                var ordinal = int.Parse(property.PropertyName.Substring(property.PropertyName.Length - 1));
                await Assert.That(property.RelationPart.Relation.ConstraintName).IsEqualTo(constraints[ordinal]);
                await Assert.That(property.RelationPart.Relation.OnDelete).IsEqualTo(constraints[ordinal] == "fk_second" ? ReferentialAction.SetNull : ReferentialAction.Restrict);
            }
        }
    }

    private static async Task AssertGraph(DatabaseDefinition database)
    {
        foreach (var property in database.TableModels.SelectMany(table => table.Model.RelationProperties.Values))
        {
            var part = property.RelationPart;
            await Assert.That(part.ColumnIndex.RelationParts.Contains(part)).IsTrue();
            await Assert.That(part.Type == RelationPartType.ForeignKey ? part.Relation.ForeignKey : part.Relation.CandidateKey).IsSameReferenceAs(part);
            await Assert.That(part.GetOtherSide().GetOtherSide()).IsSameReferenceAs(part);
        }
    }

    private static DatabaseDefinition Create(string[] constraints, string prefix, bool composite)
    {
        var parent = new CsTypeDeclaration("Parent", "DuplicateRelations", ModelCsType.Class);
        var child = new CsTypeDeclaration("Child", "DuplicateRelations", ModelCsType.Class);
        var parentColumns = composite ? new[] { "id", "tenant" } : new[] { "id" };
        var childColumns = composite ? new[] { "parent_id", "parent_tenant" } : new[] { "parent_id" };
        var parentProperties = parentColumns.Select((column, index) => Column($"Key{index}", column, true, [])).ToArray();
        var childProperties = new[] { Column("Id", "id", true, []) }.Concat(childColumns.Select((column, index) => Column($"ParentKey{index}", column, false,
            constraints.Select(name => new ForeignKeyAttribute("parents", parentColumns[index], name, index + 1, ReferentialAction.Restrict,
                name == "fk_second" ? ReferentialAction.SetNull : ReferentialAction.Restrict)).ToArray()))).ToArray();
        return new MetadataDefinitionFactory().Build(new MetadataDatabaseDraft("RelationsDb", new("RelationsDb", "DuplicateRelations", ModelCsType.Class))
        {
            TableModels = [
                new("Children", new(child)
                {
                    CsFile = new CsFileDeclaration(prefix + "Relations.cs"),
                    ValueProperties = childProperties,
                    RelationProperties = constraints.Select((name, index) => new MetadataRelationPropertyDraft($"{prefix}Parent{index}", parent)
                    { CsNullable = true, Attributes = [new RelationAttribute("parents", parentColumns, name)] }).ToArray()
                }, new("children")),
                new("Parents", new(parent)
                {
                    CsFile = new CsFileDeclaration(prefix + "Relations.cs"),
                    ValueProperties = parentProperties,
                    RelationProperties = constraints.Select((name, index) => new MetadataRelationPropertyDraft($"{prefix}Children{index}", new("IImmutableRelation<Child>", "DataLinq.Instances", ModelCsType.Interface))
                    { Attributes = [new RelationAttribute("children", childColumns, name)] }).ToArray()
                }, new("parents"))
            ]
        }).ValueOrException();
    }

    private static MetadataValuePropertyDraft Column(string property, string column, bool primaryKey, Attribute[] foreignKeys)
        => new(property, new(typeof(int)), new(column)
        { PrimaryKey = primaryKey, ForeignKey = foreignKeys.Length > 0, Nullable = !primaryKey,
            DbTypes = [new(DatabaseType.SQLite, "integer"), new(DatabaseType.MySQL, "int"), new(DatabaseType.MariaDB, "int")] })
        {
            CsNullable = !primaryKey,
            Attributes = new Attribute[] { new ColumnAttribute(column) }.Concat(primaryKey ? [new PrimaryKeyAttribute()] : [new NullableAttribute()]).Concat(foreignKeys).ToArray()
        };
}
