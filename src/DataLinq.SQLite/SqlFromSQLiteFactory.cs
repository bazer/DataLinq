using System;
using System.Data;
using System.IO;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.Query;
using Microsoft.Data.Sqlite;
using ThrowAway;

namespace DataLinq.SQLite;

public class SqlFromSQLiteFactory : ISqlFromMetadataFactory
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

        if (file != "memory" && file != ":memory:" && !File.Exists(file))
        {
            File.WriteAllBytes(file, []);
        }

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = sql.Text;

        return command.ExecuteNonQuery();
    }

    public virtual DatabaseColumnType GetDbType(ColumnDefinition column)
    {
        if (column.DbTypes.Any(x => x.DatabaseType == DatabaseType.SQLite))
            return column.DbTypes.First(x => x.DatabaseType == DatabaseType.SQLite);

        var type = column.DbTypes
            .Select(x => TryGetColumnType(x))
            .Concat(GetDbTypeFromCsType(column.ValueProperty, DatabaseType.SQLite).Yield())
            .Where(x => x != null)
            .FirstOrDefault();

        if (type == null)
            throw new Exception($"Could not find an SQLite database type for '{column.Table.Model.CsType.Name}.{column.ValueProperty.PropertyName}'");

        return type;
    }

    protected virtual DatabaseColumnType? TryGetColumnType(DatabaseColumnType dbType)
    {
        return dbType.DatabaseType switch
        {
            DatabaseType.Default => ParseDefaultType(dbType),
            DatabaseType.MySQL => ParseMySQLType(dbType),
            DatabaseType.MariaDB => ParseMariaDBType(dbType),
            _ => null,
        };
    }

    protected virtual DatabaseColumnType? ParseDefaultType(DatabaseColumnType defaultType)
    {
        var newTypeName = defaultType.Name.ToLower() switch
        {
            "integer" or "big-integer" or "boolean" => "INTEGER",
            "decimal" or "float" or "double" => "REAL",
            "text" or "json" or "xml" => "TEXT",
            "datetime" or "timestamp" or "date" or "time" or "uuid" => "TEXT",
            "blob" => "BLOB",
            _ => null,
        };

        return newTypeName == null ? null : new DatabaseColumnType(DatabaseType.SQLite, newTypeName, defaultType.Length, defaultType.Decimals, defaultType.Signed);
    }

    protected virtual DatabaseColumnType? ParseMySQLType(DatabaseColumnType mysqlType)
    {
        var newTypeName = mysqlType.Name.ToLower() switch
        {
            "int" or "tinyint" or "smallint" or "mediumint" or "bigint" or "bit" or "year" or "enum" => "INTEGER",
            "double" or "float" or "decimal" => "REAL",
            "varchar" or "text" or "mediumtext" or "longtext" or "char" or "datetime" or "timestamp" or "date" or "time" or "json" => "TEXT",
            "binary" or "varbinary" or "blob" or "tinyblob" or "mediumblob" or "longblob" => "BLOB",
            _ => null
        };

        return newTypeName == null ? null : new DatabaseColumnType(DatabaseType.SQLite, newTypeName, mysqlType.Length, mysqlType.Decimals, mysqlType.Signed);
    }

    protected virtual DatabaseColumnType? ParseMariaDBType(DatabaseColumnType mariaDbType)
    {
        if (mariaDbType.Name.ToLower() == "uuid")
            return new DatabaseColumnType(DatabaseType.SQLite, "TEXT");

        // MariaDB is highly compatible with MySQL, so we can reuse the same parsing logic.
        return ParseMySQLType(mariaDbType);
    }

    protected virtual DatabaseColumnType? GetDbTypeFromCsType(ValueProperty property, DatabaseType databaseType)
    {
        var csTypeName = property.CsType.Name.ToLower();

        return csTypeName switch
        {
            "sbyte" or "byte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "bool" or "enum" => new DatabaseColumnType(databaseType, "INTEGER"),
            "decimal" or "float" or "double" => new DatabaseColumnType(databaseType, "REAL"),
            "string" or "char" or "datetime" or "dateonly" or "timeonly" or "guid" => new DatabaseColumnType(databaseType, "TEXT"),
            "byte[]" => new DatabaseColumnType(databaseType, "BLOB"),
            _ => null
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