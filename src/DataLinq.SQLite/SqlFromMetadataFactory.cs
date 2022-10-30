using DataLinq.Metadata;
using DataLinq.Query;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Linq;

namespace DataLinq.SQLite
{
    public class SqlFromMetadataFactory : ISqlFromMetadataFactory, IDatabaseCreator
    {
        public static void Register()
        {
            DatabaseFactory.SqlGenerators[DatabaseType.SQLite] = new SqlFromMetadataFactory();
            DatabaseFactory.DbCreators[DatabaseType.SQLite] = new SqlFromMetadataFactory();
        }

        public Sql GenerateSql(DatabaseMetadata metadata, bool foreignKeyRestrict)
        {
            var sql = new SqlGeneration(2, '"', "/* Generated %datetime% by DataLinq */\n\n");
            foreach(var table in sql.SortTablesByForeignKeys(metadata.TableModels.Select(x => x.Table).ToList()))
            {
                sql.CreateTable(table.DbName, x =>
                {
                    var longestName = table.Columns.Max(x => x.DbName.Length)+1;
                    foreach (var column in table.Columns.OrderBy(x => x.Index))
                    {
                        var dbType = column.DbTypes.Single(x => x.DatabaseType == DatabaseType.SQLite);

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

    }
}