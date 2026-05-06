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

        var database = new DatabaseDefinition(name, new CsTypeDeclaration(csTypeName, csNamespace, ModelCsType.Class), dbName);
        if (!dbAccess
            .ReadReader("SELECT *\r\nFROM sqlite_master m\r\nWHERE\r\nm.type <> 'index' AND\r\nm.tbl_name <> 'sqlite_sequence'")
            .Select(x => ParseTable(database, x, dbAccess))
            .Transpose()
            .TryUnwrap(out var tableModels, out var tableFailure))
            return SingleOrAggregate(tableFailure);

        database.SetTableModels(tableModels.Where(IsTableOrViewInOptionsList));

        var missingTables = FindMissingTablesOrViewInOptionsList(database.TableModels).ToList();
        if (missingTables.Count != 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Could not find the specified tables or views: {missingTables.ToJoinedString(", ")}");

        if (database.TableModels.Length == 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"No tables or views found in database '{dbName}'. Please check the connection string and database name.");

        ParseIndices(database, dbAccess);
        if (!ParseRelations(database, dbAccess).TryUnwrap(out _, out var relationFailure))
            return relationFailure;

        return new MetadataDefinitionFactory()
            .BuildProviderMetadata(MetadataDefinitionDraft.FromMutableMetadata(database));
    });

    private IEnumerable<string> FindMissingTablesOrViewInOptionsList(TableModel[] tableModels)
    {
        foreach (var tableName in options.Include ?? [])
        {
            if (!tableModels.Any(x => tableName.Equals(x.Table.DbName, StringComparison.OrdinalIgnoreCase)))
                yield return tableName;
        }
    }
    private bool IsTableOrViewInOptionsList(TableModel tableModel)
    {
        if (options.Include == null || !options.Include.Any())
            return true;

        return options.Include.Any(x => x.Equals(tableModel.Table.DbName, StringComparison.OrdinalIgnoreCase));
    }

    private void ParseIndices(DatabaseDefinition database, DatabaseAccess dbAccess)
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

                var indexColumns = new List<ColumnDefinition>();
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
                        .Table.Columns.SingleOrDefault(x => x.DbName == columnName);

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

                var columnNames = indexColumns.Select(x => x.DbName).ToArray();
                foreach (var column in indexColumns)
                {
                    column.ValueProperty.AddAttribute(new IndexAttribute(name, indexCharacteristic, IndexType.BTREE, columnNames));
                }
            }
        }
    }

    private static string GetAutoIndexName(TableDefinition table, IReadOnlyList<ColumnDefinition> columns, IndexCharacteristic characteristic)
    {
        if (columns.Count == 1)
            return columns[0].DbName;

        var suffix = characteristic == IndexCharacteristic.Unique
            ? "unique"
            : characteristic.ToString().ToLowerInvariant();

        return $"{table.DbName}_{string.Join("_", columns.Select(x => x.DbName))}_{suffix}";
    }

    private static string QuoteSqlLiteral(string value) => $"'{value.Replace("'", "''")}'";

    private Option<bool, IDLOptionFailure> ParseRelations(DatabaseDefinition database, DatabaseAccess dbAccess)
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
                    .Table.Columns.SingleOrDefault(x => x.DbName == fromColumnName);

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
                    var primaryKeyColumns = candidateTable.Columns.Where(x => x.PrimaryKey).ToArray();
                    if (primaryKeyColumns.Length != 1)
                    {
                        return DLOptionFailure.Fail(
                            DLFailureType.InvalidModel,
                            $"SQLite foreign key '{keyName}' on table '{tableModel.Table.DbName}' omits the referenced column, but referenced table '{tableName}' does not have exactly one imported primary-key column.");
                    }

                    toColumnName = primaryKeyColumns[0].DbName;
                }

                var candidateColumn = candidateTable.Columns.SingleOrDefault(x => x.DbName == toColumnName);
                if (candidateColumn == null)
                {
                    options.Log?.Invoke($"Warning: Skipping foreign key '{keyName}' on table '{tableModel.Table.DbName}' because referenced column '{tableName}.{toColumnName}' was not imported.");
                    continue;
                }

                // The only job of this method is to mark the column and add the attribute.
                foreignKeyColumn.SetForeignKey();
                foreignKeyColumn.ValueProperty.AddAttribute(new ForeignKeyAttribute(tableName, toColumnName, keyName, ordinal, onUpdate, onDelete));
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

    private Option<TableModel, IDLOptionFailure> ParseTable(DatabaseDefinition database, IDataLinqDataReader reader, DatabaseAccess dbAccess)
    {
        var type = reader.GetString(0) == "table" ? TableType.Table : TableType.View;
        var table = type == TableType.Table
             ? new TableDefinition(reader.GetString(2))
             : new ViewDefinition(reader.GetString(2));

        var csName = table.DbName.ToCSharpIdentifier(options.CapitaliseNames);

        var tableModel = new TableModel(csName, database, table, csName);

        if (table is ViewDefinition view)
            view.SetDefinition(ParseViewDefinition(reader.GetString(4)));

        if (!dbAccess
            .ReadReader($"SELECT * FROM pragma_table_info(\"{table.DbName}\")")
            .Select(x => ParseColumn(table, x, dbAccess))
            .Transpose()
            .TryUnwrap(out var columns, out var columnFailure))
            return SingleOrAggregate(columnFailure);

        table.SetColumns(columns);

        return tableModel;
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

    private Option<ColumnDefinition, IDLOptionFailure> ParseColumn(TableDefinition table, IDataLinqDataReader reader, DatabaseAccess dbAccess)
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

        var column = new ColumnDefinition(dbName, table);
        var primaryKey = reader.GetBoolean(5);
        column.SetNullable(!primaryKey && reader.GetBoolean(3) == false);
        column.SetAutoIncrement(hasAutoIncrement);
        column.SetPrimaryKey(primaryKey);
        column.AddDbType(dbType);

        if (!ParseCsType(dbType, table.DbName, dbName).TryUnwrap(out var csType, out var csTypeFailure))
            return csTypeFailure;

        if (!MetadataFactory.TryAttachValueProperty(column, csType, options.CapitaliseNames).TryUnwrap(out var valueProperty, out var valuePropertyFailure))
            return valuePropertyFailure;

        var defaultValue = ParseDefaultValue(table, column, reader, valueProperty);
        if (defaultValue != null)
            valueProperty.AddAttribute(defaultValue);

        return column;
    }

    private DefaultAttribute? ParseDefaultValue(TableDefinition table, ColumnDefinition column, IDataLinqDataReader reader, ValueProperty property)
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
            (property.CsType.Type == typeof(DateOnly) && IsCurrentDateDefault(normalizedExpression)) ||
            (property.CsType.Type == typeof(TimeOnly) && IsCurrentTimeDefault(normalizedExpression)))
            return new DefaultCurrentTimestampAttribute();

        if (IsBlobLiteral(normalizedExpression))
        {
            options.Log?.Invoke($"Warning: Skipping unsupported SQLite blob default '{rawDefault}' for {table.DbName}.{column.DbName}.");
            return null;
        }

        if (!TryConvertDefaultValue(normalizedExpression, property, out var value))
        {
            options.Log?.Invoke($"Warning: Skipping unsupported SQLite default '{rawDefault}' for {table.DbName}.{column.DbName}.");
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

    private static bool TryConvertDefaultValue(string defaultValue, ValueProperty property, out object value)
    {
        value = null!;

        if (property.EnumProperty != null)
        {
            if (TryParseIntegerLiteral(defaultValue, out var enumValue))
            {
                value = enumValue;
                return true;
            }

            return false;
        }

        if (TryParseSqlStringLiteral(defaultValue, out var stringValue))
            return TryConvertLiteralValue(stringValue, property, out value);

        if (TryParseIntegerLiteral(defaultValue, out var integerValue))
            return TryConvertLiteralValue(integerValue.ToString(CultureInfo.InvariantCulture), property, out value);

        if (TryParseRealLiteral(defaultValue, out var realValue))
            return TryConvertLiteralValue(realValue.ToString(CultureInfo.InvariantCulture), property, out value);

        if (string.Equals(defaultValue, "TRUE", StringComparison.OrdinalIgnoreCase))
            return TryConvertLiteralValue("1", property, out value);

        if (string.Equals(defaultValue, "FALSE", StringComparison.OrdinalIgnoreCase))
            return TryConvertLiteralValue("0", property, out value);

        return false;
    }

    private static bool TryConvertLiteralValue(string literalValue, ValueProperty property, out object value)
    {
        value = null!;

        if (property.CsType.Type == typeof(string))
        {
            value = literalValue;
            return true;
        }

        if (property.CsType.Type == typeof(char))
        {
            if (literalValue.Length != 1)
                return false;

            value = literalValue[0];
            return true;
        }

        if (property.CsType.Type == typeof(bool))
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

        if (property.CsType.Type == typeof(DateOnly) && DateOnly.TryParse(literalValue, CultureInfo.InvariantCulture, out var dateOnlyValue))
        {
            value = dateOnlyValue;
            return true;
        }

        if (property.CsType.Type == typeof(TimeOnly) && TimeOnly.TryParse(literalValue, CultureInfo.InvariantCulture, out var timeOnlyValue))
        {
            value = timeOnlyValue;
            return true;
        }

        if (property.CsType.Type == typeof(DateTime) && DateTime.TryParse(literalValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeValue))
        {
            value = dateTimeValue;
            return true;
        }

        if (property.CsType.Type == typeof(DateTimeOffset) && DateTimeOffset.TryParse(literalValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeOffsetValue))
        {
            value = dateTimeOffsetValue;
            return true;
        }

        if (property.CsType.Type == typeof(TimeSpan) && TimeSpan.TryParse(literalValue, CultureInfo.InvariantCulture, out var timeSpanValue))
        {
            value = timeSpanValue;
            return true;
        }

        if (property.CsType.Type == typeof(Guid) && Guid.TryParse(literalValue, out var guidValue))
        {
            value = guidValue;
            return true;
        }

        if (property.CsType.Type == null || property.CsType.Type == typeof(byte[]))
            return false;

        try
        {
            value = Convert.ChangeType(literalValue, property.CsType.Type, CultureInfo.InvariantCulture);
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
}
