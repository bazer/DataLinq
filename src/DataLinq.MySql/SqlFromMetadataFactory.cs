using DataLinq.Metadata;
using DataLinq.Query;
using System.Linq;

namespace DataLinq.MySql
{
    public static class SqlFromMetadataFactory
    {
        private static readonly string[] NoLengthTypes = new string[] { "text", "tinytext", "mediumtext", "longtext" };
        public static Sql GenerateSql(DatabaseMetadata metadata, bool foreignKeyRestrict)
        {
            var sql = new SqlGeneration(2, '`', "/* Generated %datetime% by DataLinq */\r\n\r\n");
            foreach(var table in metadata.Tables)
            {
                sql.CreateTable(table.DbName, x =>
                {
                    var longestName = table.Columns.Max(x => x.DbName.Length)+1;
                    foreach (var column in table.Columns.OrderBy(x => x.ColumnId))
                    {
                        sql.NewRow().Indent()
                            .ColumnName(column.DbName)
                            .Type(column.DbType.ToUpper(), column.DbName, longestName);

                        if (!NoLengthTypes.Contains(column.DbType.ToLower()))
                            sql.TypeLength(column.Length);
                        sql.Unsigned(column.Unsigned);
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