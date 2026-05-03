using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.MariaDB;
using DataLinq.Metadata;
using DataLinq.Query;
using MySqlConnector;
using ThrowAway;

namespace DataLinq.MySql;

public abstract class SqlFromMetadataFactory : ISqlFromMetadataFactory
{
    protected abstract DatabaseType DatabaseType { get; }

    public static SqlFromMetadataFactory GetFactoryFromDatabaseType(DatabaseType databaseType)
    {
        if (databaseType == DatabaseType.MariaDB)
            return new SqlFromMariaDBFactory();
        if (databaseType == DatabaseType.MySQL)
            return new SqlFromMySqlFactory();

        throw new NotImplementedException($"No SQL factory for {databaseType}");
    }

    private static readonly string[] NoLengthTypes = new string[] { "text", "tinytext", "mediumtext", "longtext", "enum", "float", "double", "blob", "tinyblob", "mediumblob", "longblob" };

    public virtual Option<int, IDLOptionFailure> CreateDatabase(Sql sql, string databaseName, string connectionString, bool foreignKeyRestrict)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        var command = connection.CreateCommand();

        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{databaseName}`;\n" +
            $"USE `{databaseName}`;\n" +
            sql.Text;

        return command.ExecuteNonQuery();
    }

    public virtual Option<Sql, IDLOptionFailure> GetCreateTables(DatabaseDefinition metadata, bool foreignKeyRestrict)
    {
        var sql = new MySqlGeneration(2, '`', "/* Generated %datetime% by DataLinq */\n\n");
        //sql.CreateDatabase(metadata.DbName);

        foreach (var table in sql.SortTablesByForeignKeys(metadata.TableModels.Where(x => x.Table.Type == TableType.Table).Select(x => x.Table).ToList()))
        {
            var tableComment = GetComment(table.Model.Attributes);
            sql.CreateTable(table.DbName, x =>
            {
                CreateColumns(foreignKeyRestrict, x, table);
            }, tableComment == null ? null : $"COMMENT={QuoteSqlString(tableComment)}");
        }

        foreach (var view in sql.SortViewsByForeignKeys(metadata.TableModels.Where(x => x.Table.Type == TableType.View).Select(x => x.Table).Cast<ViewDefinition>().ToList()))
        {
            sql.CreateView(view.DbName, view.Definition);
        }

        return sql.sql;
    }

    protected virtual void CreateColumns(bool foreignKeyRestrict, SqlGeneration sql, TableDefinition table)
    {
        var longestName = table.Columns.Max(x => x.DbName.Length) + 1;
        foreach (var column in table.Columns.OrderBy(x => x.Index))
        {
            var dbType = GetDbType(column);
            //var dbType = column.DbTypes.Single(x => x.DatabaseType == DatabaseType.MySQL);

            var row = sql.NewRow().Indent()
                .ColumnName(column.DbName)
                .Type(dbType.Name.ToUpper(), column.DbName, longestName);

            if (dbType.Name == "enum" && column.ValueProperty.EnumProperty.HasValue)
                sql.EnumValues(column.ValueProperty.EnumProperty.Value.CsValuesOrDbValues.Select(x => x.name));

            if (!NoLengthTypes.Contains(dbType.Name.ToLower()) && dbType.Length.HasValue && dbType.Length != 0)
                sql.TypeLength(dbType.Length, dbType.Decimals);

            var defaultValue = GetDefaultValue(column);
            if (defaultValue != null)
                row.DefaultValue(defaultValue);

            sql.Unsigned(dbType.Signed);
            sql.Nullable(column.Nullable)
                .Autoincrement(column.AutoIncrement);

            var comment = GetComment(column.ValueProperty.Attributes);
            if (comment != null)
                row.Space().Add($"COMMENT {QuoteSqlString(comment)}");
        }

        sql.PrimaryKey(table.PrimaryKeyColumns.Select(x => x.DbName).ToArray());

        //foreach (var uniqueIndex in table.ColumnIndices.Where(x => x.Characteristic == IndexCharacteristic.Unique))
        //    sql.UniqueKey(uniqueIndex.Name, uniqueIndex.Columns.Select(x => x.DbName).ToArray());

        foreach (var relation in table.ColumnIndices
            .Where(x => x.Characteristic == IndexCharacteristic.ForeignKey)
            .SelectMany(x => x.RelationParts)
            .Where(x => x.Type == RelationPartType.ForeignKey)
            .DistinctBy(x => x.Relation))
        {
            sql.ForeignKey(relation, foreignKeyRestrict);
        }

        foreach (var index in table.ColumnIndices.Where(x => x.Characteristic != IndexCharacteristic.PrimaryKey && x.Characteristic != IndexCharacteristic.ForeignKey && x.Characteristic != IndexCharacteristic.VirtualDataLinq))
            sql.Index(index.Name, index.Characteristic != IndexCharacteristic.Simple ? index.Characteristic.ToString().ToUpper() : null, index.Type.ToString().ToUpper(), index.Columns.Select(x => x.DbName).ToArray());

        foreach (var check in GetCheckAttributes(table.Model.Attributes))
            sql.Check(check.Name, check.Expression);
    }

    public abstract DatabaseColumnType GetDbType(ColumnDefinition column);

    public virtual string? GetDefaultValue(ColumnDefinition column)
    {
        var defaultAttr = column.ValueProperty.Attributes
            .Where(x => x is DefaultAttribute)
            .Select(x => x as DefaultAttribute)
            .FirstOrDefault();

        if (defaultAttr is DefaultCurrentTimestampAttribute)
        {
            return column.ValueProperty.CsType.Name switch
            {
                "DateOnly" => "CURRENT_DATE",
                "TimeOnly" => "CURRENT_TIME",
                _ => "CURRENT_TIMESTAMP"
            };
        }

        if (defaultAttr is DefaultNewUUIDAttribute)
            return "UUID()";

        if (defaultAttr is DefaultSqlAttribute defaultSql)
            return defaultSql.DatabaseType is DatabaseType.Default || defaultSql.DatabaseType == DatabaseType
                ? defaultSql.Expression
                : null;

        if (column.ValueProperty.EnumProperty.HasValue)
        {
            var enumDefaultValue = ResolveEnumDefaultValue(column.ValueProperty, defaultAttr);
            if (enumDefaultValue != null)
                return $"'{enumDefaultValue.Replace("'", "''")}'";
        }

        if (defaultAttr == null)
            return null;

        var dbType = GetDbType(column);

        return column.ValueProperty.CsType.Name switch
        {
            "string" => QuoteSqlString(Convert.ToString(defaultAttr.Value, CultureInfo.InvariantCulture) ?? string.Empty),
            "char" => QuoteSqlString(Convert.ToString(defaultAttr.Value, CultureInfo.InvariantCulture) ?? string.Empty),
            "bool" => FormatBooleanDefaultValue(defaultAttr.Value, dbType),
            "sbyte" => Convert.ToSByte(defaultAttr.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            "byte" => Convert.ToByte(defaultAttr.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            "short" => Convert.ToInt16(defaultAttr.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            "ushort" => Convert.ToUInt16(defaultAttr.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            "int" => Convert.ToInt32(defaultAttr.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            "uint" => Convert.ToUInt32(defaultAttr.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            "long" => Convert.ToInt64(defaultAttr.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            "ulong" => Convert.ToUInt64(defaultAttr.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            "float" => Convert.ToSingle(defaultAttr.Value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            "double" => Convert.ToDouble(defaultAttr.Value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            "decimal" => Convert.ToDecimal(defaultAttr.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            "DateOnly" => QuoteSqlString(((DateOnly)defaultAttr.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            "TimeOnly" => QuoteSqlString(((TimeOnly)defaultAttr.Value).ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
            "DateTime" => QuoteSqlString(((DateTime)defaultAttr.Value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            "DateTimeOffset" => QuoteSqlString(((DateTimeOffset)defaultAttr.Value).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)),
            "TimeSpan" => QuoteSqlString(((TimeSpan)defaultAttr.Value).ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture)),
            "Guid" or "System.Guid" => FormatGuidDefaultValue((Guid)defaultAttr.Value, dbType),
            _ => Convert.ToString(defaultAttr.Value, CultureInfo.InvariantCulture)
        };
    }

    private string? GetComment(Attribute[] attributes)
    {
        var comments = attributes.OfType<CommentAttribute>();
        var typedComment = comments.FirstOrDefault(x => x.DatabaseType == DatabaseType);

        return typedComment?.Text
            ?? comments.FirstOrDefault(x => x.DatabaseType == DatabaseType.Default)?.Text;
    }

    private IEnumerable<CheckAttribute> GetCheckAttributes(Attribute[] attributes)
    {
        var checks = attributes.OfType<CheckAttribute>().ToList();
        var typedChecks = checks
            .Where(x => x.DatabaseType == DatabaseType)
            .ToList();

        return typedChecks.Count > 0
            ? typedChecks
            : checks.Where(x => x.DatabaseType == DatabaseType.Default);
    }

    private static string QuoteSqlString(string value) => $"'{value.Replace("'", "''")}'";

    private static string FormatBooleanDefaultValue(object value, DatabaseColumnType dbType)
    {
        var boolValue = Convert.ToBoolean(value, CultureInfo.InvariantCulture);

        return dbType.Name.Equals("bit", StringComparison.OrdinalIgnoreCase)
            ? (boolValue ? "b'1'" : "b'0'")
            : (boolValue ? "1" : "0");
    }

    private static string FormatGuidDefaultValue(Guid value, DatabaseColumnType dbType)
    {
        if (dbType.Name.Equals("uuid", StringComparison.OrdinalIgnoreCase) ||
            (dbType.Name.Equals("char", StringComparison.OrdinalIgnoreCase) && dbType.Length == 36) ||
            (dbType.Name.Equals("varchar", StringComparison.OrdinalIgnoreCase) && dbType.Length == 36))
        {
            return QuoteSqlString(value.ToString());
        }

        if (dbType.Name.Equals("binary", StringComparison.OrdinalIgnoreCase) && dbType.Length == 16)
            return $"X'{Convert.ToHexString(value.ToByteArray())}'";

        return QuoteSqlString(value.ToString());
    }

    private static string? ResolveEnumDefaultValue(ValueProperty property, DefaultAttribute? defaultAttr)
    {
        if (defaultAttr == null || !property.EnumProperty.HasValue)
            return null;

        var enumProperty = property.EnumProperty.Value;

        if (TryGetEnumNumericValue(enumProperty, defaultAttr, out var numericValue))
        {
            return enumProperty.DbValuesOrCsValues
                .FirstOrDefault(x => x.value == numericValue)
                .name;
        }

        return null;
    }

    private static bool TryGetEnumNumericValue(EnumProperty enumProperty, DefaultAttribute defaultAttr, out int numericValue)
    {
        if (defaultAttr.Value is Enum enumValue)
        {
            numericValue = Convert.ToInt32(enumValue);
            return true;
        }

        if (defaultAttr.Value is string stringValue)
        {
            if (int.TryParse(stringValue, out numericValue))
                return true;

            var memberName = stringValue.Split('.').Last();
            var enumMatch = enumProperty.CsValuesOrDbValues.FirstOrDefault(x => x.name == memberName)
                .name != null
                    ? enumProperty.CsValuesOrDbValues.First(x => x.name == memberName)
                    : enumProperty.DbValuesOrCsValues.FirstOrDefault(x => x.name == stringValue);

            if (enumMatch.name != null)
            {
                numericValue = enumMatch.value;
                return true;
            }
        }

        try
        {
            numericValue = Convert.ToInt32(defaultAttr.Value);
            return true;
        }
        catch
        {
            numericValue = default;
            return false;
        }
    }

    protected virtual DatabaseColumnType? ParseDefaultType(DatabaseColumnType defaultType, DatabaseType databaseType)
    {
        var newTypeName = defaultType.Name.ToLower() switch
        {
            "integer" => "int",
            "big-integer" => "bigint",
            "decimal" => "decimal",
            "float" => "float",
            "double" => "double",
            "text" => "text",
            "boolean" => "bit",
            "datetime" => "datetime",
            "timestamp" => "timestamp",
            "date" => "date",
            "time" => "time",
            "uuid" => "binary",
            "blob" => "blob",
            "json" => "json",
            "xml" => "longtext",
            _ => null,
        };

        return newTypeName == null
            ? null
            : new DatabaseColumnType(databaseType, newTypeName, defaultType.Length, defaultType.Decimals, defaultType.Signed);
    }

    protected virtual DatabaseColumnType? ParseSQLiteType(DatabaseColumnType sqliteType, DatabaseType databaseType)
    {
        var newTypeName = sqliteType.Name.ToLower() switch
        {
            "integer" => "int",
            "text" => "varchar",
            "real" => "double",
            "blob" => "binary",
            _ => null
        };

        return newTypeName == null
            ? null
            : new DatabaseColumnType(databaseType, newTypeName, sqliteType.Length, sqliteType.Decimals, sqliteType.Signed);
    }

    protected virtual DatabaseColumnType? GetDbTypeFromCsType(ValueProperty property, DatabaseType databaseType)
    {
        var csTypeName = property.CsType.Name.ToLower();

        return csTypeName switch
        {
            // Integer types
            "sbyte" => new DatabaseColumnType(databaseType, "tinyint", signed: true),
            "byte" => new DatabaseColumnType(databaseType, "tinyint", signed: false),
            "short" => new DatabaseColumnType(databaseType, "smallint", signed: true),
            "ushort" => new DatabaseColumnType(databaseType, "smallint", signed: false),
            "int" => new DatabaseColumnType(databaseType, "int", signed: true),
            "uint" => new DatabaseColumnType(databaseType, "int", signed: false),
            "long" => new DatabaseColumnType(databaseType, "bigint", signed: true),
            "ulong" => new DatabaseColumnType(databaseType, "bigint", signed: false),

            // Floating point and decimal types
            "decimal" => new DatabaseColumnType(databaseType, "decimal", 18, 4), // Common default precision
            "float" => new DatabaseColumnType(databaseType, "float"),
            "double" => new DatabaseColumnType(databaseType, "double"),

            // String and character types
            "string" => new DatabaseColumnType(databaseType, "varchar", 255), // Default length for strings
            "char" => new DatabaseColumnType(databaseType, "char", 1),

            // Date and Time types
            "datetime" => new DatabaseColumnType(databaseType, "datetime"),
            "dateonly" => new DatabaseColumnType(databaseType, "date"),
            "timeonly" => new DatabaseColumnType(databaseType, "time"),

            // Other types
            "bool" => new DatabaseColumnType(databaseType, "bit", 1),
            "guid" => new DatabaseColumnType(databaseType, "binary", 16),
            "byte[]" => new DatabaseColumnType(databaseType, "blob"),

            // Enum handling
            "enum" => new DatabaseColumnType(databaseType, "enum"), // Generic enum type, can be refined by provider

            _ => null
        };
    }
}
