using DataLinq.Metadata;
using DataLinq.Query;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace DataLinq.SQLite
{
    public class SqlFromMetadataFactory : ISqlFromMetadataFactory, IDatabaseCreator
    {
        public Sql GetCreateTables(DatabaseMetadata metadata, bool foreignKeyRestrict)
        {
            var sql = new SqlGeneration(2, '"', "/* Generated %datetime% by DataLinq */\n\n");
            foreach(var table in sql.SortTablesByForeignKeys(metadata.TableModels.Select(x => x.Table).ToList()))
            {
                sql.CreateTable(table.DbName, x =>
                {
                    var longestName = table.Columns.Max(x => x.DbName.Length)+1;
                    foreach (var column in table.Columns.OrderBy(x => x.Index))
                    {
                        var dbType = GetDbType(column);

                        sql.NewRow().Indent()
                            .ColumnName(column.DbName)
                            .Type(dbType.Name.ToUpper(), column.DbName, longestName)
                            .Add((column.PrimaryKey ? " PRIMARY KEY" : "") + (column.AutoIncrement ? " AUTOINCREMENT" : ""));
                        if(!column.PrimaryKey)
                            sql.Nullable(column.Nullable);
                    }
                    // TODO: Index
                    foreach (var foreignKey in table.Columns.Where(x => x.ForeignKey))
                        foreach (var relation in foreignKey.RelationParts)
                            sql.ForeignKey(relation, foreignKeyRestrict);
                });
            }
            return sql.sql;
        }

        public bool CreateDatabase(Sql sql, string databaseFile, string connectionString, bool foreignKeyRestrict)
        {
            if (File.Exists(databaseFile))
                return false;

            File.WriteAllBytes(databaseFile, new byte[] { });

            using var connection = new SqliteConnection($"Data Source={databaseFile}");
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = sql.Text;
            var result = command.ExecuteNonQuery();
            return true;
        }

        private DatabaseColumnType GetDbType(Column column)
        {
            if (column.DbTypes.Any(x => x.DatabaseType == DatabaseType.SQLite))
                return column.DbTypes.First(x => x.DatabaseType == DatabaseType.SQLite);

            if (column.DbTypes.Any(x => x.DatabaseType == DatabaseType.MySQL))
                return GetDbTypeFromMySQL(column.DbTypes.First(x => x.DatabaseType == DatabaseType.MySQL));

            return GetDbTypeFromCsType(column.ValueProperty);

        }

        private DatabaseColumnType GetDbTypeFromMySQL(DatabaseColumnType mysqlDbType)
        {
            return new DatabaseColumnType
            {
                DatabaseType = DatabaseType.SQLite,
                Name = ParseMySqlType(mysqlDbType.Name),
                Length = mysqlDbType.Length,
                Signed = mysqlDbType.Signed
            };
        }

        private string ParseMySqlType(string mysqlType)
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
                _ => throw new NotImplementedException($"Unknown type '{mysqlType}'"),
            };
        }

        private DatabaseColumnType GetDbTypeFromCsType(ValueProperty property)
        {
            return new DatabaseColumnType
            {
                DatabaseType = DatabaseType.SQLite,
                Name = ParseCsType(property.CsTypeName)
            };
        }

        private static string ParseCsType(string csType)
        {
            return csType.ToLower() switch
            {
                "int" => "integer",
                "double" => "real",
                "string" => "text",
                "byte[]" => "blob",
                _ => throw new NotImplementedException($"Unknown type '{csType}'"),
            };
        }
    }
}