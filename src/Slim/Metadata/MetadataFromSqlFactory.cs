using System;
using System.Data.Common;
using System.Linq;

namespace Slim.Metadata
{
    public static class MetadataFromSqlFactory
    {
        public static Database ParseDatabase(string dbName, DatabaseProvider databaseProvider)
        {
            //string sql = $@"
            //    select * from information_schema.`TABLES` t
            //    inner join information_schema.`COLUMNS` c on t.TABLE_NAME = c.TABLE_NAME
            //    where
            //    t.TABLE_SCHEMA = '{name}' AND
            //    c.TABLE_SCHEMA = '{name}';";

            string tableSql = $@"
                select TABLE_NAME, TABLE_TYPE from information_schema.`TABLES` t
                where
                t.TABLE_SCHEMA = '{dbName}';";

            var database = new Database(dbName);

            database.Tables = databaseProvider
                .ReadReader(tableSql)
                .Select(x => ParseTable(database, databaseProvider, x))
                .ToList();

            string keysSql = $@"
                select CONSTRAINT_NAME, TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME from information_schema.`KEY_COLUMN_USAGE` k
                where
                k.TABLE_SCHEMA = '{dbName}' AND
                REFERENCED_COLUMN_NAME IS NOT NULL;";

            foreach (var reader in databaseProvider.ReadReader(keysSql))
            {
                var constraint = new Constraint();
                constraint.Name = reader.GetString(0);

                constraint.Column = database.Tables
                    .Single(x => x.Name == reader.GetString(1))
                    .Columns.Single(x => x.Name == reader.GetString(2));

                constraint.ReferencedColumn = database.Tables
                    .Single(x => x.Name == reader.GetString(3))
                    .Columns.Single(x => x.Name == reader.GetString(4));

                constraint.Column.Constraints.Add(constraint);
                constraint.ReferencedColumn.Constraints.Add(constraint);
            }

            return database;
        }

        private static Table ParseTable(Database database, DatabaseProvider databaseProvider, DbDataReader reader)
        {
            var table = new Table();

            table.Database = database;
            table.Name = reader.GetString(0);
            table.Type = reader.GetString(1) == "BASE TABLE" ? TableType.Table : TableType.View;

            string columnSql = $@"
                select COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH, COLUMN_KEY from information_schema.`COLUMNS` C
                where
                c.TABLE_SCHEMA = '{database.Name}' AND
                c.TABLE_NAME = '{table.Name}';";

            table.Columns = databaseProvider
                .ReadReader(columnSql)
                .Select(x => ParseColumn(table, x))
                .ToList();

            return table;
        }

        private static Column ParseColumn(Table table, DbDataReader reader)
        {
            var column = new Column();

            column.Table = table;
            column.Name = reader.GetString(0);
            column.DbType = reader.GetString(1);
            column.Nullable = reader.GetString(2) == "YES";

            if (reader.GetValue(3) != DBNull.Value)
                column.Length = reader.GetInt64(3);

            var key = reader.GetString(4);

            if (key == "PRI")
                column.PrimaryKey = true;
            //else if (key == "MUL")
            //    column.ForeignKey = true;

            //if (reader[4] != DBNull.Value)
            //    column.Default = reader.GetString(4);

            column.CsTypeName = ParseCsType(column.DbType);
            column.CsNullable = column.Nullable && IsCsTypeNullable(column.CsTypeName);

            return column;
        }

        private static string ParseCsType(string dbType)
        {
            switch (dbType)
            {
                case "int":
                    return "int";

                case "tinyint":
                    return "int";

                case "varchar":
                    return "string";

                case "text":
                    return "string";

                case "mediumtext":
                    return "string";

                case "bit":
                    return "bool";

                case "double":
                    return "double";

                case "datetime":
                    return "DateTime";

                case "date":
                    return "DateTime";

                case "float":
                    return "float";

                case "bigint":
                    return "long";

                case "char":
                    return "string";

                case "binary":
                    return "Guid";

                case "enum":
                    return "int";

                case "longtext":
                    return "string";

                case "decimal":
                    return "decimal";

                case "blob":
                    return "byte[]";

                case "smallint":
                    return "int";

                default:
                    throw new NotImplementedException($"Unknown type '{dbType}'");
            }
        }

        private static bool IsCsTypeNullable(string csType)
        {
            switch (csType)
            {
                case "int":
                    return true;

                case "string":
                    return false;

                case "bool":
                    return true;

                case "double":
                    return true;

                case "DateTime":
                    return true;

                case "float":
                    return true;

                case "long":
                    return true;

                case "Guid":
                    return true;

                case "byte[]":
                    return false;

                case "decimal":
                    return true;

                default:
                    throw new NotImplementedException($"Unknown type '{csType}'");
            }
        }
    }
}