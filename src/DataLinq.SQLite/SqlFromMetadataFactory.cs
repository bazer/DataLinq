using System;
using System.Data;
using System.IO;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.ErrorHandling;
using DataLinq.Exceptions;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.Query;
using Microsoft.Data.Sqlite;
using ThrowAway;

namespace DataLinq.SQLite;

public class SqlFromMetadataFactory : ISqlFromMetadataFactory
{
    public Option<Sql, IDLOptionFailure> GetCreateTables(DatabaseDefinition metadata, bool foreignKeyRestrict)
    {
        var sql = new SQLiteGeneration(2, '"', "/* Generated %datetime% by DataLinq */\n\n");
        foreach (var table in sql.SortTablesByForeignKeys(metadata.TableModels.Select(x => x.Table).Where(x => x.Type == TableType.Table).ToList()))
        {
            sql.CreateTable(table.DbName, x =>
            {
                var longestName = table.Columns.Max(x => x.DbName.Length) + 1;
                foreach (var column in table.Columns.OrderBy(x => x.Index))
                {
                    var dbType = GetDbType(column);

                    sql.NewRow().Indent()
                        .ColumnName(column.DbName)
                        .Type(dbType.Name.ToUpper(), column.DbName, longestName)
                        .Add(column.PrimaryKey && table.PrimaryKeyColumns.Length == 1 ? " PRIMARY KEY" : "")
                        .Add(column.AutoIncrement ? " AUTOINCREMENT" : "");

                    sql.Nullable(column.PrimaryKey ? false : column.Nullable);
                }

                if (table.PrimaryKeyColumns.Length > 1)
                    sql.PrimaryKey(table.PrimaryKeyColumns.Select(x => x.DbName).ToArray());

                //{
                //    sql.NewRow().Indent()
                //        .Add($"PRIMARY KEY ({table.PrimaryKeyColumns.Select(x => x.DbName).ToJoinedString(", ")})");
                //}

                foreach (var uniqueIndex in table.ColumnIndices.Where(x => x.Characteristic == IndexCharacteristic.Unique))
                    sql.UniqueKey(uniqueIndex.Name, uniqueIndex.Columns.Select(x => x.DbName).ToArray());

                foreach (var foreignKey in table.Columns.Where(x => x.ForeignKey))
                    foreach (var index in foreignKey.ColumnIndices)
                        foreach (var relation in index.RelationParts)
                            sql.ForeignKey(relation, foreignKeyRestrict);
            });
        }

        foreach (var view in sql.SortViewsByForeignKeys(metadata.TableModels.Select(x => x.Table).Where(x => x.Type == TableType.View).Cast<ViewDefinition>().ToList()))
        {
            if (string.IsNullOrWhiteSpace(view.Definition))
                return DLOptionFailure.Fail($"View '{view.DbName}' does not have a Definition, can't create view. Add the 'DefinitionAttribute' to the view.");

            sql.CreateView(view.DbName, view.Definition);
        }

        return sql.sql;
    }

    public Option<int, IDLOptionFailure> CreateDatabase(Sql sql, string databaseName, string connectionString, bool foreignKeyRestrict)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var file = builder.DataSource;

        if (file != "memory")
        {
            if (File.Exists(file))
                return DLOptionFailure.Fail($"Failed to create new SQLite database file '{file}', it already exists.");

            File.WriteAllBytes(file, []);
        }

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = sql.Text;

        return command.ExecuteNonQuery();
    }

    public static DatabaseColumnType GetDbType(ColumnDefinition column)
    {
        if (column.DbTypes.Any(x => x.DatabaseType == DatabaseType.SQLite))
            return column.DbTypes.First(x => x.DatabaseType == DatabaseType.SQLite);

        var type = column.DbTypes
            .Select(x => TryGetColumnType(x))
            .Concat(GetDbTypeFromCsType(column.ValueProperty).Yield())
            .Where(x => x != null)
            .FirstOrDefault();

        if (type == null)
            throw new Exception($"Could not find a SQLite database type for '{column.Table.Model.CsType.Name}.{column.ValueProperty.PropertyName}'");

        return type;
    }

    private static DatabaseColumnType? TryGetColumnType(DatabaseColumnType dbType)
    {
        string? type = null;

        if (dbType.DatabaseType == DatabaseType.Default)
            type = ParseDefaultType(dbType.Name);
        else if (dbType.DatabaseType == DatabaseType.MySQL)
            type = ParseMySqlType(dbType.Name);

        return type == null
            ? null
            : new DatabaseColumnType(DatabaseType.SQLite, type, dbType.Length, dbType.Decimals, dbType.Signed);
    }

    private static DatabaseColumnType? GetDbTypeFromCsType(ValueProperty property)
    {
        var type = ParseCsType(property.CsType.Name);

        return type == null
            ? null 
            : new DatabaseColumnType(DatabaseType.SQLite, type);
    }

    private static string? ParseDefaultType(string defaultType)
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

    private static string? ParseMySqlType(string mysqlType)
    {
        return mysqlType.ToLower() switch
        {
            "int" => "integer",
            "tinyint" => "integer",
            "mediumint" => "integer",
            "varchar" => "text",
            "text" => "text",
            "mediumtext" => "text",
            "bit" => "integer",
            "double" => "real",
            "datetime" => "text",
            "timestamp" => "text",
            "date" => "text",
            "float" => "real",
            "bigint" => "integer",
            "char" => "text",
            "binary" => "blob",
            "enum" => "integer",
            "longtext" => "text",
            "decimal" => "real",
            "blob" => "blob",
            "smallint" => "integer",
            _ => null
            //_ => throw new NotImplementedException($"Unknown type '{mysqlType}'"),
        };
    }

    private static string? ParseCsType(string csType)
    {
        return csType.ToLower() switch
        {
            "int" => "integer",
            "double" => "real",
            "string" => "text",
            "byte[]" => "blob",
            _ => null
            //_ => throw new NotImplementedException($"Unknown type '{csType}'"),
        };
    }
}

public class SQLiteGeneration : SqlGeneration
{
    public SQLiteGeneration(int indentationSpaces = 4, char quoteChar = '`', string generatedText = "") : base(indentationSpaces, quoteChar, generatedText)
    {
    }

    public override SqlGeneration UniqueKey(string name, params string[] columns)
        => NewRow().Indent().Add($"CONSTRAINT {QuotedString(name)} UNIQUE {ParenthesisList(columns)}");
}