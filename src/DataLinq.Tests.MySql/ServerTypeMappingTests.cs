using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using DataLinq.Testing;

namespace DataLinq.Tests.MySql;

public class ServerTypeMappingTests
{
    public sealed record TypeMappingScenario(
        TestProviderDescriptor Provider,
        string ColumnDefinition,
        string ExpectedClrType,
        bool ExpectNullable = false,
        string? ExpectedDbType = null,
        bool ExpectEnum = false);

    public sealed record NumericDefaultScenario(
        TestProviderDescriptor Provider,
        string ColumnType,
        Type ExpectedValueType,
        object ExpectedValue,
        string ExpectedGeneratedAttribute);

    public static IEnumerable<Func<TypeMappingScenario>> IntegerTypeCases()
    {
        var definitions = new[]
        {
            new TypeMappingScenario(default!, "TINYINT", "sbyte", ExpectedDbType: "tinyint"),
            new TypeMappingScenario(default!, "TINYINT UNSIGNED", "byte", ExpectedDbType: "tinyint"),
            new TypeMappingScenario(default!, "SMALLINT", "short", ExpectedDbType: "smallint"),
            new TypeMappingScenario(default!, "SMALLINT UNSIGNED", "ushort", ExpectedDbType: "smallint"),
            new TypeMappingScenario(default!, "MEDIUMINT", "int", ExpectedDbType: "mediumint"),
            new TypeMappingScenario(default!, "MEDIUMINT UNSIGNED", "uint", ExpectedDbType: "mediumint"),
            new TypeMappingScenario(default!, "INT", "int", ExpectedDbType: "int"),
            new TypeMappingScenario(default!, "INT UNSIGNED", "uint", ExpectedDbType: "int"),
            new TypeMappingScenario(default!, "BIGINT", "long", ExpectedDbType: "bigint"),
            new TypeMappingScenario(default!, "BIGINT UNSIGNED", "ulong", ExpectedDbType: "bigint")
        };

        return Expand(definitions);
    }

    public static IEnumerable<Func<TypeMappingScenario>> FloatingAndTemporalCases()
    {
        var definitions = new[]
        {
            new TypeMappingScenario(default!, "FLOAT", "float", ExpectedDbType: "float"),
            new TypeMappingScenario(default!, "DOUBLE", "double", ExpectedDbType: "double"),
            new TypeMappingScenario(default!, "DECIMAL(10, 2)", "decimal", ExpectedDbType: "decimal"),
            new TypeMappingScenario(default!, "DATE", "DateOnly", ExpectedDbType: "date"),
            new TypeMappingScenario(default!, "DATETIME", "DateTime", ExpectedDbType: "datetime"),
            new TypeMappingScenario(default!, "TIMESTAMP", "DateTime", ExpectedDbType: "timestamp"),
            new TypeMappingScenario(default!, "TIME", "TimeOnly", ExpectedDbType: "time"),
            new TypeMappingScenario(default!, "YEAR", "int", ExpectedDbType: "year")
        };

        return Expand(definitions);
    }

    public static IEnumerable<Func<TypeMappingScenario>> StringBinaryAndSpecialCases()
    {
        var definitions = new[]
        {
            new TypeMappingScenario(default!, "CHAR(10)", "string", ExpectedDbType: "char"),
            new TypeMappingScenario(default!, "VARCHAR(255)", "string", ExpectedDbType: "varchar"),
            new TypeMappingScenario(default!, "TEXT", "string", ExpectedDbType: "text"),
            new TypeMappingScenario(default!, "MEDIUMTEXT", "string", ExpectedDbType: "mediumtext"),
            new TypeMappingScenario(default!, "BINARY(16)", "Guid", ExpectedDbType: "binary"),
            new TypeMappingScenario(default!, "VARBINARY(100)", "byte[]", ExpectedDbType: "varbinary"),
            new TypeMappingScenario(default!, "BLOB", "byte[]", ExpectedDbType: "blob"),
            new TypeMappingScenario(default!, "BIT(1)", "bool", ExpectedDbType: "bit"),
            new TypeMappingScenario(default!, "ENUM('a', 'b')", "enum", ExpectedDbType: "enum", ExpectEnum: true)
        };

        return Expand(definitions);
    }

    public static IEnumerable<Func<TypeMappingScenario>> NullableCases()
    {
        var definitions = new[]
        {
            new TypeMappingScenario(default!, "INT NULL", "int", true, "int"),
            new TypeMappingScenario(default!, "VARCHAR(50) NULL", "string", true, "varchar"),
            new TypeMappingScenario(default!, "DATETIME NULL", "DateTime", true, "datetime")
        };

        return Expand(definitions);
    }

    public static IEnumerable<Func<NumericDefaultScenario>> QuotedNumericDefaultCases()
    {
        foreach (var providerFactory in TestProviderDataSources.ActiveServerProviders())
        {
            var provider = providerFactory();
            yield return () => new NumericDefaultScenario(provider with { }, "INT", typeof(int), 0, "[Default(0)]");
            yield return () => new NumericDefaultScenario(provider with { }, "BIGINT", typeof(long), 0L, "[Default(0L)]");
            yield return () => new NumericDefaultScenario(provider with { }, "DECIMAL(10, 2)", typeof(decimal), 0m, "[Default(0.00M)]");
        }
    }

    [Test]
    [MethodDataSource(nameof(IntegerTypeCases))]
    public async Task ParseColumn_MapsIntegerTypes(TypeMappingScenario scenario)
    {
        var column = ParseSingleColumn(scenario.Provider, nameof(ParseColumn_MapsIntegerTypes), scenario.ColumnDefinition);

        await AssertTypeMapping(column, scenario);
    }

    [Test]
    [MethodDataSource(nameof(FloatingAndTemporalCases))]
    public async Task ParseColumn_MapsFloatingPointAndTemporalTypes(TypeMappingScenario scenario)
    {
        var column = ParseSingleColumn(scenario.Provider, nameof(ParseColumn_MapsFloatingPointAndTemporalTypes), scenario.ColumnDefinition);

        await AssertTypeMapping(column, scenario);
    }

    [Test]
    [MethodDataSource(nameof(StringBinaryAndSpecialCases))]
    public async Task ParseColumn_MapsStringBinaryAndSpecialTypes(TypeMappingScenario scenario)
    {
        var column = ParseSingleColumn(scenario.Provider, nameof(ParseColumn_MapsStringBinaryAndSpecialTypes), scenario.ColumnDefinition);

        await AssertTypeMapping(column, scenario);
    }

    [Test]
    [MethodDataSource(nameof(NullableCases))]
    public async Task ParseColumn_MapsNullableTypes(TypeMappingScenario scenario)
    {
        var column = ParseSingleColumn(scenario.Provider, nameof(ParseColumn_MapsNullableTypes), scenario.ColumnDefinition);

        await AssertTypeMapping(column, scenario);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_StringDefaultWithEmbeddedDoubleQuotes_GeneratesCleanCSharpDefault(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_StringDefaultWithEmbeddedDoubleQuotes_GeneratesCleanCSharpDefault),
            """
            CREATE TABLE default_string_quotes_test (
                id INT PRIMARY KEY AUTO_INCREMENT,
                quote_text VARCHAR(355) NOT NULL DEFAULT '""'
            );
            """);

        var database = schema.ParseDatabase(
            "TestDb",
            "TestDb",
            "TestNamespace",
            new MetadataFromDatabaseFactoryOptions { Include = ["default_string_quotes_test"], CapitaliseNames = true });

        var property = database.TableModels.Single().Model.ValueProperties["QuoteText"];
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "DefaultStringQuotesTest.cs");

        await Assert.That(property.GetDefaultAttribute()).IsTypeOf<DefaultAttribute>();
        await Assert.That(((DefaultAttribute)property.GetDefaultAttribute()!).Value).IsEqualTo("\"\"");
        await Assert.That(generatedFile.contents.Contains("[Default(\"\\\"\\\"\")]", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generatedFile.contents.Contains("[Default(\"'\\\"\\\"'\")]", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [MethodDataSource(nameof(QuotedNumericDefaultCases))]
    public async Task ParseDatabase_QuotedNumericDefaults_AreTypedAndGeneratedWithoutStringLiterals(NumericDefaultScenario scenario)
    {
        using var schema = ServerSchemaDatabase.Create(
            scenario.Provider,
            nameof(ParseDatabase_QuotedNumericDefaults_AreTypedAndGeneratedWithoutStringLiterals),
            $"""
            CREATE TABLE quoted_numeric_default_test (
                id INT PRIMARY KEY AUTO_INCREMENT,
                typed_default {scenario.ColumnType} NOT NULL DEFAULT '0'
            );
            """);

        var database = schema.ParseDatabase(
            "TestDb",
            "TestDb",
            "TestNamespace",
            new MetadataFromDatabaseFactoryOptions { Include = ["quoted_numeric_default_test"], CapitaliseNames = true });

        var property = database.TableModels.Single().Model.ValueProperties["TypedDefault"];
        var defaultAttribute = property.GetDefaultAttribute();
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "QuotedNumericDefaultTest.cs");

        await Assert.That(defaultAttribute).IsTypeOf<DefaultAttribute>();
        await Assert.That(((DefaultAttribute)defaultAttribute!).Value!.GetType()).IsEqualTo(scenario.ExpectedValueType);
        await Assert.That(((DefaultAttribute)defaultAttribute).Value).IsEqualTo(scenario.ExpectedValue);
        await Assert.That(generatedFile.contents.Contains(scenario.ExpectedGeneratedAttribute, StringComparison.Ordinal)).IsTrue();
        await Assert.That(generatedFile.contents.Contains("[Default(\"0\")]", StringComparison.Ordinal)).IsFalse();
        await Assert.That(generatedFile.contents.Contains("[Default(\"'0'\")]", StringComparison.Ordinal)).IsFalse();
    }

    private static IEnumerable<Func<TypeMappingScenario>> Expand(IEnumerable<TypeMappingScenario> definitions)
    {
        foreach (var providerFactory in TestProviderDataSources.ActiveServerProviders())
        {
            var provider = providerFactory();
            foreach (var definition in definitions)
            {
                var current = definition;
                yield return () => current with { Provider = provider with { } };
            }
        }
    }

    private static ColumnDefinition ParseSingleColumn(TestProviderDescriptor provider, string scenarioName, string columnDefinition)
    {
        var nullabilityClause = columnDefinition.Contains(" NULL", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : " NOT NULL";

        using var schema = ServerSchemaDatabase.Create(
            provider,
            scenarioName,
            $"""
            CREATE TABLE type_test_table (
                id INT PRIMARY KEY AUTO_INCREMENT,
                test_column {columnDefinition}{nullabilityClause}
            );
            """);

        var database = schema.ParseDatabase(
            "TestDb",
            "TestDb",
            "TestNamespace",
            new MetadataFromDatabaseFactoryOptions { Include = ["type_test_table"], CapitaliseNames = true });

        return database.TableModels.Single().Table.Columns.Single(column => column.DbName == "test_column");
    }

    private static async Task AssertTypeMapping(ColumnDefinition column, TypeMappingScenario scenario)
    {
        if (scenario.ExpectEnum)
        {
            await Assert.That(column.ValueProperty.EnumProperty).IsNotNull();
            await Assert.That(column.ValueProperty.EnumProperty!.Value.DbEnumValues.Count).IsEqualTo(2);
            await Assert.That(column.ValueProperty.EnumProperty!.Value.DbEnumValues[0].name).IsEqualTo("a");
            await Assert.That(column.ValueProperty.EnumProperty!.Value.DbEnumValues[1].name).IsEqualTo("b");
        }
        else
        {
            await Assert.That(column.ValueProperty.CsType.Name).IsEqualTo(scenario.ExpectedClrType);
            await Assert.That(column.ValueProperty.CsNullable).IsEqualTo(scenario.ExpectNullable);
        }

        await Assert.That(column.Nullable).IsEqualTo(scenario.ExpectNullable);

        if (scenario.ExpectedDbType is not null)
            await Assert.That(column.GetDbTypeFor(scenario.Provider.DatabaseType)!.Name).IsEqualTo(scenario.ExpectedDbType);
    }
}
