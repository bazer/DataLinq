using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.Testing;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class DuplicateForeignKeyRegenerationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task DuplicateForeignKeys_RegenerateTwiceWithCustomNavigationNamesAndActions(TestProviderDescriptor provider)
    {
        foreach (var composite in new[] { false, true })
        {
            var parentColumns = composite ? "id INT NOT NULL, tenant INT NOT NULL, PRIMARY KEY (id, tenant)" : "id INT NOT NULL PRIMARY KEY";
            var childColumns = composite ? "parent_id INT NULL, parent_tenant INT NULL" : "parent_id INT NULL";
            var from = composite ? "parent_id, parent_tenant" : "parent_id";
            var to = composite ? "id, tenant" : "id";
            using var schema = ServerSchemaDatabase.Create(provider, "DuplicateFkRegeneration",
                $"CREATE TABLE parents ({parentColumns})",
                $"CREATE TABLE children (id INT NOT NULL PRIMARY KEY, {childColumns}, CONSTRAINT fk_first FOREIGN KEY ({from}) REFERENCES parents({to}) ON UPDATE RESTRICT ON DELETE RESTRICT, CONSTRAINT fk_second FOREIGN KEY ({from}) REFERENCES parents({to}) ON UPDATE CASCADE ON DELETE SET NULL)");
            var sqlMetadata = schema.ParseDatabase("DuplicateDb", "DuplicateDb", "DuplicateRegeneration",
                new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
            var fileFactory = new ModelFileFactory(new ModelFileFactoryOptions { UseFileScopedNamespaces = true });
            var sourceFiles = fileFactory.CreateModelFiles(sqlMetadata).ToArray();
            var customNames = sqlMetadata.TableModels.SelectMany(table => table.Model.RelationProperties.Values)
                .Select(property => property.PropertyName).Distinct().ToArray();
            for (var i = 0; i < sourceFiles.Length; i++)
                foreach (var name in customNames)
                    sourceFiles[i].contents = sourceFiles[i].contents.Replace($" {name} {{ get; }}", $" Custom_{name} {{ get; }}", StringComparison.Ordinal);
            var source = Parse(sourceFiles);
            for (var iteration = 0; iteration < 2; iteration++)
            {
                var merged = new MetadataTransformer(new()).TryTransformDatabaseSnapshot(source, sqlMetadata).ValueOrException();
                source = Parse(fileFactory.CreateModelFiles(merged).ToArray());
                await Assert.That(source.TableModels.SelectMany(table => table.Model.RelationProperties.Values).All(property => property.PropertyName.StartsWith("Custom_", StringComparison.Ordinal))).IsTrue();
                var relations = source.GetTableModel("children").Model.RelationProperties.Values.Select(property => property.RelationPart.Relation).ToArray();
                await Assert.That(relations.Length).IsEqualTo(2);
                await Assert.That(relations.Single(relation => relation.ConstraintName == "fk_first").OnDelete).IsEqualTo(ReferentialAction.Restrict);
                await Assert.That(relations.Single(relation => relation.ConstraintName == "fk_second").OnDelete).IsEqualTo(ReferentialAction.SetNull);
                await Assert.That(relations.Single(relation => relation.ConstraintName == "fk_second").OnUpdate).IsEqualTo(ReferentialAction.Cascade);
            }
            var ddl = SqlFromMetadataFactory.GetFactoryFromDatabaseType(provider.DatabaseType).GetCreateTables(source, false).ValueOrException();
            using var generated = ServerSchemaDatabase.Create(provider, "DuplicateFkGenerated", ddl.Text);
            var roundtrip = generated.ParseDatabase("DuplicateDb", "DuplicateDb", "DuplicateRegeneration");
            await Assert.That(roundtrip.GetTableModel("children").Model.RelationProperties.Count).IsEqualTo(2);
            var secondRelation = roundtrip.GetTableModel("children").Model.RelationProperties.Values
                .Single(property => property.RelationPart.Relation.ConstraintName == "fk_second").RelationPart.Relation;
            await Assert.That(secondRelation.OnDelete).IsEqualTo(ReferentialAction.SetNull);
            await Assert.That(secondRelation.OnUpdate).IsEqualTo(ReferentialAction.Cascade);
            await Assert.That(sqlMetadata.IsFrozen).IsTrue();
        }
    }

    private static DatabaseDefinition Parse((string path, string contents)[] files)
        => new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions()).ReadSyntaxTrees(files
            .SelectMany(file => CSharpSyntaxTree.ParseText(file.contents, path: file.path).GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            .ToImmutableArray()).Single().ValueOrException();
}
