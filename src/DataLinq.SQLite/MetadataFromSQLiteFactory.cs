using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Logging;
using DataLinq.Metadata;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.SQLite;

public class MetadataFromSQLiteFactoryCreator : IMetadataFromDatabaseFactoryCreator
{
    public IMetadataFromSqlFactory GetMetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options)
    {
        return new MetadataFromSQLiteFactory(options);
    }
}

public class MetadataFromSQLiteFactory : IMetadataFromSqlFactory
{
    private readonly MetadataFromDatabaseFactoryOptions options;

    public MetadataFromSQLiteFactory(MetadataFromDatabaseFactoryOptions options)
    {
        this.options = options;
    }

    public Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString) => DLOptionFailure.CatchAll<DatabaseDefinition>(() =>
    {
        var normalizedConnectionString = SQLiteConnectionStringFactory.NormalizeConnectionString(connectionString, dbName);
        using var keepAliveLease = SQLiteConnectionStringFactory.AcquireKeepAliveConnectionIfInMemory(normalizedConnectionString);
        var dbAccess = new SQLiteDbAccess(normalizedConnectionString, DataLinqLoggingConfiguration.NullConfiguration);

        var database = new SQLiteProviderDatabaseDraft(
            name,
            new CsTypeDeclaration(csTypeName, csNamespace, ModelCsType.Class),
            dbName);
        if (!dbAccess
            .ReadReader("SELECT *\r\nFROM sqlite_master m\r\nWHERE\r\nm.type <> 'index' AND\r\nm.tbl_name <> 'sqlite_sequence'")
            .Select(x => ParseTable(database, x, dbAccess))
            .Transpose()
            .TryUnwrap(out var tableModels, out var tableFailure))
            return SingleOrAggregate(tableFailure);

        database.TableModels.AddRange(tableModels.Where(IsTableOrViewInOptionsList));

        var missingTables = FindMissingTablesOrViewInOptionsList(database.TableModels).ToList();
        if (missingTables.Count != 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Could not find the specified tables or views: {missingTables.ToJoinedString(", ")}");

        if (database.TableModels.Count == 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"No tables or views found in database '{dbName}'. Please check the connection string and database name.");

        ParseIndices(database, dbAccess);
        if (!ParseRelations(database, dbAccess).TryUnwrap(out _, out var relationFailure))
            return relationFailure;

        return new MetadataDefinitionFactory()
            .BuildProviderMetadata(database.ToMetadataDraft());
    });

    private IEnumerable<string> FindMissingTablesOrViewInOptionsList(IReadOnlyList<SQLiteProviderTableModelDraft> tableModels)
    {
        foreach (var tableName in options.Include ?? [])
        {
            if (!tableModels.Any(x => tableName.Equals(x.Table.DbName, StringComparison.OrdinalIgnoreCase)))
                yield return tableName;
        }
    }
    private bool IsTableOrViewInOptionsList(SQLiteProviderTableModelDraft tableModel)
    {
        if (options.Include == null || !options.Include.Any())
            return true;

        return options.Include.Any(x => x.Equals(tableModel.Table.DbName, StringComparison.OrdinalIgnoreCase));
    }

    private void ParseIndices(SQLiteProviderDatabaseDraft database, DatabaseAccess dbAccess)
    {
        foreach (var tableModel in database.TableModels.Where(x => x.Table.Type == TableType.Table))
        {
            foreach (var indexReader in dbAccess.ReadReader($"SELECT name, origin, \"unique\", partial FROM pragma_index_list({QuoteSqlLiteral(tableModel.Table.DbName)})"))
            {
                var rawIndexName = indexReader.GetString(0);
                var origin = indexReader.GetString(1);
                var indexCharacteristic = indexReader.GetInt32(2) == 1
                    ? IndexCharacteristic.Unique
                    : IndexCharacteristic.Simple;
                var isPartial = indexReader.GetInt32(3) == 1;

                if (origin == "pk")
                    continue;

                if (isPartial)
                {
                    options.Log?.Invoke($"Warning: Skipping unsupported SQLite partial index '{rawIndexName}' on table '{tableModel.Table.DbName}'.");
                    continue;
                }

                var indexColumns = new List<SQLiteProviderValuePropertyDraft>();
                var skipIndex = false;

                foreach (var columnReader in dbAccess.ReadReader($"SELECT seqno, cid, name, \"desc\", \"key\" FROM pragma_index_xinfo({QuoteSqlLiteral(rawIndexName)}) WHERE \"key\" = 1 ORDER BY seqno"))
                {
                    var cid = columnReader.GetInt32(1);
                    var isDescending = columnReader.GetInt32(3) == 1;

                    if (cid < 0 || columnReader.IsDbNull(2))
                    {
                        options.Log?.Invoke($"Warning: Skipping unsupported SQLite expression index '{rawIndexName}' on table '{tableModel.Table.DbName}'.");
                        skipIndex = true;
                        break;
                    }

                    if (isDescending)
                    {
                        options.Log?.Invoke($"Warning: Skipping unsupported SQLite descending index '{rawIndexName}' on table '{tableModel.Table.DbName}'.");
                        skipIndex = true;
                        break;
                    }

                    var columnName = columnReader.GetString(2);
                    var column = tableModel
                        .Table.Columns.SingleOrDefault(x => x.Column.DbName == columnName);

                    if (column == null)
                    {
                        options.Log?.Invoke($"Warning: Skipping SQLite index '{rawIndexName}' on table '{tableModel.Table.DbName}' because column '{columnName}' was not imported.");
                        skipIndex = true;
                        break;
                    }

                    indexColumns.Add(column);
                }

                if (skipIndex || indexColumns.Count == 0)
                    continue;

                var name = rawIndexName.StartsWith("sqlite_autoindex", StringComparison.Ordinal)
                    ? GetAutoIndexName(tableModel.Table, indexColumns, indexCharacteristic)
                    : rawIndexName;

                var columnNames = indexColumns.Select(x => x.Column.DbName).ToArray();
                foreach (var column in indexColumns)
                {
                    column.Attributes.Add(new IndexAttribute(name, indexCharacteristic, IndexType.BTREE, columnNames));
                }
            }
        }
    }

    private static string GetAutoIndexName(SQLiteProviderTableDraft table, IReadOnlyList<SQLiteProviderValuePropertyDraft> columns, IndexCharacteristic characteristic)
    {
        if (columns.Count == 1)
            return columns[0].Column.DbName;

        var suffix = characteristic == IndexCharacteristic.Unique
            ? "unique"
            : characteristic.ToString().ToLowerInvariant();

        return $"{table.DbName}_{string.Join("_", columns.Select(x => x.Column.DbName))}_{suffix}";
    }

    private static string QuoteSqlLiteral(string value) => $"'{value.Replace("'", "''")}'";

    private Option<bool, IDLOptionFailure> ParseRelations(SQLiteProviderDatabaseDraft database, DatabaseAccess dbAccess)
    {
        foreach (var tableModel in database.TableModels.Where(x => x.Table.Type == TableType.Table))
        {
            foreach (var reader in dbAccess.ReadReader($"SELECT id, seq, \"table\", \"from\", \"to\", on_update, on_delete FROM pragma_foreign_key_list({QuoteSqlLiteral(tableModel.Table.DbName)})"))
            {
                var keyName = reader.GetString(0);
                var ordinal = reader.GetInt32(1);
                var tableName = reader.IsDbNull(2) ? null : reader.GetString(2);
                var fromColumnName = reader.IsDbNull(3) ? null : reader.GetString(3);
                var toColumnName = reader.IsDbNull(4) ? null : reader.GetString(4);
                var onUpdate = ParseReferentialAction(reader.GetString(5));
                var onDelete = ParseReferentialAction(reader.GetString(6));

                if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(fromColumnName))
                    return DLOptionFailure.Fail(
                        DLFailureType.InvalidModel,
                        $"Malformed SQLite foreign-key metadata row in table '{tableModel.Table.DbName}': referenced table and source column are required.");

                var foreignKeyColumn = tableModel
                    .Table.Columns.SingleOrDefault(x => x.Column.DbName == fromColumnName);

                if (foreignKeyColumn == null)
                    continue;

                var candidateTable = database
                    .TableModels.SingleOrDefault(x => x.Table.DbName == tableName)?
                    .Table;

                if (candidateTable == null)
                {
                    options.Log?.Invoke($"Warning: Skipping foreign key '{keyName}' on table '{tableModel.Table.DbName}' because referenced table '{tableName}' was not imported.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(toColumnName))
                {
                    var primaryKeyColumns = candidateTable.Columns.Where(x => x.Column.PrimaryKey).ToArray();
                    if (primaryKeyColumns.Length != 1)
                    {
                        return DLOptionFailure.Fail(
                            DLFailureType.InvalidModel,
                            $"SQLite foreign key '{keyName}' on table '{tableModel.Table.DbName}' omits the referenced column, but referenced table '{tableName}' does not have exactly one imported primary-key column.");
                    }

                    toColumnName = primaryKeyColumns[0].Column.DbName;
                }

                var candidateColumn = candidateTable.Columns.SingleOrDefault(x => x.Column.DbName == toColumnName);
                if (candidateColumn == null)
                {
                    options.Log?.Invoke($"Warning: Skipping foreign key '{keyName}' on table '{tableModel.Table.DbName}' because referenced column '{tableName}.{toColumnName}' was not imported.");
                    continue;
                }

                // The only job of this method is to mark the column and add the attribute.
                foreignKeyColumn.Column.ForeignKey = true;
                foreignKeyColumn.Attributes.Add(new ForeignKeyAttribute(tableName, toColumnName, keyName, ordinal, onUpdate, onDelete));
            }
        }

        return true;
    }

    private static ReferentialAction ParseReferentialAction(string? value)
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

    private Option<SQLiteProviderTableModelDraft, IDLOptionFailure> ParseTable(SQLiteProviderDatabaseDraft database, IDataLinqDataReader reader, DatabaseAccess dbAccess)
    {
        var type = reader.GetString(0) == "table" ? TableType.Table : TableType.View;
        var table = new SQLiteProviderTableDraft(reader.GetString(2), type);

        var csName = table.DbName.ToCSharpIdentifier(options.CapitaliseNames);

        if (type == TableType.View)
            table.Definition = ParseViewDefinition(reader.GetString(4));

        if (!dbAccess
            .ReadReader($"SELECT * FROM pragma_table_info(\"{table.DbName}\")")
            .Select(x => ParseColumn(table, x, dbAccess))
            .Transpose()
            .TryUnwrap(out var columns, out var columnFailure))
            return SingleOrAggregate(columnFailure);

        table.Columns.AddRange(columns);

        return new SQLiteProviderTableModelDraft(
            csName,
            new CsTypeDeclaration(csName, database.CsType.Namespace, ModelCsType.Class),
            table);
    }

    private static IDLOptionFailure SingleOrAggregate(IReadOnlyCollection<IDLOptionFailure> failures) =>
        failures.Count == 1
            ? failures.Single()
            : DLOptionFailure.AggregateFail(failures);

    private static string ParseViewDefinition(string definition)
    {
        definition = definition
            .ReplaceLineEndings(" ")
            .Replace("\"", @"\""");

        var selectIndex = definition.IndexOf("SELECT ", StringComparison.OrdinalIgnoreCase);

        if (selectIndex != -1)
            definition = definition.Substring(selectIndex);

        return definition;
    }

    private Option<SQLiteProviderValuePropertyDraft, IDLOptionFailure> ParseColumn(SQLiteProviderTableDraft table, IDataLinqDataReader reader, DatabaseAccess dbAccess)
    {
        var dbType = new DatabaseColumnType(DatabaseType.SQLite, reader.GetString(2).ToLower());

        if (string.IsNullOrEmpty(dbType.Name))
            dbType.SetName("text");

        var dbName = reader.GetString(1);

        var createStatement = dbAccess.ExecuteScalar<string>($"SELECT sql FROM sqlite_master WHERE type='table' AND name='{table.DbName}'");
        var hasAutoIncrement = false;

        if (createStatement != null)
        {
            var pattern = $@"\""({dbName})\""\s+INTEGER\s+PRIMARY\s+KEY\s+AUTOINCREMENT\b";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            hasAutoIncrement = regex.IsMatch(createStatement);
        }

        var primaryKey = reader.GetBoolean(5);
        var column = new SQLiteProviderColumnDraft(dbName)
        {
            AutoIncrement = hasAutoIncrement,
            Nullable = !primaryKey && reader.GetBoolean(3) == false,
            PrimaryKey = primaryKey
        };
        column.DbTypes.Add(dbType);

        if (!ParseCsType(dbType, table.DbName, dbName).TryUnwrap(out var csType, out var csTypeFailure))
            return csTypeFailure;

        if (!ParseCsTypeDeclaration(csType, table.DbName, dbName).TryUnwrap(out var csTypeDeclaration, out var valuePropertyFailure))
            return valuePropertyFailure;

        var valueProperty = new SQLiteProviderValuePropertyDraft(
            dbName.ToCSharpIdentifier(options.CapitaliseNames),
            csTypeDeclaration,
            column,
            CreateColumnAttributes(column).ToList())
        {
            CsNullable = column.Nullable || column.AutoIncrement,
            CsSize = MetadataTypeConverter.CsTypeSize(csType)
        };

        var defaultValue = ParseDefaultValue(table.DbName, column.DbName, reader, valueProperty.CsType, valueProperty.EnumProperty);
        if (defaultValue != null)
            valueProperty.Attributes.Add(defaultValue);

        return valueProperty;
    }

    private DefaultAttribute? ParseDefaultValue(string tableName, string columnName, IDataLinqDataReader reader, CsTypeDeclaration csType, EnumProperty? enumProperty)
    {
        var defaultOrdinal = reader.GetOrdinal("dflt_value");
        if (reader.IsDbNull(defaultOrdinal))
            return null;

        var rawDefault = reader.GetString(defaultOrdinal);
        if (string.IsNullOrWhiteSpace(rawDefault))
            return null;

        var normalizedExpression = UnwrapParenthesizedDefaultExpression(rawDefault).Trim();
        if (string.Equals(normalizedExpression, "NULL", StringComparison.OrdinalIgnoreCase))
            return null;

        if (IsCurrentTimestampDefault(normalizedExpression) ||
            (csType.Type == typeof(DateOnly) && IsCurrentDateDefault(normalizedExpression)) ||
            (csType.Type == typeof(TimeOnly) && IsCurrentTimeDefault(normalizedExpression)))
            return new DefaultCurrentTimestampAttribute();

        if (IsBlobLiteral(normalizedExpression))
        {
            options.Log?.Invoke($"Warning: Skipping unsupported SQLite blob default '{rawDefault}' for {tableName}.{columnName}.");
            return null;
        }

        if (!TryConvertDefaultValue(normalizedExpression, csType, enumProperty, out var value))
        {
            options.Log?.Invoke($"Warning: Skipping unsupported SQLite default '{rawDefault}' for {tableName}.{columnName}.");
            return null;
        }

        return new DefaultAttribute(value);
    }

    private static Option<string, IDLOptionFailure> ParseCsType(DatabaseColumnType dbType, string tableName, string columnName)
    {
        var lowerColumnName = columnName.ToLowerInvariant();

        // Name-based override for specific types
        if (dbType.Name == "text")
        {
            if (lowerColumnName.EndsWith("_date")) return "DateOnly";
            if (lowerColumnName.EndsWith("_time")) return "TimeOnly";
            if (lowerColumnName.EndsWith("_at") || lowerColumnName.Contains("datetime") || lowerColumnName.Contains("timestamp")) return "DateTime";
            if (lowerColumnName == "guid" || lowerColumnName.EndsWith("_guid") || lowerColumnName == "uuid" || lowerColumnName.EndsWith("_uuid")) return "Guid";
        }
        else if (dbType.Name == "integer")
        {
            if (lowerColumnName.StartsWith("is_") || lowerColumnName.StartsWith("has_")) return "bool";
        }
        else if (dbType.Name == "blob")
        {
            if (lowerColumnName == "guid" || lowerColumnName.EndsWith("_guid") || lowerColumnName == "uuid" || lowerColumnName.EndsWith("_uuid")) return "Guid";
        }

        // Default mapping based on affinity
        return dbType.Name.ToLower() switch
        {
            "integer" => "int",
            "real" => "double",
            "text" => "string",
            "blob" => "byte[]",
            _ => DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Unsupported SQLite column type '{dbType.Name}' for column '{tableName}.{columnName}'."),
        };
    }

    private static Option<CsTypeDeclaration, IDLOptionFailure> ParseCsTypeDeclaration(string csTypeName, string tableName, string columnName)
    {
        var type = MetadataTypeConverter.GetType(csTypeName);
        if (type == null)
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Unsupported C# type '{csTypeName}' for column '{tableName}.{columnName}'.");

        return new CsTypeDeclaration(type);
    }

    private static IEnumerable<Attribute> CreateColumnAttributes(SQLiteProviderColumnDraft column)
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

    private static bool TryConvertDefaultValue(string defaultValue, CsTypeDeclaration csType, EnumProperty? enumProperty, out object value)
    {
        value = null!;

        if (enumProperty != null)
        {
            if (TryParseIntegerLiteral(defaultValue, out var enumValue))
            {
                value = enumValue;
                return true;
            }

            return false;
        }

        if (TryParseSqlStringLiteral(defaultValue, out var stringValue))
            return TryConvertLiteralValue(stringValue, csType, out value);

        if (TryParseIntegerLiteral(defaultValue, out var integerValue))
            return TryConvertLiteralValue(integerValue.ToString(CultureInfo.InvariantCulture), csType, out value);

        if (TryParseRealLiteral(defaultValue, out var realValue))
            return TryConvertLiteralValue(realValue.ToString(CultureInfo.InvariantCulture), csType, out value);

        if (string.Equals(defaultValue, "TRUE", StringComparison.OrdinalIgnoreCase))
            return TryConvertLiteralValue("1", csType, out value);

        if (string.Equals(defaultValue, "FALSE", StringComparison.OrdinalIgnoreCase))
            return TryConvertLiteralValue("0", csType, out value);

        return false;
    }

    private static bool TryConvertLiteralValue(string literalValue, CsTypeDeclaration csType, out object value)
    {
        value = null!;

        if (csType.Type == typeof(string))
        {
            value = literalValue;
            return true;
        }

        if (csType.Type == typeof(char))
        {
            if (literalValue.Length != 1)
                return false;

            value = literalValue[0];
            return true;
        }

        if (csType.Type == typeof(bool))
        {
            if (literalValue == "0")
            {
                value = false;
                return true;
            }

            if (literalValue == "1")
            {
                value = true;
                return true;
            }

            return false;
        }

        if (csType.Type == typeof(DateOnly) && DateOnly.TryParse(literalValue, CultureInfo.InvariantCulture, out var dateOnlyValue))
        {
            value = dateOnlyValue;
            return true;
        }

        if (csType.Type == typeof(TimeOnly) && TimeOnly.TryParse(literalValue, CultureInfo.InvariantCulture, out var timeOnlyValue))
        {
            value = timeOnlyValue;
            return true;
        }

        if (csType.Type == typeof(DateTime) && DateTime.TryParse(literalValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeValue))
        {
            value = dateTimeValue;
            return true;
        }

        if (csType.Type == typeof(DateTimeOffset) && DateTimeOffset.TryParse(literalValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeOffsetValue))
        {
            value = dateTimeOffsetValue;
            return true;
        }

        if (csType.Type == typeof(TimeSpan) && TimeSpan.TryParse(literalValue, CultureInfo.InvariantCulture, out var timeSpanValue))
        {
            value = timeSpanValue;
            return true;
        }

        if (csType.Type == typeof(Guid) && Guid.TryParse(literalValue, out var guidValue))
        {
            value = guidValue;
            return true;
        }

        if (csType.Type == null || csType.Type == typeof(byte[]))
            return false;

        try
        {
            value = Convert.ChangeType(literalValue, csType.Type, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
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
        for (var i = 0; i < value.Length; i++)
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

    private static bool TryParseSqlStringLiteral(string defaultValue, out string value)
    {
        value = string.Empty;

        if (defaultValue.Length < 2 || defaultValue[0] != '\'' || defaultValue[^1] != '\'')
            return false;

        value = defaultValue[1..^1].Replace("''", "'");
        return true;
    }

    private static bool IsBlobLiteral(string defaultValue) =>
        defaultValue.StartsWith("X'", StringComparison.OrdinalIgnoreCase) &&
        defaultValue.EndsWith("'", StringComparison.Ordinal);

    private static bool TryParseIntegerLiteral(string defaultValue, out long value) =>
        long.TryParse(defaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseRealLiteral(string defaultValue, out double value) =>
        double.TryParse(defaultValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

    private static bool IsCurrentTimestampDefault(string defaultValue) =>
        string.Equals(defaultValue, "CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentDateDefault(string defaultValue) =>
        string.Equals(defaultValue, "CURRENT_DATE", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentTimeDefault(string defaultValue) =>
        string.Equals(defaultValue, "CURRENT_TIME", StringComparison.OrdinalIgnoreCase);

    private sealed class SQLiteProviderDatabaseDraft(string name, CsTypeDeclaration csType, string dbName)
    {
        public string Name { get; } = name;
        public CsTypeDeclaration CsType { get; } = csType;
        public string DbName { get; } = dbName;
        public List<SQLiteProviderTableModelDraft> TableModels { get; } = [];

        public MetadataDatabaseDraft ToMetadataDraft() => new(Name, CsType)
        {
            DbName = DbName,
            TableModels = TableModels.Select(x => x.ToMetadataDraft()).ToArray()
        };
    }

    private sealed class SQLiteProviderTableModelDraft(
        string csPropertyName,
        CsTypeDeclaration modelType,
        SQLiteProviderTableDraft table)
    {
        public string CsPropertyName { get; } = csPropertyName;
        public CsTypeDeclaration ModelType { get; } = modelType;
        public SQLiteProviderTableDraft Table { get; } = table;

        public MetadataTableModelDraft ToMetadataDraft() => new(
            CsPropertyName,
            new MetadataModelDraft(ModelType)
            {
                ValueProperties = Table.Columns.Select(x => x.ToMetadataDraft()).ToArray()
            },
            Table.ToMetadataDraft());
    }

    private sealed class SQLiteProviderTableDraft(string dbName, TableType type)
    {
        public string DbName { get; } = dbName;
        public TableType Type { get; } = type;
        public string? Definition { get; set; }
        public List<SQLiteProviderValuePropertyDraft> Columns { get; } = [];

        public MetadataTableDraft ToMetadataDraft() => new(DbName)
        {
            Type = Type,
            Definition = Definition
        };
    }

    private sealed class SQLiteProviderValuePropertyDraft(
        string propertyName,
        CsTypeDeclaration csType,
        SQLiteProviderColumnDraft column,
        List<Attribute> attributes)
    {
        public string PropertyName { get; } = propertyName;
        public CsTypeDeclaration CsType { get; } = csType;
        public SQLiteProviderColumnDraft Column { get; } = column;
        public List<Attribute> Attributes { get; } = attributes;
        public bool CsNullable { get; init; }
        public int? CsSize { get; init; }
        public EnumProperty? EnumProperty { get; init; }

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

    private sealed class SQLiteProviderColumnDraft(string dbName)
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
}
