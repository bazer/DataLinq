using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.MySql.Models;
using ThrowAway;

namespace DataLinq.MySql;

public class MetadataFromMySqlFactoryCreator : IMetadataFromDatabaseFactoryCreator
{
    public IMetadataFromSqlFactory GetMetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options)
    {
        return new MetadataFromMySqlFactory(options);
    }
}

public class MetadataFromMySqlFactory : IMetadataFromSqlFactory
{
    private readonly MetadataFromDatabaseFactoryOptions options;

    public MetadataFromMySqlFactory(MetadataFromDatabaseFactoryOptions options)
    {
        this.options = options;
    }

    public Option<DatabaseMetadata> ParseDatabase(string name, string csTypeName, string dbName, string connectionString)
    {
        var information_Schema = new MySqlDatabase<information_schema>(connectionString, "information_schema").Query();
        var database = new DatabaseMetadata(name, null, csTypeName, dbName);

        database.TableModels = information_Schema
            .TABLES.Where(x => x.TABLE_SCHEMA == dbName)
            .AsEnumerable()
            .Select(x => ParseTable(database, information_Schema, x))
            .Where(IsTableOrViewInOptionsList)
            .ToList();

        var missingTables = FindMissingTablesOrViewInOptionsList(database.TableModels).ToList();
        if (missingTables.Any())
            return $"Could not find the specified tables or views: {missingTables.ToJoinedString(", ")}";

        ParseIndices(database, information_Schema);
        ParseRelations(database, information_Schema);
        MetadataFactory.ParseIndices(database);
        MetadataFactory.ParseRelations(database);

        return database;
    }

    private IEnumerable<string> FindMissingTablesOrViewInOptionsList(List<TableModelMetadata> tableModels)
    {
        foreach (var tableName in options.Tables.Concat(options.Views))
        {
            if (!tableModels.Any(x => tableName.Equals(x.Table.DbName, StringComparison.OrdinalIgnoreCase)))
                yield return tableName;
        }
    }

    private bool IsTableOrViewInOptionsList(TableModelMetadata tableModel)
    {
        if (tableModel.Table.Type == TableType.View && options.Views.Any() && !options.Views.Any(x => x.Equals(tableModel.Table.DbName, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (tableModel.Table.Type == TableType.Table && options.Tables.Any() && !options.Tables.Any(x => x.Equals(tableModel.Table.DbName, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private void ParseIndices(DatabaseMetadata database, information_schema information_Schema)
    {
        // Fetch table-column pairs that are part of a foreign key relationship
        var foreignKeyColumns = information_Schema.KEY_COLUMN_USAGE
            .Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_TABLE_NAME != null)
            .Select(x => new { x.TABLE_NAME, x.COLUMN_NAME })
            .ToList();

        var indexGroups = information_Schema
            .STATISTICS.Where(x => x.TABLE_SCHEMA == database.DbName && x.INDEX_NAME != "PRIMARY")
            .ToList()
            .Where(x => !foreignKeyColumns.Any(fk => fk.TABLE_NAME == x.TABLE_NAME && fk.COLUMN_NAME == x.COLUMN_NAME))
            .GroupBy(x => x.INDEX_NAME);

        foreach (var dbIndexGroup in indexGroups)
        {
            var indexedColumns = dbIndexGroup.OrderBy(x => x.SEQ_IN_INDEX).ToList();
            var columnNames = indexedColumns.Select(x => x.COLUMN_NAME).ToArray();

            // Determine the type and characteristic of the index.
            var indexType = dbIndexGroup.First().INDEX_TYPE.ToUpper() switch
            {
                "BTREE" => IndexType.BTREE,
                "FULLTEXT" => IndexType.FULLTEXT,
                "HASH" => IndexType.HASH,
                "RTREE" => IndexType.RTREE,
                _ => throw new NotImplementedException($"Unknown index type '{dbIndexGroup.First().INDEX_TYPE.ToUpper()}'"),
            };

            var indexCharacteristic = dbIndexGroup.First().NON_UNIQUE == 0
                ? IndexCharacteristic.Unique
                : IndexCharacteristic.Simple;

            foreach (var indexColumn in indexedColumns)
            {
                var column = database
                    .TableModels.SingleOrDefault(x => x.Table.DbName == indexColumn.TABLE_NAME)?
                    .Table.Columns.SingleOrDefault(x => x.DbName == indexColumn.COLUMN_NAME);

                column?.ValueProperty.Attributes.Add(new IndexAttribute(dbIndexGroup.First().INDEX_NAME, indexCharacteristic, indexType, columnNames));
            }
        }
    }

    private void ParseRelations(DatabaseMetadata database, information_schema information_Schema)
    {
        foreach (var key in information_Schema
            .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_COLUMN_NAME != null))
        {
            var foreignKeyColumn = database
                .TableModels.SingleOrDefault(x => x.Table.DbName == key.TABLE_NAME)?
                .Table.Columns.SingleOrDefault(x => x.DbName == key.COLUMN_NAME);

            if (foreignKeyColumn == null)
                continue;

            foreignKeyColumn.ForeignKey = true;
            foreignKeyColumn.ValueProperty.Attributes.Add(new ForeignKeyAttribute(key.REFERENCED_TABLE_NAME, key.REFERENCED_COLUMN_NAME, key.CONSTRAINT_NAME));

            var referencedColumn = database
                .TableModels.SingleOrDefault(x => x.Table.DbName == key.REFERENCED_TABLE_NAME)?
                .Table.Columns.SingleOrDefault(x => x.DbName == key.REFERENCED_COLUMN_NAME);

            if (referencedColumn != null)
            {
                MetadataFactory.AddRelationProperty(referencedColumn, foreignKeyColumn, key.CONSTRAINT_NAME);
                MetadataFactory.AddRelationProperty(foreignKeyColumn, referencedColumn, key.CONSTRAINT_NAME);
            }
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
            .ToArray();

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
            Signed = dbColumns.COLUMN_TYPE.Contains("unsigned") ? false : null
        };

        if (dbType.Name == "decimal" || dbType.Name == "bit")
        {
            dbType.Length = dbColumns.NUMERIC_PRECISION;
            dbType.Decimals = (int?)dbColumns.NUMERIC_SCALE;
        }
        else if (dbType.Name != "enum")
        {
            dbType.Length = dbColumns.CHARACTER_MAXIMUM_LENGTH;
        }

        var column = new Column
        {
            Table = table,
            DbName = dbColumns.COLUMN_NAME,
            Nullable = dbColumns.IS_NULLABLE == "YES",
            //PrimaryKey = dbColumns.COLUMN_KEY == "PRI",
            AutoIncrement = dbColumns.EXTRA.Contains("auto_increment")
        };

        column.SetPrimaryKey(dbColumns.COLUMN_KEY == "PRI");

        column.AddDbType(dbType);

        var csType = ParseCsType(dbType.Name);
        var valueProp = MetadataFactory.AttachValueProperty(column, csType, options.CapitaliseNames);

        if (csType == "enum")
        {
            MetadataFactory.AttachEnumProperty(valueProp, new List<(string name, int value)>(), ParseEnumType(dbColumns.COLUMN_TYPE), true);
            valueProp.CsTypeName = valueProp.CsTypeName == "enum" ? valueProp.CsName + "Value" : valueProp.CsTypeName;
        }

        return column;
    }

    private IEnumerable<(string name, int value)> ParseEnumType(string COLUMN_TYPE) =>
        COLUMN_TYPE[5..^1]
        .Trim('\'')
        .Split("','")
        .Select((x, i) => (x, i + 1));

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
            "year" => "int",
            "date" => "DateOnly",
            "time" => "TimeOnly",
            "float" => "float",
            "bigint" => "long",
            "char" => "string",
            "binary" => "Guid",
            "enum" => "enum",
            "longtext" => "string",
            "decimal" => "decimal",
            "blob" => "byte[]",
            "tinyblob" => "byte[]",
            "mediumblob" => "byte[]",
            "longblob" => "byte[]",
            "smallint" => "int",
            _ => throw new NotImplementedException($"Unknown type '{dbType}'"),
        };
    }
}