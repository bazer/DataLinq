using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using DataLinq.PlatformCompatibility.Smoke;
using DataLinq.Tests.Models.Allround;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class MetadataEquivalenceTests
{
    [Test]
    public async Task SourceParsedAndGeneratedRuntimeMetadata_AreEquivalentForRepresentativeModels()
    {
        var repositoryRoot = RepositoryLayout.FindRepositoryRoot();

        await AssertMetadataEquivalent(
            typeof(EmployeesDb),
            Path.Combine(repositoryRoot, "src", "DataLinq.Tests.Models", "employees"));
        await AssertMetadataEquivalent(
            typeof(AllroundBenchmark),
            Path.Combine(repositoryRoot, "src", "DataLinq.Tests.Models", "Allround"));
        await AssertMetadataEquivalent(
            typeof(PlatformSmokeDb),
            Path.Combine(repositoryRoot, "src", "DataLinq.PlatformCompatibility.Smoke"));
    }

    private static async Task AssertMetadataEquivalent(Type databaseType, string sourcePath)
    {
        var sourceMetadata = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions())
            .ReadFiles(databaseType.Name, [sourcePath])
            .ValueOrException()
            .Single(database => database.CsType.Name == databaseType.Name);
        var generatedMetadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(databaseType).ValueOrException();

        var sourceDigest = MetadataDigest.Create(sourceMetadata);
        var generatedDigest = MetadataDigest.Create(generatedMetadata);

        await Assert.That(string.Join(Environment.NewLine, generatedDigest))
            .IsEqualTo(string.Join(Environment.NewLine, sourceDigest));
    }

    private static class MetadataDigest
    {
        public static string[] Create(DatabaseDefinition database)
        {
            var lines = new List<string>
            {
                $"database|name={database.Name}|db={database.DbName}|cs={Format(database.CsType)}|cache={database.UseCache}",
                $"database-cache-limits|{string.Join(",", database.CacheLimits.OrderBy(x => x.limitType).ThenBy(x => x.amount).Select(x => $"{x.limitType}:{x.amount}"))}",
                $"database-cache-cleanup|{string.Join(",", database.CacheCleanup.OrderBy(x => x.cleanupType).ThenBy(x => x.amount).Select(x => $"{x.cleanupType}:{x.amount}"))}",
                $"database-index-cache|{string.Join(",", database.IndexCache.OrderBy(x => x.indexCacheType).ThenBy(x => x.amount).Select(x => $"{x.indexCacheType}:{x.amount}"))}",
            };

            foreach (var tableModel in database.TableModels.OrderBy(x => x.Table.DbName, StringComparer.Ordinal))
            {
                lines.AddRange(CreateTableDigest(tableModel));
            }

            return lines.ToArray();
        }

        private static IEnumerable<string> CreateTableDigest(TableModel tableModel)
        {
            var table = tableModel.Table;
            var model = tableModel.Model;

            yield return $"table|{table.DbName}|type={table.Type}|model={Format(model.CsType)}|property={tableModel.CsPropertyName}|interface={model.ModelInstanceInterface?.Name}|cache={table.UseCache}";

            foreach (var column in table.Columns.OrderBy(x => x.Index).ThenBy(x => x.DbName, StringComparer.Ordinal))
            {
                var property = column.ValueProperty;
                yield return $"column|{table.DbName}.{column.Index}.{column.DbName}|property={property.PropertyName}|type={property.CsType.Name}|nullable={property.CsNullable}|dbNullable={column.Nullable}|pk={column.PrimaryKey}|fk={column.ForeignKey}|auto={column.AutoIncrement}|dbTypes={FormatDbTypes(column)}|enum={FormatEnum(property)}";
            }

            foreach (var index in table.ColumnIndices.OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Characteristic).ThenBy(x => x.Type))
            {
                yield return $"index|{table.DbName}.{index.Name}|characteristic={index.Characteristic}|type={index.Type}|columns={string.Join(",", index.Columns.Select(x => x.DbName))}";
            }

            foreach (var relation in model.RelationProperties.Values.OrderBy(x => x.PropertyName, StringComparer.Ordinal))
            {
                var relationPart = relation.RelationPart;
                yield return $"relation-property|{table.DbName}.{relation.PropertyName}|type={relation.CsType.Name}|nullable={relation.CsNullable}|part={relationPart?.Type}|constraint={relationPart?.Relation.ConstraintName}|index={relationPart?.ColumnIndex.Name}|columns={FormatRelationColumns(relationPart)}";
            }
        }

        private static string Format(CsTypeDeclaration declaration) =>
            string.IsNullOrWhiteSpace(declaration.Namespace)
                ? declaration.Name
                : $"{declaration.Namespace}.{declaration.Name}";

        private static string FormatDbTypes(ColumnDefinition column) =>
            string.Join(
                ",",
                column.DbTypes
                    .OrderBy(x => x.DatabaseType)
                    .ThenBy(x => x.Name, StringComparer.Ordinal)
                    .Select(x => $"{x.DatabaseType}:{x.Name}:{x.Length}:{x.Decimals}:{x.Signed}"));

        private static string FormatEnum(ValueProperty property) =>
            property.EnumProperty.HasValue
                ? string.Join(",", property.EnumProperty.Value.CsEnumValues.Select(x => $"{x.name}:{x.value}"))
                : "";

        private static string FormatRelationColumns(RelationPart? relationPart) =>
            relationPart == null
                ? ""
                : string.Join(",", relationPart.ColumnIndex.Columns.Select(x => x.DbName));
    }
}
