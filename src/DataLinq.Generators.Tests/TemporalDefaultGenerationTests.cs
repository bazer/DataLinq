using System;
using System.Collections.Immutable;
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

public class TemporalDefaultGenerationTests : GeneratorTestBase
{
    [Test]
    public async Task FixedTemporalDefaults_GenerateCompileAndRegenerateWithoutPrecisionLoss()
    {
        var timestamp = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddTicks(1234567);
        var offset = new DateTimeOffset(2020, 2, 3, 4, 5, 6, TimeSpan.FromHours(5.5)).AddTicks(7654321);
        var duration = TimeSpan.FromHours(-25).Add(TimeSpan.FromTicks(-1234567));
        var draft = new MetadataDatabaseDraft("temporal_defaults", new CsTypeDeclaration("TemporalDb", "TemporalRepro", ModelCsType.Class))
        {
            TableModels = [new MetadataTableModelDraft("Rows",
                new MetadataModelDraft(new CsTypeDeclaration("TemporalRow", "TemporalRepro", ModelCsType.Class))
                {
                    ValueProperties = [
                        new MetadataValuePropertyDraft("Id", new CsTypeDeclaration(typeof(int)),
                            new MetadataColumnDraft("id") { PrimaryKey = true, DbTypes = [new DatabaseColumnType(DatabaseType.MariaDB, "int")] }),
                        Property("ChangedAt", typeof(DateTime), "datetime", timestamp),
                        Property("OffsetAt", typeof(DateTimeOffset), "datetime", offset),
                        Property("Duration", typeof(TimeSpan), "time", duration)
                    ]
                }, new MetadataTableDraft("temporal_rows"))]
        };
        var database = new MetadataDefinitionFactory().Build(draft).ValueOrException();
        var files = new ModelFileFactory(new ModelFileFactoryOptions()).CreateModelFiles(database).ToArray();
        var source = string.Join("\n", files.Select(x => x.contents));
        await Assert.That(source).Contains("DefaultDateTime(\"1970-01-01T00:00:00.1234567\"");
        await Assert.That(source).Contains("DefaultDateTimeOffset(\"2020-02-03T04:05:06.7654321+05:30\")");
        await Assert.That(source).Contains("DefaultTimeSpan(\"-1.01:00:00.1234567\")");
        var trees = files.Select(x => CSharpSyntaxTree.ParseText(x.contents, path: x.path)).ToArray();
        var (compilation, diagnostics, generated) = RunGeneratorWithDiagnostics(trees);
        await Assert.That(diagnostics.Concat(compilation.GetDiagnostics()).Where(x => x.Severity == DiagnosticSeverity.Error)).IsEmpty();
        var generatedCode = string.Join("\n", generated.Select(x => x.ToString()));
        await Assert.That(generatedCode).Contains($"new global::System.DateTime({timestamp.Ticks}L, global::System.DateTimeKind.Unspecified)");
        await Assert.That(generatedCode).Contains($"new global::System.DateTimeOffset({offset.Ticks}L, global::System.TimeSpan.FromTicks({offset.Offset.Ticks}L))");
        await Assert.That(generatedCode).Contains($"global::System.TimeSpan.FromTicks({duration.Ticks}L)");

        var parser = new SyntaxParser(trees.SelectMany(x => x.GetRoot().DescendantNodes())
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>().ToImmutableArray());
        var attributes = trees.SelectMany(x => x.GetRoot().DescendantNodes())
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax>()
            .Where(x => x.Name.ToString().StartsWith("Default", StringComparison.Ordinal))
            .Select(x => (DefaultAttribute)parser.ParseAttribute(x).ValueOrException()).ToArray();
        await Assert.That(attributes.Select(x => x.Value)).IsEquivalentTo(new object[] { timestamp, offset, duration });
        await Assert.That(attributes.All(x => x.CodeExpression is null)).IsTrue();
        var reparsed = attributes.Single(x => x.Value is DateTime);
        await Assert.That(((DateTime)reparsed.Value).Kind).IsEqualTo(DateTimeKind.Unspecified);
        var reparsedDatabase = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions())
            .ReadSyntaxTrees(trees.SelectMany(x => x.GetRoot().DescendantNodes())
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>().ToImmutableArray())
            .Single().ValueOrException();
        var regeneratedFiles = new ModelFileFactory(new ModelFileFactoryOptions()).CreateModelFiles(reparsedDatabase).ToArray();
        var (regeneratedCompilation, regeneratedDiagnostics, _) = RunGeneratorWithDiagnostics(
            regeneratedFiles.Select(x => CSharpSyntaxTree.ParseText(x.contents, path: x.path)).ToArray());
        await Assert.That(regeneratedDiagnostics.Concat(regeneratedCompilation.GetDiagnostics())
            .Where(x => x.Severity == DiagnosticSeverity.Error)).IsEmpty();
        await Assert.That(reparsedDatabase.TableModels.Single().Model.ValueProperties["ChangedAt"].GetDefaultAttribute()!.Value).IsEqualTo(timestamp);
    }

    private static MetadataValuePropertyDraft Property(string name, Type type, string sqlType, object value)
        => new(name, new CsTypeDeclaration(type), new MetadataColumnDraft(name)
        { DbTypes = [new DatabaseColumnType(DatabaseType.MariaDB, sqlType)] })
        { Attributes = [new DefaultAttribute(value)] };
}
