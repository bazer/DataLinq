using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ThrowAway.Extensions;

namespace DataLinq.Generators.Tests;

public class RelationNameGenerationTests : GeneratorTestBase
{
    [Test]
    [Arguments("Forsandelser", false, false, 0)]
    [Arguments("forsandelser", false, false, 0)]
    [Arguments("Forsandelser", false, true, 0)]
    [Arguments("Forsandelser", true, true, 0)]
    [Arguments("Forsandelser", false, false, 2)]
    public async Task SelfReference_ReservesModelNameAndCompiles(
        string modelName, bool composite, bool bothDirections, int occupiedFallbacks)
    {
        var additionalProperties = Enumerable.Range(1, occupiedFallbacks)
            .Select(index => Column(modelName + "Relation" + (index == 1 ? "" : index.ToString())))
            .ToArray();
        var database = new MetadataDefinitionFactory().BuildProviderMetadata(
            SelfReferenceDraft(modelName, composite, bothDirections, additionalProperties)).ValueOrException();
        var model = database.TableModels.Single().Model;
        var parent = model.RelationProperties.Values.Single(property => property.RelationPart.Type == RelationPartType.ForeignKey);
        var children = model.RelationProperties.Values.Single(property => property.RelationPart.Type == RelationPartType.CandidateKey);

        await Assert.That(parent.PropertyName).IsEqualTo(bothDirections ? modelName + "Relation" : "Gammal");
        await Assert.That(children.PropertyName).IsEqualTo(bothDirections
            ? modelName + "Relation_2"
            : modelName + "Relation" + (occupiedFallbacks == 0 ? "" : (occupiedFallbacks + 1).ToString()));
        await Assert.That(parent.CsNullable).IsTrue();
        await Assert.That(children.RelationPart.GetOtherSide()).IsSameReferenceAs(parent.RelationPart);
        await Assert.That(parent.RelationPart.Relation.ConstraintName).IsEqualTo("fk_self");
        await Assert.That(parent.RelationPart.ColumnIndex.Columns.Count).IsEqualTo(composite ? 2 : 1);
        await AssertCompiles(database);
    }

    [Test]
    [Arguments("ParentRelation", false, "ParentRelation2")]
    [Arguments("ParentRelation2", true, "ParentRelation3")]
    public async Task FallbackCandidateMatchingModelName_IsSkippedAndCompiles(
        string modelName, bool occupyFirstFallback, string expectedName)
    {
        var properties = new List<MetadataValuePropertyDraft> { Column("Parent") };
        if (occupyFirstFallback)
            properties.Add(Column("ParentRelation"));
        var draft = SelfReferenceDraft(modelName, false, false, properties, foreignKeyName: "ParentId");
        var database = new MetadataDefinitionFactory().BuildProviderMetadata(draft).ValueOrException();
        var parent = database.TableModels.Single().Model.RelationProperties.Values
            .Single(property => property.RelationPart.Type == RelationPartType.ForeignKey);

        await Assert.That(parent.PropertyName).IsEqualTo(expectedName);
        await AssertCompiles(database);
    }

    [Test]
    public async Task ExplicitRelationOccupyingFallbackName_IsPreservedAndCompiles()
    {
        var draft = SelfReferenceDraft("Forsandelser", false, false);
        var table = draft.TableModels.Single();
        draft = draft with
        {
            TableModels = [table with
            {
                Model = table.Model with
                {
                    RelationProperties = [new("ForsandelserRelation", table.Model.CsType)
                    {
                        CsNullable = true,
                        Attributes = [new RelationAttribute("forsandelser", "ForsandelseID", "fk_self")]
                    }]
                }
            }]
        };
        var database = new MetadataDefinitionFactory().BuildProviderMetadata(draft).ValueOrException();
        var model = database.TableModels.Single().Model;
        await Assert.That(model.RelationProperties["ForsandelserRelation"].RelationPart.Type).IsEqualTo(RelationPartType.ForeignKey);
        await Assert.That(model.RelationProperties["ForsandelserRelation2"].RelationPart.Type).IsEqualTo(RelationPartType.CandidateKey);
        await AssertCompiles(database);
    }

    [Test]
    public async Task ExplicitSelfRelationNames_ArePreservedAndCompile()
    {
        var draft = SelfReferenceDraft("Forsandelser", false, false);
        var table = draft.TableModels.Single();
        draft = draft with
        {
            TableModels = [table with
            {
                Model = table.Model with
                {
                    RelationProperties = [
                        new("Previous", table.Model.CsType)
                        {
                            CsNullable = true,
                            Attributes = [new RelationAttribute("forsandelser", "ForsandelseID", "fk_self")]
                        },
                        new("Children", new("IImmutableRelation<Forsandelser>", "DataLinq.Instances", ModelCsType.Interface))
                        {
                            Attributes = [new RelationAttribute("forsandelser", "GammalFK", "fk_self")]
                        }
                    ]
                }
            }]
        };
        var database = new MetadataDefinitionFactory().BuildProviderMetadata(draft).ValueOrException();
        await Assert.That(database.TableModels.Single().Model.RelationProperties.Keys)
            .IsEquivalentTo(new[] { "Previous", "Children" });
        await AssertCompiles(database);
    }

    private async Task AssertCompiles(DatabaseDefinition database)
    {
        var files = new ModelFileFactory(new ModelFileFactoryOptions()).CreateModelFiles(database).ToArray();
        var trees = files.Select(file => CSharpSyntaxTree.ParseText(file.contents, path: file.path)).ToArray();
        var (compilation, diagnostics, generated) = RunGeneratorWithDiagnostics(trees);
        await Assert.That(generated.Any()).IsTrue();
        await Assert.That(diagnostics.Concat(compilation.GetDiagnostics())
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsEmpty();
    }

    private static MetadataDatabaseDraft SelfReferenceDraft(
        string modelName,
        bool composite,
        bool bothDirections,
        IReadOnlyList<MetadataValuePropertyDraft>? additionalProperties = null,
        string? foreignKeyName = null)
    {
        var properties = new List<MetadataValuePropertyDraft>();
        for (var index = 0; index < (composite ? 2 : 1); index++)
        {
            var key = index == 0 ? "ForsandelseID" : "TenantID";
            var foreignKey = index == 0
                ? foreignKeyName ?? (bothDirections ? modelName + "ID" : "GammalFK")
                : "ParentTenantID";
            properties.Add(Column(key) with
            {
                Column = new(key) { PrimaryKey = true, DbTypes = [new(DatabaseType.MariaDB, "int")] },
                Attributes = [new ColumnAttribute(key), new PrimaryKeyAttribute()]
            });
            properties.Add(Column(foreignKey) with
            {
                Column = new(foreignKey) { ForeignKey = true, Nullable = true, DbTypes = [new(DatabaseType.MariaDB, "int")] },
                CsNullable = true,
                Attributes = [new ColumnAttribute(foreignKey), new NullableAttribute(), new ForeignKeyAttribute("forsandelser", key, "fk_self", index + 1)]
            });
        }
        if (additionalProperties is not null)
            properties.AddRange(additionalProperties);
        return new("epostko", new("Epostko", "RelationNames", ModelCsType.Class))
        {
            TableModels = [new("Rows", new(new(modelName, "RelationNames", ModelCsType.Class))
            {
                ValueProperties = properties
            }, new("forsandelser"))]
        };
    }

    private static MetadataValuePropertyDraft Column(string name)
        => new(name, new(typeof(int)), new(name) { DbTypes = [new(DatabaseType.MariaDB, "int")] })
        { Attributes = [new ColumnAttribute(name)] };
}
