using System;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.Query;
using MySqlConnector;
using ThrowAway;

namespace DataLinq.MySql;

public class SqlFromMetadataFactory : ISqlFromMetadataFactory
{
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

    public virtual DatabaseColumnType GetDbType(ColumnDefinition column)
    {
        if (column.DbTypes.Any(x => x.DatabaseType == DatabaseType.MySQL))
            return column.DbTypes.First(x => x.DatabaseType == DatabaseType.MySQL);

        var type = column.DbTypes
            .Select(x => TryGetColumnType(x))
            .Concat(GetDbTypeFromCsType(column.ValueProperty).Yield())
            .Where(x => x != null)
            .FirstOrDefault();

        if (type == null)
            throw new Exception($"Could not find a MySQL database type for '{column.Table.Model.CsType.Name}.{column.ValueProperty.PropertyName}'");

        return type;
    }

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

    protected virtual DatabaseColumnType? TryGetColumnType(DatabaseColumnType dbType)
    {
        string? type = null;

        if (dbType.DatabaseType == DatabaseType.Default)
            type = ParseDefaultType(dbType.Name);
        else if (dbType.DatabaseType == DatabaseType.SQLite)
            type = ParseSQLiteType(dbType.Name);

        return type == null
            ? null
            : new DatabaseColumnType(DatabaseType.MySQL, type, dbType.Length, dbType.Decimals, dbType.Signed);
    }

    protected virtual DatabaseColumnType? GetDbTypeFromCsType(ValueProperty property)
    {
        var type = ParseCsType(property.CsType.Name);

        return type == null
            ? null
            : new DatabaseColumnType(DatabaseType.MySQL, type);
    }

    protected virtual string? ParseDefaultType(string defaultType)
    {
        return defaultType.ToLower() switch
        {
            "integer" => "integer",
            "int" => "integer",
            "tinyint" => "integer",
            "mediumint" => "integer",
            "bit" => "integer",
            "bigint" => "integer",
            "smallint" => "integer",
            "enum" => "integer",
            "real" => "real",
            "double" => "real",
            "float" => "real",
            "decimal" => "real",
            "varchar" => "text",
            "text" => "text",
            "mediumtext" => "text",
            "datetime" => "text",
            "timestamp" => "text",
            "date" => "text",
            "char" => "text",
            "longtext" => "text",
            "binary" => "blob",
            "blob" => "blob",
            _ => null
            //_ => throw new NotImplementedException($"Unknown type '{mysqlType}'"),
        };
    }

    protected virtual string? ParseSQLiteType(string sqliteType)
    {
        return sqliteType.ToLower() switch
        {
            "integer" => "int",
            "text" => "varchar",
            "real" => "double",
            "blob" => "binary",
            _ => null
        };
    }

    protected virtual string? ParseCsType(string csType)
    {
        return csType.ToLower() switch
        {
            "int" => "int",
            "string" => "varchar",
            "bool" => "bit",
            "double" => "double",
            "datetime" => "datetime",
            "dateonly" => "date",
            "float" => "float",
            "long" => "bigint",
            "guid" => "binary(16)",
            "enum" => "enum",
            "decimal" => "decimal",
            "byte[]" => "blob",
            _ => null
        };
    }
}