using DataLinq.Exceptions;
using DataLinq.Metadata;
using DataLinq.Query;
using MySqlConnector;
using System;
using System.Linq;
using ThrowAway;

namespace DataLinq.MySql
{
    public class SqlFromMetadataFactory : ISqlFromMetadataFactory
    {
        private static readonly string[] NoLengthTypes = new string[] { "text", "tinytext", "mediumtext", "longtext", "enum" };

        public Option<int, IDataLinqOptionFailure> CreateDatabase(Sql sql, string databaseName, string connectionString, bool foreignKeyRestrict)
        {
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            var command = connection.CreateCommand();

            command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{databaseName}`;\n" +
                $"USE `{databaseName}`;\n" +
                sql.Text;

            return command.ExecuteNonQuery();
        }

        public Option<Sql, IDataLinqOptionFailure> GetCreateTables(DatabaseMetadata metadata, bool foreignKeyRestrict)
        {
            var sql = new SqlGeneration(2, '`', "/* Generated %datetime% by DataLinq */\n\n");
            //sql.CreateDatabase(metadata.DbName);

            foreach(var table in sql.SortTablesByForeignKeys(metadata.TableModels.Where(x => x.Table.Type == TableType.Table).Select(x => x.Table).ToList()))
            {
                sql.CreateTable(table.DbName, x =>
                {
                    CreateColumns(foreignKeyRestrict, x, table);
                });
            }

            foreach (var view in sql.SortViewsByForeignKeys(metadata.TableModels.Where(x => x.Table.Type == TableType.View).Select(x => x.Table).Cast<ViewMetadata>().ToList()))
            {
                sql.CreateView(view.DbName, view.Definition);
            }

            return sql.sql;
        }

        private static void CreateColumns(bool foreignKeyRestrict, SqlGeneration sql, TableMetadata table)
        {
            var longestName = table.Columns.Max(x => x.DbName.Length) + 1;
            foreach (var column in table.Columns.OrderBy(x => x.Index))
            {
                var dbType = column.DbTypes.Single(x => x.DatabaseType == DatabaseType.MySQL);

                sql.NewRow().Indent()
                    .ColumnName(column.DbName)
                    .Type(dbType.Name.ToUpper(), column.DbName, longestName);

                if (dbType.Name == "enum" && column.ValueProperty.EnumProperty.HasValue)
                    sql.EnumValues(column.ValueProperty.EnumProperty.Value.EnumValues.Select(x => x.name));

                if (!NoLengthTypes.Contains(dbType.Name.ToLower()))
                    sql.TypeLength(dbType.Length);
                sql.Unsigned(dbType.Signed);
                sql.Nullable(column.Nullable)
                    .Autoincrement(column.AutoIncrement);

            }

            sql.PrimaryKey(table.PrimaryKeyColumns.Select(x => x.DbName).ToArray());

            foreach (var uniqueIndex in table.ColumnIndices.Where(x => x.Type == IndexType.Unique))
                sql.UniqueKey(uniqueIndex.ConstraintName, uniqueIndex.Columns.Select(x => x.DbName).ToArray());

            foreach (var foreignKey in table.Columns.Where(x => x.ForeignKey))
                foreach (var relation in foreignKey.RelationParts)
                    sql.ForeignKey(relation, foreignKeyRestrict);
        }

        
    }
}