using DataLinq.Exceptions;
using DataLinq.Extensions;
using DataLinq.Metadata;
using DataLinq.Query;
using Microsoft.Data.Sqlite;
using System;
using System.Data;
using System.IO;
using System.Linq;
using ThrowAway;

namespace DataLinq.SQLite
{
    public class SqlFromMetadataFactory : ISqlFromMetadataFactory
    {
        public Option<Sql, IDataLinqOptionFailure> GetCreateTables(DatabaseMetadata metadata, bool foreignKeyRestrict)
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
                            .Add(column.PrimaryKey && table.PrimaryKeyColumns.Count == 1 ? " PRIMARY KEY" : "")
                            .Add(column.AutoIncrement ? " AUTOINCREMENT" : "");

                        sql.Nullable(column.PrimaryKey ? false : column.Nullable);
                    }

                    if (table.PrimaryKeyColumns.Count > 1)
                        sql.PrimaryKey(table.PrimaryKeyColumns.Select(x => x.DbName).ToArray());

                    //{
                    //    sql.NewRow().Indent()
                    //        .Add($"PRIMARY KEY ({table.PrimaryKeyColumns.Select(x => x.DbName).ToJoinedString(", ")})");
                    //}

                    foreach (var uniqueIndex in table.ColumnIndices.Where(x => x.Type == IndexType.Unique))
                        sql.UniqueKey(uniqueIndex.ConstraintName, uniqueIndex.Columns.Select(x => x.DbName).ToArray());

                    foreach (var foreignKey in table.Columns.Where(x => x.ForeignKey))
                        foreach (var relation in foreignKey.RelationParts)
                            sql.ForeignKey(relation, foreignKeyRestrict);
                });
            }

            foreach (var view in sql.SortViewsByForeignKeys(metadata.TableModels.Select(x => x.Table).Where(x => x.Type == TableType.View).Cast<ViewMetadata>().ToList()))
            {
                sql.CreateView(view.DbName, view.Definition);
            }

            return sql.sql;
        }

        public Option<int, IDataLinqOptionFailure> CreateDatabase(Sql sql, string databaseFile, string connectionString, bool foreignKeyRestrict)
        {
            if (File.Exists(databaseFile))
                return DataLinqOptionFailure.Fail("DatabaseFile already exists");

            File.WriteAllBytes(databaseFile, new byte[] { });

            using var connection = new SqliteConnection($"Data Source={databaseFile}");
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = sql.Text;

            return command.ExecuteNonQuery();
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

    public class SQLiteGeneration : SqlGeneration
    {
        public SQLiteGeneration(int indentationSpaces = 4, char quoteChar = '`', string generatedText = "") : base (indentationSpaces, quoteChar, generatedText)
        {
        }

        public override SqlGeneration UniqueKey(string name, params string[] columns)
            => NewRow().Indent().Add($"CONSTRAINT {QuotedString(name)} UNIQUE {ParenthesisList(columns)}");
    }
}