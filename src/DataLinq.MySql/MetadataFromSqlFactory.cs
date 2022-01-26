using System;
using System.Data.Common;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.MySql.Models;

namespace DataLinq.MySql
{
    public static class MetadataFromSqlFactory
    {
        public static DatabaseMetadata ParseDatabase(string dbName, information_schema information_Schema)
        {
            var database = new DatabaseMetadata(dbName);

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


        private static TableMetadata ParseTable(DatabaseMetadata database, information_schema information_Schema, TABLES dbTables)
        {
            var table = new TableMetadata();

            table.Database = database;
            table.DbName = dbTables.TABLE_NAME;
            table.Type = dbTables.TABLE_TYPE == "BASE TABLE" ? TableType.Table : TableType.View;
            table.Model = new Model
            {
                CsTypeName = dbTables.TABLE_NAME,
                Table = table
            };

            table.Columns = information_Schema
                .COLUMNS.Where(x => x.TABLE_SCHEMA == database.Name && x.TABLE_NAME == table.DbName)
                .AsEnumerable()
                .Select(x => ParseColumn(table, x))
                .ToList();

            return table;
        }

        private static Column ParseColumn(TableMetadata table, COLUMNS dbColumns)
        {
            var column = new Column();

            column.Table = table;
            column.DbName = dbColumns.COLUMN_NAME;
            column.DbType = dbColumns.DATA_TYPE;
            column.Nullable = dbColumns.IS_NULLABLE == "YES";
            column.Length = dbColumns.CHARACTER_MAXIMUM_LENGTH;
            column.PrimaryKey = dbColumns.COLUMN_KEY == "PRI";
            column.AutoIncrement = dbColumns.EXTRA.Contains("auto_increment");

            var property = new Property();
            property.CsTypeName = ParseCsType(column.DbType);
            property.CsNullable = column.Nullable && IsCsTypeNullable(property.CsTypeName);
            column.ValueProperty = property;

            return column;
        }

        private static string ParseCsType(string dbType)
        {
            return dbType switch
            {
                "int" => "int",
                "tinyint" => "int",
                "mediumint" => "int",
                "varchar" => "string",
                "text" => "string",
                "mediumtext" => "string",
                "bit" => "bool",
                "double" => "double",
                "datetime" => "DateTime",
                "date" => "DateTime",
                "float" => "float",
                "bigint" => "long",
                "char" => "string",
                "binary" => "Guid",
                "enum" => "int",
                "longtext" => "string",
                "decimal" => "decimal",
                "blob" => "byte[]",
                "smallint" => "int",
                _ => throw new NotImplementedException($"Unknown type '{dbType}'"),
            };
        }

        private static bool IsCsTypeNullable(string csType)
        {
            return csType switch
            {
                "int" => true,
                "string" => false,
                "bool" => true,
                "double" => true,
                "DateTime" => true,
                "float" => true,
                "long" => true,
                "Guid" => true,
                "byte[]" => false,
                "decimal" => true,
                _ => throw new NotImplementedException($"Unknown type '{csType}'"),
            };
        }
    }
}