using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.MariaDB;
using DataLinq.Metadata;
using DataLinq.MySql.Shared;
using MySqlConnector;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.MySql;

public abstract class MetadataFromSqlFactory : IMetadataFromSqlFactory
{
    private readonly MetadataFromDatabaseFactoryOptions options;
    private readonly DatabaseType databaseType;

    protected readonly record struct ProviderColumnImport(ProviderValuePropertyDraft? Property);
    protected readonly record struct ProviderForeignKeyReference(
        string TableName,
        string ColumnName,
        string ReferencedTableName,
        string ReferencedColumnName,
        string ConstraintName);

    public static MetadataFromSqlFactory GetSqlFactory(MetadataFromDatabaseFactoryOptions options, DatabaseType databaseType)
    {
        if (databaseType == DatabaseType.MariaDB)
            return new MetadataFromMariaDBFactory(options);
        if (databaseType == DatabaseType.MySQL)
            return new MetadataFromMySqlFactory(options);

        throw new NotImplementedException($"No metadata factory for {databaseType}");
    }

    public MetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options, DatabaseType databaseType)
    {
        this.options = options;
        this.databaseType = databaseType;
    }

    public abstract Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString);

    protected IEnumerable<string> FindMissingTablesOrViewInOptionsList(IReadOnlyList<ProviderTableModelDraft> tableModels)
    {
        foreach (var tableName in options.Include ?? [])
        {
            if (!tableModels.Any(x => tableName.Equals(x.Table.DbName, StringComparison.OrdinalIgnoreCase)))
                yield return tableName;
        }
    }
    protected bool IsTableOrViewInOptionsList(ProviderTableModelDraft tableModel)
    {
        // If the Include list is null or empty, always include the item.
        if (options.Include == null || !options.Include.Any())
            return true;

        // Otherwise, the table/view name must exist in the Include list.
        return options.Include.Any(x => x.Equals(tableModel.Table.DbName, StringComparison.OrdinalIgnoreCase));
    }

    protected Option<ProviderColumnImport, IDLOptionFailure> ParseColumn(ProviderTableDraft table, ICOLUMNS dbColumns)
    {
        if (IsGeneratedColumn(dbColumns))
        {
            options.Log?.Invoke($"Warning: Skipping unsupported {databaseType} generated column '{table.DbName}.{dbColumns.COLUMN_NAME}'.");
            return new ProviderColumnImport(null);
        }

        var dbType = new DatabaseColumnType(databaseType, dbColumns.DATA_TYPE);

        if (dbColumns.COLUMN_TYPE.Contains("unsigned"))
            dbType.SetSigned(false);

        if (dbType.Name == "decimal" || dbType.Name == "bit")
        {
            dbType.SetLength(dbColumns.NUMERIC_PRECISION);
            dbType.SetDecimals(dbColumns.NUMERIC_SCALE);
        }
        else if (dbType.Name == "int" || dbType.Name == "tinyint" || dbType.Name == "smallint" || dbType.Name == "mediumint" || dbType.Name == "bigint")
        {
            // Parse length from COLUMN_TYPE string
            var length = ParseLengthFromColumnType(dbColumns.COLUMN_TYPE);
            dbType.SetLength(length);
        }
        else if (dbType.Name != "enum")
        {
            dbType.SetLength(dbColumns.CHARACTER_MAXIMUM_LENGTH);
        }

        var column = new ProviderColumnDraft(dbColumns.COLUMN_NAME)
        {
            Nullable = dbColumns.IS_NULLABLE == "YES",
            PrimaryKey = dbColumns.COLUMN_KEY == COLUMN_KEY.PRI,
            AutoIncrement = dbColumns.EXTRA.Contains("auto_increment")
        };
        column.DbTypes.Add(dbType);

        if (!ParseCsType(dbType, table.DbName, dbColumns.COLUMN_NAME).TryUnwrap(out var csType, out var csTypeFailure))
            return csTypeFailure;

        if (!ParseCsTypeDeclaration(csType, table.DbName, dbColumns.COLUMN_NAME).TryUnwrap(out var csTypeDeclaration, out var valuePropertyFailure))
            return valuePropertyFailure;

        var valueProp = new ProviderValuePropertyDraft(
            dbColumns.COLUMN_NAME.ToCSharpIdentifier(options.CapitaliseNames),
            csTypeDeclaration,
            column,
            CreateColumnAttributes(column).ToList())
        {
            CsSize = MetadataTypeConverter.CsTypeSize(csType),
            CsNullable = column.Nullable || column.AutoIncrement
        };

        if (csType == "enum")
        {
            var (dbValues, csValues) = ParseEnumType(dbColumns.COLUMN_TYPE);
            valueProp.EnumProperty = new EnumProperty(dbValues, csValues, true);

            if (valueProp.CsType.Name == "enum")
                valueProp.CsType = valueProp.CsType.MutateName(valueProp.PropertyName + "Value");
        }

        var defaultAttr = ParseDefaultValue(table, dbColumns, valueProp);
        if (defaultAttr != null)
            valueProp.Attributes.Add(defaultAttr);

        if (!string.IsNullOrWhiteSpace(dbColumns.COLUMN_COMMENT))
            valueProp.Attributes.Add(new CommentAttribute(dbColumns.COLUMN_COMMENT));

        return new ProviderColumnImport(valueProp);
    }

    private static bool IsGeneratedColumn(ICOLUMNS dbColumns)
    {
        var extra = dbColumns.EXTRA.Replace("_", " ");
        if (extra.Contains("VIRTUAL GENERATED", StringComparison.OrdinalIgnoreCase) ||
            extra.Contains("STORED GENERATED", StringComparison.OrdinalIgnoreCase) ||
            extra.Contains("PERSISTENT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var isGeneratedProperty = dbColumns.GetType().GetProperty("IS_GENERATED");
        var isGeneratedValue = isGeneratedProperty?.GetValue(dbColumns)?.ToString();

        return !string.IsNullOrWhiteSpace(isGeneratedValue) &&
            !string.Equals(isGeneratedValue, "NEVER", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(isGeneratedValue, "NO", StringComparison.OrdinalIgnoreCase);
    }

    protected void ParseCheckConstraints(ProviderDatabaseDraft database, DatabaseAccess informationSchemaAccess)
    {
        using var command = new MySqlCommand(
            """
            SELECT
                tc.TABLE_NAME,
                cc.CONSTRAINT_NAME,
                cc.CHECK_CLAUSE
            FROM information_schema.TABLE_CONSTRAINTS tc
            JOIN information_schema.CHECK_CONSTRAINTS cc
                ON cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA
                AND cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
            WHERE tc.TABLE_SCHEMA = @schema
                AND tc.CONSTRAINT_TYPE = 'CHECK'
            ORDER BY tc.TABLE_NAME, cc.CONSTRAINT_NAME
            """);
        command.Parameters.AddWithValue("@schema", database.DbName);

        using var reader = informationSchemaAccess.ExecuteReader(command);
        var tableNameOrdinal = reader.GetOrdinal("TABLE_NAME");
        var constraintNameOrdinal = reader.GetOrdinal("CONSTRAINT_NAME");
        var checkClauseOrdinal = reader.GetOrdinal("CHECK_CLAUSE");

        while (reader.ReadNextRow())
        {
            var tableName = reader.GetString(tableNameOrdinal);
            var constraintName = reader.GetString(constraintNameOrdinal);
            var checkClause = NormalizeCheckClause(reader.GetString(checkClauseOrdinal));
            var tableModel = database.TableModels.SingleOrDefault(x => x.Table.DbName == tableName);

            tableModel?.ModelAttributes.Add(new CheckAttribute(databaseType, constraintName, checkClause));
        }
    }

    protected Dictionary<(string TableName, string ConstraintName), (ReferentialAction OnUpdate, ReferentialAction OnDelete)> ParseReferentialActions(
        ProviderDatabaseDraft database,
        DatabaseAccess informationSchemaAccess)
    {
        using var command = new MySqlCommand(
            """
            SELECT
                TABLE_NAME,
                CONSTRAINT_NAME,
                UPDATE_RULE,
                DELETE_RULE
            FROM information_schema.REFERENTIAL_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = @schema
            """);
        command.Parameters.AddWithValue("@schema", database.DbName);

        using var reader = informationSchemaAccess.ExecuteReader(command);
        var tableNameOrdinal = reader.GetOrdinal("TABLE_NAME");
        var constraintNameOrdinal = reader.GetOrdinal("CONSTRAINT_NAME");
        var updateRuleOrdinal = reader.GetOrdinal("UPDATE_RULE");
        var deleteRuleOrdinal = reader.GetOrdinal("DELETE_RULE");
        var actions = new Dictionary<(string TableName, string ConstraintName), (ReferentialAction OnUpdate, ReferentialAction OnDelete)>();

        while (reader.ReadNextRow())
        {
            var key = (reader.GetString(tableNameOrdinal), reader.GetString(constraintNameOrdinal));
            actions[key] = (
                ParseReferentialAction(reader.GetString(updateRuleOrdinal)),
                ParseReferentialAction(reader.GetString(deleteRuleOrdinal)));
        }

        return actions;
    }

    protected static ReferentialAction ParseReferentialAction(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            "NO ACTION" => ReferentialAction.NoAction,
            "RESTRICT" => ReferentialAction.Restrict,
            "CASCADE" => ReferentialAction.Cascade,
            "SET NULL" => ReferentialAction.SetNull,
            "SET DEFAULT" => ReferentialAction.SetDefault,
            _ => ReferentialAction.Unspecified
        };
    }

    protected Option<IndexType, IDLOptionFailure> ParseIndexType(string indexType, string tableName, string indexName)
    {
        return indexType.Trim().ToUpperInvariant() switch
        {
            "BTREE" => IndexType.BTREE,
            "FULLTEXT" => IndexType.FULLTEXT,
            "HASH" => IndexType.HASH,
            "RTREE" => IndexType.RTREE,
            _ => DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Unsupported {databaseType} index type '{indexType}' for index '{tableName}.{indexName}'."),
        };
    }

    protected Option<ProviderForeignKeyReference, IDLOptionFailure> ParseForeignKeyReference(
        string databaseName,
        string? tableName,
        string? columnName,
        string? referencedTableName,
        string? referencedColumnName,
        string? constraintName)
    {
        if (string.IsNullOrWhiteSpace(tableName) ||
            string.IsNullOrWhiteSpace(columnName) ||
            string.IsNullOrWhiteSpace(referencedTableName) ||
            string.IsNullOrWhiteSpace(referencedColumnName) ||
            string.IsNullOrWhiteSpace(constraintName))
        {
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Malformed {databaseType} foreign-key metadata row in database '{databaseName}': table, column, referenced table, referenced column, and constraint name are required.");
        }

        return new ProviderForeignKeyReference(tableName, columnName, referencedTableName, referencedColumnName, constraintName);
    }

    private Option<CsTypeDeclaration, IDLOptionFailure> ParseCsTypeDeclaration(string csTypeName, string tableName, string columnName)
    {
        if (csTypeName == "enum")
            return new CsTypeDeclaration(csTypeName, string.Empty, ModelCsType.Enum);

        var type = MetadataTypeConverter.GetType(csTypeName);
        if (type == null)
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Unsupported C# type '{csTypeName}' for column '{tableName}.{columnName}'.");

        return new CsTypeDeclaration(type);
    }

    private static IEnumerable<Attribute> CreateColumnAttributes(ProviderColumnDraft column)
    {
        if (column.PrimaryKey)
            yield return new PrimaryKeyAttribute();

        if (column.AutoIncrement)
            yield return new AutoIncrementAttribute();

        if (column.Nullable)
            yield return new NullableAttribute();

        yield return new ColumnAttribute(column.DbName);

        foreach (var dbType in column.DbTypes)
            yield return new TypeAttribute(dbType);
    }

    private static string NormalizeCheckClause(string checkClause) =>
        checkClause.Replace(@"\'", "'");

    protected DefaultAttribute? ParseDefaultValue(ProviderTableDraft table, ICOLUMNS dbColumns, ProviderValuePropertyDraft property)
    {
        if (dbColumns.COLUMN_DEFAULT == null || string.Equals(dbColumns.COLUMN_DEFAULT, "NULL", StringComparison.CurrentCultureIgnoreCase))
            return null;

        if (dbColumns.COLUMN_DEFAULT == "" && property.CsType.Type != typeof(string))
            return null;

        var normalizedExpression = UnwrapParenthesizedDefaultExpression(dbColumns.COLUMN_DEFAULT).Trim();

        if (IsCurrentTimestampDefault(normalizedExpression) ||
            (property.CsType.Type == typeof(DateOnly) && IsCurrentDateDefault(normalizedExpression)) ||
            (property.CsType.Type == typeof(TimeOnly) && IsCurrentTimeDefault(normalizedExpression)))
            return new DefaultCurrentTimestampAttribute();

        if (normalizedExpression.StartsWith("UUID()", StringComparison.CurrentCultureIgnoreCase))
            return new DefaultNewUUIDAttribute();

        if (property.CsType.Type == typeof(bool) && normalizedExpression.StartsWith("b'"))
            return new DefaultAttribute(normalizedExpression == "b'1'");

        if (!IsTypedSqlLiteralDefault(normalizedExpression, property))
            return new DefaultSqlAttribute(databaseType, FormatRawDefaultExpression(dbColumns.COLUMN_DEFAULT.Trim(), normalizedExpression));

        var normalizedDefault = NormalizeSqlLiteral(normalizedExpression);

        if (IsUnsupportedZeroDateDefault(normalizedDefault, property))
        {
            options.Log?.Invoke($"Warning: Skipping unsupported zero date default '{dbColumns.COLUMN_DEFAULT}' for {table.DbName}.{dbColumns.COLUMN_NAME}.");
            return null;
        }

        var value = ConvertDefaultValue(normalizedDefault, property);

        return new DefaultAttribute(value);

    }

    private static bool IsTypedSqlLiteralDefault(string defaultValue, ProviderValuePropertyDraft property)
    {
        if (IsSqlStringLiteral(defaultValue))
            return true;

        if (property.EnumProperty != null)
            return IsIntegerLiteral(defaultValue);

        var type = property.CsType.Type;
        if (type == null)
            return false;

        if (type == typeof(bool))
            return defaultValue is "0" or "1" ||
                string.Equals(defaultValue, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(defaultValue, "FALSE", StringComparison.OrdinalIgnoreCase);

        if (type == typeof(string))
            return !LooksLikeSqlExpression(defaultValue);

        if (type == typeof(char))
            return !LooksLikeSqlExpression(defaultValue);

        if (type == typeof(DateOnly) ||
            type == typeof(TimeOnly) ||
            type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(TimeSpan) ||
            type == typeof(Guid))
        {
            return false;
        }

        if (type == typeof(float) ||
            type == typeof(double) ||
            type == typeof(decimal))
        {
            return decimal.TryParse(defaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        return IsIntegerLiteral(defaultValue);
    }

    private static bool IsSqlStringLiteral(string defaultValue) =>
        defaultValue.Length >= 2 &&
        defaultValue[0] == '\'' &&
        defaultValue[^1] == '\'';

    private static bool LooksLikeSqlExpression(string defaultValue) =>
        defaultValue.Contains('(') ||
        defaultValue.Contains(')');

    private static string FormatRawDefaultExpression(string rawDefault, string normalizedExpression) =>
        rawDefault.StartsWith("(", StringComparison.Ordinal)
            ? rawDefault
            : $"({normalizedExpression})";

    private static bool IsIntegerLiteral(string defaultValue) =>
        decimal.TryParse(defaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    private static bool IsCurrentTimestampDefault(string defaultValue) =>
        defaultValue.StartsWith("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase) ||
        defaultValue.StartsWith("NOW(", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(defaultValue, "NOW()", StringComparison.OrdinalIgnoreCase) ||
        defaultValue.StartsWith("LOCALTIMESTAMP", StringComparison.OrdinalIgnoreCase) ||
        defaultValue.StartsWith("LOCALTIME", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentDateDefault(string defaultValue) =>
        defaultValue.StartsWith("CURRENT_DATE", StringComparison.OrdinalIgnoreCase) ||
        defaultValue.StartsWith("CURDATE(", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(defaultValue, "CURDATE()", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentTimeDefault(string defaultValue) =>
        defaultValue.StartsWith("CURRENT_TIME", StringComparison.OrdinalIgnoreCase) ||
        defaultValue.StartsWith("CURTIME(", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(defaultValue, "CURTIME()", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnsupportedZeroDateDefault(string defaultValue, ProviderValuePropertyDraft property)
    {
        if (!defaultValue.StartsWith("0000-00-00", StringComparison.Ordinal))
            return false;

        return property.CsType.Type == typeof(DateOnly) ||
               property.CsType.Type == typeof(DateTime) ||
               property.CsType.Type == typeof(DateTimeOffset);
    }

    private static object ConvertDefaultValue(string defaultValue, ProviderValuePropertyDraft property)
    {
        if (property.EnumProperty != null)
        {
            if (int.TryParse(defaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var enumNumericValue))
                return enumNumericValue;

            var enumValue = property.EnumProperty.Value.DbValuesOrCsValues
                .FirstOrDefault(x => string.Equals(x.name, defaultValue, StringComparison.Ordinal));

            if (enumValue.name != null)
                return enumValue.value;

            return defaultValue;
        }

        if (property.CsType.Type == typeof(bool))
        {
            if (defaultValue == "0")
                return false;

            if (defaultValue == "1")
                return true;

            return Convert.ChangeType(defaultValue, property.CsType.Type, CultureInfo.InvariantCulture);
        }

        if (property.CsType.Type == typeof(DateOnly))
            return DateOnly.Parse(defaultValue, CultureInfo.InvariantCulture);

        if (property.CsType.Type == typeof(TimeOnly))
            return TimeOnly.Parse(defaultValue, CultureInfo.InvariantCulture);

        if (property.CsType.Type == typeof(TimeSpan))
            return TimeSpan.Parse(defaultValue, CultureInfo.InvariantCulture);

        if (property.CsType.Type == typeof(Guid))
            return Guid.Parse(defaultValue);

        if (property.CsType.Type != null)
            return Convert.ChangeType(defaultValue, property.CsType.Type, CultureInfo.InvariantCulture);

        return defaultValue;
    }

    private static string NormalizeSqlLiteral(string defaultValue)
    {
        if (defaultValue.Length >= 2 && defaultValue[0] == '\'' && defaultValue[^1] == '\'')
            return defaultValue[1..^1].Replace("''", "'");

        return defaultValue;
    }

    private static string UnwrapParenthesizedDefaultExpression(string defaultValue)
    {
        var trimmed = defaultValue.Trim();

        while (trimmed.Length >= 2 && trimmed[0] == '(' && trimmed[^1] == ')' && HasBalancedOuterParentheses(trimmed))
            trimmed = trimmed[1..^1].Trim();

        return trimmed;
    }

    private static bool HasBalancedOuterParentheses(string value)
    {
        var depth = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '(')
                depth++;
            else if (value[i] == ')')
                depth--;

            if (depth == 0 && i < value.Length - 1)
                return false;
        }

        return depth == 0;
    }

    protected uint? ParseLengthFromColumnType(string columnType)
    {
        var startIndex = columnType.IndexOf('(') + 1;
        var endIndex = columnType.IndexOf(')');
        if (startIndex > 0 && endIndex > startIndex)
        {
            var lengthStr = columnType.Substring(startIndex, endIndex - startIndex);
            if (uint.TryParse(lengthStr, out var length))
            {
                return length;
            }
        }

        return null; // Default length if parsing fails
    }

    private (IEnumerable<(string, int)> dbValues, IEnumerable<(string, int)> csValues) ParseEnumType(string COLUMN_TYPE)
    {
        var startIndex = COLUMN_TYPE.IndexOf('(') + 1;
        var endIndex = COLUMN_TYPE.LastIndexOf(')');
        if (startIndex == 0 || endIndex == -1 || endIndex < startIndex)
            return ([], []);

        var enumContent = COLUMN_TYPE.Substring(startIndex, endIndex - startIndex);

        var regex = new Regex(@"'([^']*)'");
        var matches = regex.Matches(enumContent);

        var dbValues = new List<(string, int)>();
        var csValues = new List<(string, int)>();

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var dbName = match.Groups[1].Value;
            dbValues.Add((dbName, i + 1));

            // It's crucial to ensure the resulting name is a valid C# identifier.
            var csName = string.IsNullOrWhiteSpace(dbName)
                ? "Empty" // Or "None", "Default", etc. "Empty" is clear.
                : options.CapitaliseNames && !dbName.IsFirstCharUpper() ? dbName.ToPascalCase() : dbName.Replace(" ", "_");

            csValues.Add((csName, i + 1));
        }
        return (dbValues, csValues);
    }

    protected sealed class ProviderDatabaseDraft(string name, CsTypeDeclaration csType, string dbName)
    {
        public string Name { get; } = name;
        public CsTypeDeclaration CsType { get; } = csType;
        public string DbName { get; } = dbName;
        public List<ProviderTableModelDraft> TableModels { get; } = [];

        public MetadataDatabaseDraft ToMetadataDraft() => new(Name, CsType)
        {
            DbName = DbName,
            TableModels = TableModels.Select(x => x.ToMetadataDraft()).ToArray()
        };
    }

    protected sealed class ProviderTableModelDraft(
        string csPropertyName,
        CsTypeDeclaration modelType,
        ProviderTableDraft table)
    {
        public string CsPropertyName { get; } = csPropertyName;
        public CsTypeDeclaration ModelType { get; } = modelType;
        public ProviderTableDraft Table { get; } = table;
        public List<Attribute> ModelAttributes { get; } = [];

        public MetadataTableModelDraft ToMetadataDraft() => new(
            CsPropertyName,
            new MetadataModelDraft(ModelType)
            {
                Attributes = ModelAttributes,
                ValueProperties = Table.Columns.Select(x => x.ToMetadataDraft()).ToArray()
            },
            Table.ToMetadataDraft());
    }

    protected sealed class ProviderTableDraft(string dbName, TableType type)
    {
        public string DbName { get; } = dbName;
        public TableType Type { get; } = type;
        public string? Definition { get; set; }
        public List<ProviderValuePropertyDraft> Columns { get; } = [];

        public MetadataTableDraft ToMetadataDraft() => new(DbName)
        {
            Type = Type,
            Definition = Definition
        };
    }

    protected sealed class ProviderValuePropertyDraft(
        string propertyName,
        CsTypeDeclaration csType,
        ProviderColumnDraft column,
        List<Attribute> attributes)
    {
        public string PropertyName { get; } = propertyName;
        public CsTypeDeclaration CsType { get; set; } = csType;
        public ProviderColumnDraft Column { get; } = column;
        public List<Attribute> Attributes { get; } = attributes;
        public bool CsNullable { get; init; }
        public int? CsSize { get; init; }
        public EnumProperty? EnumProperty { get; set; }

        public MetadataValuePropertyDraft ToMetadataDraft() => new(
            PropertyName,
            CsType,
            Column.ToMetadataDraft())
        {
            Attributes = Attributes,
            CsNullable = CsNullable,
            CsSize = CsSize,
            EnumProperty = EnumProperty
        };
    }

    protected sealed class ProviderColumnDraft(string dbName)
    {
        public string DbName { get; } = dbName;
        public List<DatabaseColumnType> DbTypes { get; } = [];
        public bool PrimaryKey { get; init; }
        public bool ForeignKey { get; set; }
        public bool AutoIncrement { get; init; }
        public bool Nullable { get; init; }

        public MetadataColumnDraft ToMetadataDraft() => new(DbName)
        {
            DbTypes = DbTypes,
            PrimaryKey = PrimaryKey,
            ForeignKey = ForeignKey,
            AutoIncrement = AutoIncrement,
            Nullable = Nullable
        };
    }

    protected virtual Option<string, IDLOptionFailure> ParseCsType(DatabaseColumnType dbType, string tableName, string columnName)
    {
        var dbTypeName = dbType.Name.ToLower();

        if (dbTypeName.StartsWith("enum"))
            return "enum";

        // Use a switch on the lowercased DB type name
        switch (dbTypeName)
        {
            case "int":
                return (dbType.Signed == false) ? "uint" : "int";
            case "tinyint":
                return (dbType.Signed == false) ? "byte" : "sbyte"; // Use byte for unsigned, sbyte for signed
            case "smallint":
                return (dbType.Signed == false) ? "ushort" : "short";
            case "mediumint":
                return (dbType.Signed == false) ? "uint" : "int"; // No direct ushort equivalent, maps to uint/int
            case "bigint":
                return (dbType.Signed == false) ? "ulong" : "long";

            case "bit":
                return "bool";
            case "double":
                return "double";
            case "float":
                return "float";
            case "decimal":
                return "decimal";

            case "varchar":
            case "tinytext":
            case "text":
            case "mediumtext":
            case "longtext":
            case "char":
                return "string";

            case "datetime":
            case "timestamp":
                return "DateTime";
            case "date":
                return "DateOnly";
            case "time":
                return "TimeOnly";

            case "year":
                return "int";

            case "binary":
                return "Guid";

            case "varbinary":
            case "blob":
            case "tinyblob":
            case "mediumblob":
            case "longblob":
                return "byte[]";

            default:
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Unsupported {databaseType} column type '{dbType.Name}' for column '{tableName}.{columnName}'.");
        }
        ;
    }
}
