using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Shared;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class MetadataFromSqlFactoryDefaultParsingTests
{
    public sealed record TemporalAliasCase(string ColumnDefault, string CsTypeName, string ExpectedSqlDefault);
    public sealed record ZeroDateCase(string ColumnDefault, string CsTypeName);
    public sealed record SqlLiteralFormattingCase(CsTypeDeclaration CsType, object DefaultValue, string ExpectedSqlDefault);

    private sealed class TestMetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options)
        : MetadataFromSqlFactory(options, DataLinq.DatabaseType.MariaDB)
    {
        public DefaultAttribute? ParseDefaultForTest(TableDefinition table, ICOLUMNS column, ValueProperty property)
        {
            var draftColumn = new ProviderColumnDraft(column.COLUMN_NAME);
            var draftProperty = new ProviderValuePropertyDraft(
                property.PropertyName,
                property.CsType,
                draftColumn,
                property.Attributes.ToList())
            {
                CsNullable = property.CsNullable,
                CsSize = property.CsSize,
                EnumProperty = property.EnumProperty
            };

            return ParseDefaultValue(new ProviderTableDraft(table.DbName, table.Type), column, draftProperty);
        }

        public override ThrowAway.Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString) =>
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
        public string? GENERATION_EXPRESSION => null;
        public string COLUMN_DEFAULT => columnDefault;
        public string COLUMN_COMMENT => string.Empty;
        public string COLUMN_NAME => columnName;
    }

    public static IEnumerable<Func<TemporalAliasCase>> TemporalAliasCases()
    {
        yield return () => new TemporalAliasCase("CURRENT_DATE", "DateOnly", "CURRENT_DATE");
        yield return () => new TemporalAliasCase("CURRENT_TIME", "TimeOnly", "CURRENT_TIME");
        yield return () => new TemporalAliasCase("NOW()", "DateTime", "CURRENT_TIMESTAMP");
        yield return () => new TemporalAliasCase("(CURRENT_TIMESTAMP)", "DateTime", "CURRENT_TIMESTAMP");
    }

    public static IEnumerable<Func<ZeroDateCase>> ZeroDateCases()
    {
        yield return () => new ZeroDateCase("'0000-00-00'", "DateOnly");
        yield return () => new ZeroDateCase("'0000-00-00 00:00:00'", "DateTime");
    }

    public static IEnumerable<Func<SqlLiteralFormattingCase>> SqlLiteralFormattingCases()
    {
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(string)), "abc", "'abc'");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(string)), "O'Reilly", "'O''Reilly'");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(string)), "\"\"", "'\"\"'");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(char)), '\'', "''''");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(bool)), true, "b'1'");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(bool)), false, "b'0'");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(int)), 12, "12");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(long)), 12L, "12");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(decimal)), 12.50m, "12.50");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(float)), 1.5f, "1.5");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(double)), 1.5d, "1.5");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(DateOnly)), new DateOnly(2024, 1, 2), "'2024-01-02'");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(TimeOnly)), new TimeOnly(12, 34, 56), "'12:34:56'");
        yield return () => new SqlLiteralFormattingCase(new CsTypeDeclaration(typeof(DateTime)), new DateTime(2024, 1, 2, 3, 4, 5), "'2024-01-02 03:04:05'");
    }

    [Test]
    [MethodDataSource(nameof(TemporalAliasCases))]
    public async Task ParseDefaultValue_TemporalAliases_MapToDynamicDefault(TemporalAliasCase testCase)
    {
        var factory = new TestMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions());
        var (table, column, property) = CreateValueProperty("test_col", testCase.CsTypeName);
        var dbColumn = new TestColumn("test_col", "varchar", "varchar", testCase.ColumnDefault);

        var defaultAttr = factory.ParseDefaultForTest(table, dbColumn, property);

        await Assert.That(defaultAttr).IsTypeOf<DefaultCurrentTimestampAttribute>();

        var (_, columnWithDefault, _) = CreateValueProperty(
            "test_col",
            testCase.CsTypeName,
            [(DefaultCurrentTimestampAttribute)defaultAttr!]);
        var sqlDefault = SqlFromMetadataFactory.GetFactoryFromDatabaseType(DataLinq.DatabaseType.MariaDB).GetDefaultValue(columnWithDefault);

        await Assert.That(sqlDefault).IsEqualTo(testCase.ExpectedSqlDefault);
    }

    [Test]
    public async Task ParseDefaultValue_ParenthesizedNumericLiteral_IsUnwrappedAndTyped()
    {
        var factory = new TestMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions());
        var (table, _, property) = CreateValueProperty("test_col", "int");
        var dbColumn = new TestColumn("test_col", "int", "int", "(0)");

        var defaultAttr = factory.ParseDefaultForTest(table, dbColumn, property);

        await Assert.That(defaultAttr).IsTypeOf<DefaultAttribute>();
        await Assert.That(((DefaultAttribute)defaultAttr!).Value).IsTypeOf<int>();
        await Assert.That((int)((DefaultAttribute)defaultAttr).Value).IsEqualTo(0);
    }

    [Test]
    public async Task ParseDefaultValue_ParenthesizedStringLiteral_IsUnwrapped()
    {
        var factory = new TestMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions());
        var (table, _, property) = CreateValueProperty("test_col", "string");
        var dbColumn = new TestColumn("test_col", "varchar", "varchar(32)", "('abc')");

        var defaultAttr = factory.ParseDefaultForTest(table, dbColumn, property);

        await Assert.That(defaultAttr).IsTypeOf<DefaultAttribute>();
        await Assert.That((string)((DefaultAttribute)defaultAttr!).Value).IsEqualTo("abc");
    }

    [Test]
    [MethodDataSource(nameof(ZeroDateCases))]
    public async Task ParseDefaultValue_ZeroDates_WarnAndSkipDefault(ZeroDateCase testCase)
    {
        var warnings = new List<string>();
        var factory = new TestMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions { Log = warnings.Add });
        var (table, _, property) = CreateValueProperty("test_col", testCase.CsTypeName);
        var dbColumn = new TestColumn("test_col", "date", "date", testCase.ColumnDefault);

        var defaultAttr = factory.ParseDefaultForTest(table, dbColumn, property);

        await Assert.That(defaultAttr).IsNull();
        await Assert.That(warnings.Count).IsEqualTo(1);
        await Assert.That(warnings[0].Contains("Skipping unsupported zero date default", StringComparison.Ordinal)).IsTrue();
        await Assert.That(warnings[0].Contains("test_table.test_col", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ParseDefaultValue_EnumLabelDefault_MapsToEnumValueAndRoundTripsToSqlLabel()
    {
        var factory = new TestMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions());
        var enumProperty = new EnumProperty(
            enumValues: [("standard", 1), ("premium", 2)],
            csEnumValues: [("Standard", 1), ("Premium", 2)],
            declaredInClass: false);
        var (table, column, property) = CreateEnumProperty("tier", "TierValue", enumProperty);
        var dbColumn = new TestColumn("tier", "enum", "enum('standard','premium')", "'premium'");

        var defaultAttr = factory.ParseDefaultForTest(table, dbColumn, property);

        await Assert.That(defaultAttr).IsTypeOf<DefaultAttribute>();
        await Assert.That(((DefaultAttribute)defaultAttr!).Value).IsTypeOf<int>();
        await Assert.That((int)((DefaultAttribute)defaultAttr).Value).IsEqualTo(2);

        var (_, columnWithDefault, propertyWithDefault) = CreateEnumProperty(
            "tier",
            "TierValue",
            enumProperty,
            [(DefaultAttribute)defaultAttr]);
        await Assert.That(propertyWithDefault.GetDefaultValueCode()).IsEqualTo("TierValue.Premium");

        var sqlDefault = SqlFromMetadataFactory.GetFactoryFromDatabaseType(DataLinq.DatabaseType.MariaDB).GetDefaultValue(columnWithDefault);
        await Assert.That(sqlDefault).IsEqualTo("'premium'");
    }

    [Test]
    [MethodDataSource(nameof(SqlLiteralFormattingCases))]
    public async Task GetDefaultValue_FormatsTypedSqlLiterals(SqlLiteralFormattingCase testCase)
    {
        var (_, column, _) = CreateProperty(
            "test_col",
            testCase.CsType,
            [new DefaultAttribute(testCase.DefaultValue)]);

        var sqlDefault = SqlFromMetadataFactory.GetFactoryFromDatabaseType(DataLinq.DatabaseType.MariaDB).GetDefaultValue(column);

        await Assert.That(sqlDefault).IsEqualTo(testCase.ExpectedSqlDefault);
    }

    [Test]
    public async Task GetDefaultValue_GuidMariaDbDefault_FormatsAsUuidStringLiteral()
    {
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var (_, column, _) = CreateProperty(
            "test_col",
            new CsTypeDeclaration(typeof(Guid)),
            [new DefaultAttribute(guid)]);

        var sqlDefault = SqlFromMetadataFactory.GetFactoryFromDatabaseType(DataLinq.DatabaseType.MariaDB).GetDefaultValue(column);

        await Assert.That(sqlDefault).IsEqualTo($"'{guid}'");
    }

    [Test]
    public async Task GetDefaultValue_GuidMySqlBinaryDefault_FormatsAsHexLiteral()
    {
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var (_, column, _) = CreateProperty(
            "test_col",
            new CsTypeDeclaration(typeof(Guid)),
            [new DefaultAttribute(guid)],
            dbTypes: [new DatabaseColumnType(DataLinq.DatabaseType.MySQL, "binary", 16)]);

        var sqlDefault = SqlFromMetadataFactory.GetFactoryFromDatabaseType(DataLinq.DatabaseType.MySQL).GetDefaultValue(column);

        await Assert.That(sqlDefault).IsEqualTo($"X'{Convert.ToHexString(guid.ToByteArray())}'");
    }

    [Test]
    public async Task GetDefaultValue_DefaultNewUuid_RoundTripsToUuidFunction()
    {
        var (_, column, _) = CreateProperty(
            "test_col",
            new CsTypeDeclaration(typeof(Guid)),
            [new DefaultNewUUIDAttribute()]);

        var sqlDefault = SqlFromMetadataFactory.GetFactoryFromDatabaseType(DataLinq.DatabaseType.MariaDB).GetDefaultValue(column);

        await Assert.That(sqlDefault).IsEqualTo("UUID()");
    }

    [Test]
    public async Task GetCreateTables_EmitsFormattedMariaDbDefaults()
    {
        var database = CreateDatabase(
            CreateColumn(
                "Id",
                typeof(int),
                "id",
                primaryKey: true,
                autoIncrement: true),
            CreateColumn(
                "DisplayName",
                typeof(string),
                "display_name",
                attributes: [new DefaultAttribute("O'Reilly")]),
            CreateColumn(
                "IsEnabled",
                typeof(bool),
                "is_enabled",
                attributes: [new DefaultAttribute(true)]),
            CreateColumn(
                "CreatedOn",
                typeof(DateOnly),
                "created_on",
                attributes: [new DefaultAttribute(new DateOnly(2024, 1, 2))]),
            CreateColumn(
                "PublicId",
                typeof(Guid),
                "public_id",
                attributes: [new DefaultNewUUIDAttribute()]));

        var sqlResult = SqlFromMetadataFactory.GetFactoryFromDatabaseType(DataLinq.DatabaseType.MariaDB).GetCreateTables(database, foreignKeyRestrict: false);

        await Assert.That(sqlResult.HasValue).IsTrue();

        var sql = sqlResult.Value.Text;
        await Assert.That(sql.Contains("DEFAULT 'O''Reilly'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(sql.Contains("DEFAULT b'1'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(sql.Contains("DEFAULT '2024-01-02'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(sql.Contains("DEFAULT UUID()", StringComparison.Ordinal)).IsTrue();
    }

    private static (TableDefinition table, ColumnDefinition column, ValueProperty property) CreateValueProperty(
        string columnName,
        string csTypeName,
        Attribute[]? attributes = null)
    {
        var csType = csTypeName switch
        {
            "int" => new CsTypeDeclaration(typeof(int)),
            "long" => new CsTypeDeclaration(typeof(long)),
            "bool" => new CsTypeDeclaration(typeof(bool)),
            "char" => new CsTypeDeclaration(typeof(char)),
            "decimal" => new CsTypeDeclaration(typeof(decimal)),
            "float" => new CsTypeDeclaration(typeof(float)),
            "double" => new CsTypeDeclaration(typeof(double)),
            "string" => new CsTypeDeclaration(typeof(string)),
            "DateOnly" => new CsTypeDeclaration(typeof(DateOnly)),
            "TimeOnly" => new CsTypeDeclaration(typeof(TimeOnly)),
            "DateTime" => new CsTypeDeclaration(typeof(DateTime)),
            "TimeSpan" => new CsTypeDeclaration(typeof(TimeSpan)),
            "Guid" => new CsTypeDeclaration(typeof(Guid)),
            _ => throw new NotImplementedException($"Unsupported test type {csTypeName}")
        };

        return CreateProperty(columnName, csType, attributes);
    }

    private static (TableDefinition table, ColumnDefinition column, ValueProperty property) CreateEnumProperty(
        string columnName,
        string enumTypeName,
        EnumProperty enumProperty,
        Attribute[]? attributes = null)
    {
        var csType = new CsTypeDeclaration(enumTypeName, "TestNamespace", ModelCsType.Enum);
        return CreateProperty(columnName, csType, attributes, enumProperty);
    }

    private static (TableDefinition table, ColumnDefinition column, ValueProperty property) CreateProperty(
        string columnName,
        CsTypeDeclaration csType,
        Attribute[]? attributes = null,
        EnumProperty? enumProperty = null,
        DatabaseColumnType[]? dbTypes = null)
    {
        var database = CreateDatabase(
            CreateColumn(
                "Id",
                typeof(int),
                "id",
                primaryKey: true),
            CreateColumn(
                "TestProperty",
                csType,
                columnName,
                attributes,
                enumProperty: enumProperty,
                dbTypes: dbTypes));
        var table = database.TableModels.Single().Table;
        var column = table.Columns.Single(x => x.DbName == columnName);
        var property = column.ValueProperty;

        return (table, column, property);
    }

    private static DatabaseDefinition CreateDatabase(params MetadataValuePropertyDraft[] columns)
    {
        var draft = new MetadataDatabaseDraft(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "TestModels",
                    new MetadataModelDraft(new CsTypeDeclaration("TestModel", "TestNamespace", ModelCsType.Class))
                    {
                        ValueProperties = columns
                    },
                    new MetadataTableDraft("test_table"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private static MetadataValuePropertyDraft CreateColumn(
        string propertyName,
        Type csType,
        string columnName,
        bool primaryKey = false,
        bool autoIncrement = false,
        Attribute[]? attributes = null)
    {
        return CreateColumn(
            propertyName,
            new CsTypeDeclaration(csType),
            columnName,
            attributes,
            primaryKey,
            autoIncrement);
    }

    private static MetadataValuePropertyDraft CreateColumn(
        string propertyName,
        CsTypeDeclaration csType,
        string columnName,
        Attribute[]? attributes = null,
        bool primaryKey = false,
        bool autoIncrement = false,
        EnumProperty? enumProperty = null,
        DatabaseColumnType[]? dbTypes = null)
    {
        return new MetadataValuePropertyDraft(
            propertyName,
            csType,
            new MetadataColumnDraft(columnName)
            {
                DbTypes = dbTypes ?? [],
                PrimaryKey = primaryKey,
                AutoIncrement = autoIncrement
            })
        {
            Attributes = [new ColumnAttribute(columnName), .. (attributes ?? [])],
            CsNullable = autoIncrement,
            EnumProperty = enumProperty
        };
    }
}
