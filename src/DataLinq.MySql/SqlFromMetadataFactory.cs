using DataLinq.Metadata;
using DataLinq.Query;
using MySqlConnector;
using System.Linq;

namespace DataLinq.MySql
{
    public class SqlFromMetadataFactory : ISqlFromMetadataFactory, IDatabaseCreator
    {
        public static void Register()
        {
            DatabaseFactory.SqlGenerators[DatabaseFactory.DatabaseType.MySQL] = new SqlFromMetadataFactory();
            DatabaseFactory.DbCreators[DatabaseFactory.DatabaseType.MySQL] = new SqlFromMetadataFactory();
        }

        private static readonly string[] NoLengthTypes = new string[] { "text", "tinytext", "mediumtext", "longtext" };

        public bool CreateDatabase(Sql sql, string database, string connectionString, bool foreignKeyRestrict)
        {
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE IF NOT EXISTS {database};\r\n"+
                $"USE {database};\r\n"+
                sql.Text;
            var result = command.ExecuteNonQuery();
            return true;
        }

        public Sql GenerateSql(DatabaseMetadata metadata, bool foreignKeyRestrict)
        {
            var sql = new SqlGeneration(2, '`', "/* Generated %datetime% by DataLinq */\r\n\r\n");

            foreach(var table in sql.SortTablesByForeignKeys(metadata.Tables))
            {
                sql.CreateTable(table.DbName, x =>
                {
                    var longestName = table.Columns.Max(x => x.DbName.Length)+1;
                    foreach (var column in table.Columns.OrderBy(x => x.Index))
                    {
                        sql.NewRow().Indent()
                            .ColumnName(column.DbName)
                            .Type(column.DbType.ToUpper(), column.DbName, longestName);

                        if (!NoLengthTypes.Contains(column.DbType.ToLower()))
                            sql.TypeLength(column.Length);
                        sql.Unsigned(column.Signed);
                        sql.Nullable(column.Nullable)
                            .Autoincrement(column.AutoIncrement);
                        
                    }
                    foreach (var primaryKey in table.PrimaryKeyColumns)
                        sql.PrimaryKey(primaryKey.DbName);
                    foreach (var foreignKey in table.Columns.Where(x => x.ForeignKey))
                        foreach (var relation in foreignKey.RelationParts)
                            sql.ForeignKey(relation, foreignKeyRestrict);
                });
            }
            return sql.sql;
        }

    }
}