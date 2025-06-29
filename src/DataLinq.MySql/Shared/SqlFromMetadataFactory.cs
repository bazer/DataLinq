using System;
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
            sql.CreateTable(table.DbName, x =>
            {
                CreateColumns(foreignKeyRestrict, x, table);
            });
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

        }

        sql.PrimaryKey(table.PrimaryKeyColumns.Select(x => x.DbName).ToArray());

        //foreach (var uniqueIndex in table.ColumnIndices.Where(x => x.Characteristic == IndexCharacteristic.Unique))
        //    sql.UniqueKey(uniqueIndex.Name, uniqueIndex.Columns.Select(x => x.DbName).ToArray());

        foreach (var foreignKey in table.Columns.Where(x => x.ForeignKey))
            foreach (var index in foreignKey.ColumnIndices)
                foreach (var relation in index.RelationParts)
                    sql.ForeignKey(relation, foreignKeyRestrict);

        foreach (var index in table.ColumnIndices.Where(x => x.Characteristic != IndexCharacteristic.PrimaryKey && x.Characteristic != IndexCharacteristic.ForeignKey && x.Characteristic != IndexCharacteristic.VirtualDataLinq))
            sql.Index(index.Name, index.Characteristic != IndexCharacteristic.Simple ? index.Characteristic.ToString().ToUpper() : null, index.Type.ToString().ToUpper(), index.Columns.Select(x => x.DbName).ToArray());
    }

    public abstract DatabaseColumnType GetDbType(ColumnDefinition column);

    public virtual string? GetDefaultValue(ColumnDefinition column)
    {
        var defaultAttr = column.ValueProperty.Attributes
            .Where(x => x is DefaultAttribute)
            .Select(x => x as DefaultAttribute)
            .FirstOrDefault();

        if (defaultAttr is DefaultCurrentTimestampAttribute)
            return "CURRENT_TIMESTAMP";

        return defaultAttr?.Value.ToString();
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
        "sbyte"     => new DatabaseColumnType(databaseType, "tinyint", signed: true),
        "byte"      => new DatabaseColumnType(databaseType, "tinyint", signed: false),
        "short"     => new DatabaseColumnType(databaseType, "smallint", signed: true),
        "ushort"    => new DatabaseColumnType(databaseType, "smallint", signed: false),
        "int"       => new DatabaseColumnType(databaseType, "int", signed: true),
        "uint"      => new DatabaseColumnType(databaseType, "int", signed: false),
        "long"      => new DatabaseColumnType(databaseType, "bigint", signed: true),
        "ulong"     => new DatabaseColumnType(databaseType, "bigint", signed: false),

        // Floating point and decimal types
        "decimal"   => new DatabaseColumnType(databaseType, "decimal", 18, 4), // Common default precision
        "float"     => new DatabaseColumnType(databaseType, "float"),
        "double"    => new DatabaseColumnType(databaseType, "double"),

        // String and character types
        "string"    => new DatabaseColumnType(databaseType, "varchar", 255), // Default length for strings
        "char"      => new DatabaseColumnType(databaseType, "char", 1),

        // Date and Time types
        "datetime"  => new DatabaseColumnType(databaseType, "datetime"),
        "dateonly"  => new DatabaseColumnType(databaseType, "date"),
        "timeonly"  => new DatabaseColumnType(databaseType, "time"),

        // Other types
        "bool"      => new DatabaseColumnType(databaseType, "bit", 1),
        "guid"      => new DatabaseColumnType(databaseType, "binary", 16),
        "byte[]"    => new DatabaseColumnType(databaseType, "blob"),
        
        // Enum handling
        "enum"      => new DatabaseColumnType(databaseType, "enum"), // Generic enum type, can be refined by provider

        _ => null
    };
}