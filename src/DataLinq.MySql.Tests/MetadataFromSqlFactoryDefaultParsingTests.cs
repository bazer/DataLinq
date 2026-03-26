using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.MySql.Shared;
using ThrowAway;
using Xunit;

namespace DataLinq.MySql.Tests;

public class MetadataFromSqlFactoryDefaultParsingTests
{
    private sealed class TestMetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options)
        : MetadataFromSqlFactory(options, DatabaseType.MariaDB)
    {
        public DefaultAttribute? ParseDefaultForTest(TableDefinition table, ICOLUMNS column, ValueProperty property) =>
            ParseDefaultValue(table, column, property);

        public override Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString) =>
            throw new NotImplementedException();
    }

    private sealed class TestColumn(
        string columnName,
        string dataType,
        string columnType,
        string columnDefault) : ICOLUMNS
    {
        public string TABLE_SCHEMA => "test_db";
        public string TABLE_NAME => "test_table";
        public string DATA_TYPE => dataType;
        public string COLUMN_TYPE => columnType;
        public ulong? NUMERIC_PRECISION => null;
        public ulong? NUMERIC_SCALE => null;
        public ulong? CHARACTER_MAXIMUM_LENGTH => null;
        public string IS_NULLABLE => "NO";
        public COLUMN_KEY COLUMN_KEY => COLUMN_KEY.Empty;
        public string EXTRA => string.Empty;
        public string COLUMN_DEFAULT => columnDefault;
        public string COLUMN_NAME => columnName;
    }

    [Theory]
    [InlineData("CURRENT_DATE", "DateOnly", "CURRENT_DATE")]
    [InlineData("CURRENT_TIME", "TimeOnly", "CURRENT_TIME")]
    [InlineData("NOW()", "DateTime", "CURRENT_TIMESTAMP")]
    [InlineData("(CURRENT_TIMESTAMP)", "DateTime", "CURRENT_TIMESTAMP")]
    public void ParseDefaultValue_TemporalAliases_MapToDynamicDefault(string columnDefault, string csTypeName, string expectedSqlDefault)
    {
        var factory = new TestMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions());
        var (table, column, property) = CreateValueProperty("test_col", csTypeName);
        var dbColumn = new TestColumn("test_col", "varchar", "varchar", columnDefault);

        var defaultAttr = factory.ParseDefaultForTest(table, dbColumn, property);

        var currentTimestamp = Assert.IsType<DefaultCurrentTimestampAttribute>(defaultAttr);
        property.AddAttribute(currentTimestamp);

        var sqlDefault = SqlFromMetadataFactory.GetFactoryFromDatabaseType(DatabaseType.MariaDB).GetDefaultValue(column);
        Assert.Equal(expectedSqlDefault, sqlDefault);
    }

    [Fact]
    public void ParseDefaultValue_ParenthesizedNumericLiteral_IsUnwrappedAndTyped()
    {
        var factory = new TestMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions());
        var (table, _, property) = CreateValueProperty("test_col", "int");
        var dbColumn = new TestColumn("test_col", "int", "int", "(0)");

        var defaultAttr = factory.ParseDefaultForTest(table, dbColumn, property);

        defaultAttr = Assert.IsType<DefaultAttribute>(defaultAttr);
        Assert.IsType<int>(defaultAttr.Value);
        Assert.Equal(0, defaultAttr.Value);
    }

    [Fact]
    public void ParseDefaultValue_ParenthesizedStringLiteral_IsUnwrapped()
    {
        var factory = new TestMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions());
        var (table, _, property) = CreateValueProperty("test_col", "string");
        var dbColumn = new TestColumn("test_col", "varchar", "varchar(32)", "('abc')");

        var defaultAttr = factory.ParseDefaultForTest(table, dbColumn, property);

        defaultAttr = Assert.IsType<DefaultAttribute>(defaultAttr);
        Assert.Equal("abc", defaultAttr.Value);
    }

    [Theory]
    [InlineData("'0000-00-00'", "DateOnly")]
    [InlineData("'0000-00-00 00:00:00'", "DateTime")]
    public void ParseDefaultValue_ZeroDates_WarnAndSkipDefault(string columnDefault, string csTypeName)
    {
        var warnings = new List<string>();
        var factory = new TestMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions { Log = warnings.Add });
        var (table, _, property) = CreateValueProperty("test_col", csTypeName);
        var dbColumn = new TestColumn("test_col", "date", "date", columnDefault);

        var defaultAttr = factory.ParseDefaultForTest(table, dbColumn, property);

        Assert.Null(defaultAttr);
        Assert.Single(warnings);
        Assert.Contains("Skipping unsupported zero date default", warnings[0]);
        Assert.Contains("test_table.test_col", warnings[0]);
    }

    [Fact]
    public void ParseDefaultValue_EnumLabelDefault_MapsToEnumValueAndRoundTripsToSqlLabel()
    {
        var factory = new TestMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions());
        var enumProperty = new EnumProperty(
            enumValues: [("standard", 1), ("premium", 2)],
            csEnumValues: [("Standard", 1), ("Premium", 2)],
            declaredInClass: false);
        var (table, column, property) = CreateEnumProperty("tier", "TierValue", enumProperty);
        var dbColumn = new TestColumn("tier", "enum", "enum('standard','premium')", "'premium'");

        var defaultAttr = factory.ParseDefaultForTest(table, dbColumn, property);

        defaultAttr = Assert.IsType<DefaultAttribute>(defaultAttr);
        Assert.IsType<int>(defaultAttr.Value);
        Assert.Equal(2, defaultAttr.Value);

        property.AddAttribute(defaultAttr);
        Assert.Equal("TierValue.Premium", property.GetDefaultValueCode());

        var sqlDefault = SqlFromMetadataFactory.GetFactoryFromDatabaseType(DatabaseType.MariaDB).GetDefaultValue(column);
        Assert.Equal("'premium'", sqlDefault);
    }

    private static (TableDefinition table, ColumnDefinition column, ValueProperty property) CreateValueProperty(string columnName, string csTypeName)
    {
        var csType = csTypeName switch
        {
            "int" => new CsTypeDeclaration(typeof(int)),
            "string" => new CsTypeDeclaration(typeof(string)),
            "DateOnly" => new CsTypeDeclaration(typeof(DateOnly)),
            "TimeOnly" => new CsTypeDeclaration(typeof(TimeOnly)),
            "DateTime" => new CsTypeDeclaration(typeof(DateTime)),
            _ => throw new NotImplementedException($"Unsupported test type {csTypeName}")
        };

        return CreateProperty(columnName, csType);
    }

    private static (TableDefinition table, ColumnDefinition column, ValueProperty property) CreateEnumProperty(string columnName, string enumTypeName, EnumProperty enumProperty)
    {
        var csType = new CsTypeDeclaration(enumTypeName, "TestNamespace", ModelCsType.Enum);
        var result = CreateProperty(columnName, csType);
        result.property.SetEnumProperty(enumProperty);
        return result;
    }

    private static (TableDefinition table, ColumnDefinition column, ValueProperty property) CreateProperty(string columnName, CsTypeDeclaration csType)
    {
        var database = new DatabaseDefinition("TestDb", new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var model = new ModelDefinition(new CsTypeDeclaration("TestModel", "TestNamespace", ModelCsType.Class));
        var table = new TableDefinition("test_table");
        var tableModel = new TableModel("TestModels", database, model, table);
        database.SetTableModels([tableModel]);

        var property = new ValueProperty("TestProperty", csType, model, [new ColumnAttribute(columnName)]);
        var column = new ColumnDefinition(columnName, table);
        column.SetValueProperty(property);
        table.SetColumns([column]);
        model.AddProperty(property);

        return (table, column, property);
    }
}
