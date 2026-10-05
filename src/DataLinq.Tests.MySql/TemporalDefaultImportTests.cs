using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Testing;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class TemporalDefaultImportTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task FixedDateTimeDefault_ImportRenderParseAndMergePreserveValue(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider,
            nameof(FixedDateTimeDefault_ImportRenderParseAndMergePreserveValue),
            "CREATE TABLE temporal_rows (id INT NOT NULL AUTO_INCREMENT PRIMARY KEY, changed_at DATETIME(6) NOT NULL DEFAULT '1970-01-01 00:00:00.123456');");
        var imported = schema.ParseDatabase("TemporalDb", "TemporalDb", "TemporalImport");
        var expected = new DateTime(1970, 1, 1).AddTicks(1234560);
        var attribute = imported.TableModels.Single().Table.Columns.Single(x => x.DbName == "changed_at")
            .ValueProperty.GetDefaultAttribute()!;
        // MariaDB reports a quoted typed literal; MySQL's unquoted metadata
        // retains the existing provider-scoped SQL fallback.
        var expectedCarrier = "DefaultDateTime(\"1970-01-01T00:00:00.1234560\"";
        if (provider.DatabaseType == DatabaseType.MySQL)
        {
            await Assert.That(attribute).IsTypeOf<DefaultSqlAttribute>();
            expectedCarrier = "DefaultSql(DatabaseType.MySQL";
        }
        else
        {
            var date = (DateTime)attribute.Value;
            await Assert.That(date).IsEqualTo(expected);
            await Assert.That(date.Kind).IsEqualTo(DateTimeKind.Unspecified);
            var sql = DataLinq.MySql.SqlFromMetadataFactory.GetFactoryFromDatabaseType(provider.DatabaseType)
                .GetDefaultValue(imported.TableModels.Single().Table.Columns.Single(x => x.DbName == "changed_at"));
            await Assert.That(sql).IsEqualTo("'1970-01-01 00:00:00.123456'");
        }
        var factory = new ModelFileFactory(new ModelFileFactoryOptions());
        var files = factory.CreateModelFiles(imported).ToArray();
        await Assert.That(string.Join("\n", files.Select(x => x.contents))).Contains(expectedCarrier);
        var parsed = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions())
            .ReadSyntaxTrees(files.SelectMany(x => CSharpSyntaxTree.ParseText(x.contents).GetRoot().DescendantNodes())
                .OfType<TypeDeclarationSyntax>().ToImmutableArray()).Single().ValueOrException();
        var merged = new MetadataTransformer(new MetadataTransformerOptions()).TransformDatabaseSnapshot(parsed, imported);
        await Assert.That(string.Join("\n", factory.CreateModelFiles(merged).Select(x => x.contents)))
            .Contains(expectedCarrier);
    }
}
