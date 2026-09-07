using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.MySql;
using DataLinq.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MySqlConnector;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class EnumLiteralTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ValuesAndOrdinalsSurviveParsingCodeGenerationAndBothSqlModes(TestProviderDescriptor provider)
    {
        var labels = new[] { "can't", "fine", "a\\b", "with,comma", "", "class", "a-b", "a_b", "a b", "1st", "statusValue", "value__", "line\nbreak", "雪", "'quoted'", "quoted", "('fine')", "NULL" };
        var originalLiterals = string.Join(",", labels.Select(label => "'" + label.Replace("\\", "\\\\").Replace("'", "''") + "'"));
        using var schema = ServerSchemaDatabase.Create(provider, nameof(ValuesAndOrdinalsSurviveParsingCodeGenerationAndBothSqlModes),
            $"CREATE TABLE enum_rows (id INT PRIMARY KEY AUTO_INCREMENT, status ENUM({originalLiterals}) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL DEFAULT 'fine')");
        var metadata = schema.ParseDatabase("EnumDb", "EnumDb", "GeneratedEnums", new MetadataFromDatabaseFactoryOptions { CapitaliseNames = false });
        var column = metadata.TableModels.Single().Table.GetColumnByDbName("status");
        var members = column.ValueProperty.EnumProperty!.Value;
        await Assert.That(members.DbValuesOrCsValues.Select(member => member.name).SequenceEqual(labels)).IsTrue();
        await Assert.That(members.DbValuesOrCsValues.Select(member => member.value).SequenceEqual(Enumerable.Range(1, labels.Length))).IsTrue();
        await Assert.That(members.CsValuesOrDbValues.Select(member => member.name).Distinct().Count()).IsEqualTo(labels.Length);
        await Assert.That(column.ValueProperty.GetDefaultAttribute()!.Value).IsEqualTo((object)2);

        var modelTrees = new ModelFileFactory(new ModelFileFactoryOptions()).CreateModelFiles(metadata)
            .Select(file => CSharpSyntaxTree.ParseText(file.contents)).ToArray();
        var modelTree = modelTrees.Single(tree => tree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>().Any());
        var enumDeclaration = modelTree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>().Single();
        var compilation = CSharpCompilation.Create("GeneratedEnum",
            [CSharpSyntaxTree.ParseText(enumDeclaration.ToFullString())],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        await Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
        var enumAttribute = modelTree.GetRoot().DescendantNodes().OfType<AttributeSyntax>().Single(attribute => attribute.Name.ToString() == "Enum");
        var emittedLabels = enumAttribute.ArgumentList!.Arguments.Select(argument => ((LiteralExpressionSyntax)argument.Expression).Token.ValueText);
        await Assert.That(emittedLabels.SequenceEqual(labels)).IsTrue();

        using var connection = new MySqlConnection(schema.Connection.ConnectionString);
        connection.Open();
        for (var mode = 0; mode < 2; mode++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = mode == 0 ? "SET SESSION sql_mode = ''" : "SET SESSION sql_mode = 'NO_BACKSLASH_ESCAPES'";
            command.ExecuteNonQuery();
            command.CommandText = "DROP TABLE enum_rows";
            command.ExecuteNonQuery();
            var factory = SqlFromMetadataFactory.GetFactoryFromDatabaseType(provider.DatabaseType);
            factory.NoBackslashEscapes = mode == 1;
            command.CommandText = factory.GetCreateTables(metadata, foreignKeyRestrict: false).ValueOrException().Text;
            command.ExecuteNonQuery();

            command.CommandText = "INSERT INTO enum_rows (status) VALUES " + string.Join(",", Enumerable.Range(1, labels.Length).Select(ordinal => $"({ordinal})"));
            command.ExecuteNonQuery();
            command.CommandText = "SELECT status, status + 0 FROM enum_rows ORDER BY id";
            using (var reader = command.ExecuteReader())
            {
                for (var index = 0; index < labels.Length; index++)
                {
                    await Assert.That(reader.Read()).IsTrue();
                    await Assert.That(reader.GetString(0)).IsEqualTo(labels[index]);
                    await Assert.That(Convert.ToInt32(reader.GetValue(1))).IsEqualTo(index + 1);
                }
                await Assert.That(reader.Read()).IsFalse();
            }
            command.CommandText = "INSERT INTO enum_rows () VALUES (); SELECT status + 0 FROM enum_rows ORDER BY id DESC LIMIT 1";
            await Assert.That(Convert.ToInt32(command.ExecuteScalar())).IsEqualTo(2);

            // Both session modes expose the same canonical COLUMN_TYPE representation.
            command.CommandText = "SELECT COLUMN_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'enum_rows' AND COLUMN_NAME = 'status'";
            await Assert.That((string)command.ExecuteScalar()!).Contains("'a\\\\b'");
            var reparsed = schema.ParseDatabase("EnumDb", "EnumDb", "GeneratedEnums");
            var reparsedMembers = reparsed.TableModels.Single().Table.GetColumnByDbName("status").ValueProperty.EnumProperty!.Value;
            await Assert.That(reparsedMembers.DbValuesOrCsValues.Select(member => member.name).SequenceEqual(labels)).IsTrue();

            foreach (var index in new[] { 0, 2, 4, 14, 16, 17 })
            {
                var label = mode == 0 ? labels[index].Replace("\\", "\\\\") : labels[index];
                var literal = "'" + label.Replace("'", "''") + "'";
                command.CommandText = "ALTER TABLE enum_rows ALTER COLUMN status SET DEFAULT " + literal;
                command.ExecuteNonQuery();
                var defaultMetadata = schema.ParseDatabase("EnumDb", "EnumDb", "GeneratedEnums");
                var defaultValue = defaultMetadata.TableModels.Single().Table.GetColumnByDbName("status").ValueProperty.GetDefaultAttribute()?.Value;
                await Assert.That(defaultValue).IsEqualTo((object)(index + 1));
            }
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task RuntimeEnumMappingKeepsDatabaseLabelsAndNumericIdentity(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<EnumRuntimeDatabase>.Create(provider, nameof(RuntimeEnumMappingKeepsDatabaseLabelsAndNumericIdentity));
        var values = Enum.GetValues<EnumRuntimeStatus>();
        for (var index = 0; index < values.Length; index++)
            scope.Database.Insert(new MutableEnumRuntimeRow { Id = index + 1, Status = values[index] });

        scope.Database.Provider.State.ClearCache();
        var rows = scope.Database.Query().Rows.OrderBy(row => row.Id).ToArray();
        await Assert.That(rows.Select(row => row.Status).SequenceEqual(values)).IsTrue();
        await Assert.That(scope.Database.Provider.DatabaseAccess.ExecuteScalar<string>("SELECT status FROM enum_runtime_rows WHERE id = 1")).IsEqualTo("can't");
        await Assert.That(scope.Database.Provider.DatabaseAccess.ExecuteScalar<string>("SELECT status FROM enum_runtime_rows WHERE id = 3")).IsEqualTo("a\\b");
    }
}

public partial class EnumRuntimeDatabase(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<EnumRuntimeRow> Rows { get; } = new(source);
}

public enum EnumRuntimeStatus { Quoted = 1, Fine = 2, Backslash = 3, Empty = 4 }

[Table("enum_runtime_rows")]
public abstract partial class EnumRuntimeRow(IRowData data, IDataSourceAccess source)
    : Immutable<EnumRuntimeRow, EnumRuntimeDatabase>(data, source), ITableModel<EnumRuntimeDatabase>
{
    [PrimaryKey, Column("id"), Type(DatabaseType.MySQL, "int"), Type(DatabaseType.MariaDB, "int")]
    public abstract int Id { get; }

    [Column("status"), Enum("can't", "fine", "a\\b", ""), Type(DatabaseType.MySQL, "enum"), Type(DatabaseType.MariaDB, "enum")]
    public abstract EnumRuntimeStatus Status { get; }
}
