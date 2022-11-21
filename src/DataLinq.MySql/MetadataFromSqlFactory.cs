using DataLinq.Attributes;
using DataLinq.Metadata;
using DataLinq.MySql.Models;
using System;
using System.Data;
using System.Linq;

namespace DataLinq.MySql
{
    public class MetadataFromSqlFactory
    {
        private readonly MetadataFromSqlFactoryOptions options;

        public MetadataFromSqlFactory(MetadataFromSqlFactoryOptions options)
        {
            this.options = options;
        }

        public DatabaseMetadata ParseDatabase(string name, string csTypeName, string dbName, information_schema information_Schema)
        {
            var database = new DatabaseMetadata(name, null, csTypeName, dbName);

            database.TableModels = information_Schema
                .TABLES.Where(x => x.TABLE_SCHEMA == dbName)
                .AsEnumerable()
                .Select(x => ParseTable(database, information_Schema, x))
                .ToList();

            ParseIndices(database, information_Schema);
            ParseRelations(database, information_Schema);
            MetadataFactory.ParseIndices(database);
            MetadataFactory.ParseRelations(database);

            return database;
        }

        private void ParseIndices(DatabaseMetadata database, information_schema information_Schema)
        {
            foreach (var dbIndex in information_Schema
                .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_COLUMN_NAME == null && x.CONSTRAINT_NAME != "PRIMARY").ToList().GroupBy(x => x.CONSTRAINT_NAME))
            {
                var columns = dbIndex.Select(key => database
                    .TableModels.Single(x => x.Table.DbName == key.TABLE_NAME)
                    .Table.Columns.Single(x => x.DbName == key.COLUMN_NAME));

                foreach (var column in columns)
                {
                    column.Unique = true;
                    column.ValueProperty.Attributes.Add(new UniqueAttribute(dbIndex.First().CONSTRAINT_NAME));
                }
            }
        }

        private void ParseRelations(DatabaseMetadata database, information_schema information_Schema)
        {
            foreach (var key in information_Schema
                .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_COLUMN_NAME != null))
            {
                var foreignKeyColumn = database
                    .TableModels.Single(x => x.Table.DbName == key.TABLE_NAME)
                    .Table.Columns.Single(x => x.DbName == key.COLUMN_NAME);

                foreignKeyColumn.ForeignKey = true;
                foreignKeyColumn.ValueProperty.Attributes.Add(new ForeignKeyAttribute(key.REFERENCED_TABLE_NAME, key.REFERENCED_COLUMN_NAME, key.CONSTRAINT_NAME));
            }
        }

        private TableModelMetadata ParseTable(DatabaseMetadata database, information_schema information_Schema, TABLES dbTables)
        {
            var type = dbTables.TABLE_TYPE == "BASE TABLE" ? TableType.Table : TableType.View;

            var table = type == TableType.Table
                ? new TableMetadata()
                : new ViewMetadata();

            table.Database = database;
            table.DbName = dbTables.TABLE_NAME;
            MetadataFactory.AttachModel(table, options.CapitaliseNames);

            if (table is ViewMetadata view)
            {
                view.Definition = information_Schema
                    .VIEWS.Where(x => x.TABLE_SCHEMA == database.DbName && x.TABLE_NAME == view.DbName)
                    .AsEnumerable()
                    .Select(x => x.VIEW_DEFINITION)
                    .FirstOrDefault()?
                    .Replace($"`{database.DbName}`.", "") ?? "";
            }

            table.Columns = information_Schema
                .COLUMNS.Where(x => x.TABLE_SCHEMA == database.DbName && x.TABLE_NAME == table.DbName)
                .AsEnumerable()
                .Select(x => ParseColumn(table, x))
                .ToList();

            return new TableModelMetadata
            {
                Table = table,
                Model = table.Model,
                CsPropertyName = table.Model.CsTypeName
            };
        }

        private Column ParseColumn(TableMetadata table, COLUMNS dbColumns)
        {
            var dbType = new DatabaseColumnType
            {
                DatabaseType = DatabaseType.MySQL,
                Name = dbColumns.DATA_TYPE,
                Length = dbColumns.CHARACTER_MAXIMUM_LENGTH,
                Signed = dbColumns.COLUMN_TYPE.Contains("unsigned") ? false : null
            };

            var column = new Column
            {
                Table = table,
                DbName = dbColumns.COLUMN_NAME,
                Nullable = dbColumns.IS_NULLABLE == "YES",
                PrimaryKey = dbColumns.COLUMN_KEY == "PRI",
                AutoIncrement = dbColumns.EXTRA.Contains("auto_increment")
            };

            column.DbTypes.Add(dbType);

            var csType = ParseCsType(dbType.Name);

            if (csType == "enum")
                MetadataFactory.AttachEnumProperty(column, true, options.CapitaliseNames, ParseEnumType(dbColumns.COLUMN_TYPE));
            else
                MetadataFactory.AttachValueProperty(column, csType, options.CapitaliseNames);

            return column;
        }

        private string[] ParseEnumType(string COLUMN_TYPE) =>
            COLUMN_TYPE[5..^1].Trim('\'').Split("','");

        private string ParseCsType(string dbType)
        {
            return dbType.ToLower() switch
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
                "timestamp" => "DateTime",
                "date" => "DateOnly",
                "float" => "float",
                "bigint" => "long",
                "char" => "string",
                "binary" => "Guid",
                "enum" => "enum",
                "longtext" => "string",
                "decimal" => "decimal",
                "blob" => "byte[]",
                "smallint" => "int",
                _ => throw new NotImplementedException($"Unknown type '{dbType}'"),
            };
        }
    }
}