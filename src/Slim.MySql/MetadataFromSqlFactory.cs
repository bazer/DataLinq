using System;
using System.Data.Common;
using System.Linq;
using Slim.Metadata;
using Slim.MySql.Models;

namespace Slim.MySql
{
    public static class MetadataFromSqlFactory
    {
        public static Database ParseDatabase(string dbName, information_schema information_Schema)
        {
            var database = new Database(dbName);

            database.Tables = information_Schema
                .TABLES.Where(x => x.TABLE_SCHEMA == dbName)
                .AsEnumerable()
                .Select(x => ParseTable(database, information_Schema, x))
                .ToList();

            foreach (var key in information_Schema
                .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == dbName && x.REFERENCED_COLUMN_NAME != null))
            {
                var relation = new Relation
                {
                    Constraint = key.CONSTRAINT_NAME,
                    Type = RelationType.OneToMany
                };

                var foreignKeyColumn = database
                    .Tables.Single(x => x.DbName == key.TABLE_NAME)
                    .Columns.Single(x => x.DbName == key.COLUMN_NAME);

                var candidateKeyColumn = database
                    .Tables.Single(x => x.DbName == key.REFERENCED_TABLE_NAME)
                    .Columns.Single(x => x.DbName == key.REFERENCED_COLUMN_NAME);

                relation.ForeignKey = CreateRelationPart(relation, foreignKeyColumn, RelationPartType.ForeignKey);
                relation.CandidateKey = CreateRelationPart(relation, candidateKeyColumn, RelationPartType.CandidateKey);
            }

            database.Relations = database
                .Tables.SelectMany(x => x.Columns.SelectMany(y => y.RelationParts.Select(z => z.Relation)))
                .Distinct()
                .ToList();

            return database;
        }

        private static RelationPart CreateRelationPart(Relation relation, Column column, RelationPartType type)
        {
            var relationPart = new RelationPart
            {
                Relation = relation,
                Column = column,
                Type = type
            };

            column.RelationParts.Add(relationPart);

            return relationPart;
        }


        private static Table ParseTable(Database database, information_schema information_Schema, TABLES dbTables)
        {
            var table = new Table();

            table.Database = database;
            table.DbName = dbTables.TABLE_NAME;
            table.Type = dbTables.TABLE_TYPE == "BASE TABLE" ? TableType.Table : TableType.View;
            table.Model = new Model
            {
                CsTypeName = dbTables.TABLE_NAME
            };

            table.Columns = information_Schema
                .COLUMNS.Where(x => x.TABLE_SCHEMA == database.Name && x.TABLE_NAME == table.DbName)
                .AsEnumerable()
                .Select(x => ParseColumn(table, x))
                .ToList();

            return table;
        }

        private static Column ParseColumn(Table table, COLUMNS dbColumns)
        {
            var column = new Column();

            column.Table = table;
            column.DbName = dbColumns.COLUMN_NAME;
            column.DbType = dbColumns.DATA_TYPE;
            column.Nullable = dbColumns.IS_NULLABLE == "YES";
            column.Length = dbColumns.CHARACTER_MAXIMUM_LENGTH;
            column.PrimaryKey = dbColumns.COLUMN_KEY == "PRI";

            var property = new Property();
            property.CsTypeName = ParseCsType(column.DbType);
            property.CsNullable = column.Nullable && IsCsTypeNullable(property.CsTypeName);
            column.ValueProperty = property;

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