using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
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

    public Option<DatabaseDefinition> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString)
    {
        var information_Schema = new MySqlDatabase<information_schema>(connectionString, "information_schema").Query();
        var database = new DatabaseDefinition(name, new CsTypeDeclaration(csTypeName, csNamespace, ModelCsType.Class), dbName);

        database.SetTableModels(information_Schema
            .TABLES.Where(x => x.TABLE_SCHEMA == dbName)
            .AsEnumerable()
            .Select(x => ParseTable(database, information_Schema, x))
            .Where(IsTableOrViewInOptionsList));

        var missingTables = FindMissingTablesOrViewInOptionsList(database.TableModels).ToList();
        if (missingTables.Any())
            return $"Could not find the specified tables or views: {missingTables.ToJoinedString(", ")}";

        ParseIndices(database, information_Schema);
        ParseRelations(database, information_Schema);
        MetadataFactory.ParseIndices(database);
        MetadataFactory.ParseRelations(database);

        return database;
    }

    private IEnumerable<string> FindMissingTablesOrViewInOptionsList(TableModel[] tableModels)
    {
        foreach (var tableName in options.Tables.Concat(options.Views))
        {
            if (!tableModels.Any(x => tableName.Equals(x.Table.DbName, StringComparison.OrdinalIgnoreCase)))
                yield return tableName;
        }
    }

    private bool IsTableOrViewInOptionsList(TableModel tableModel)
    {
        if (tableModel.Table.Type == TableType.View && options.Views.Any() && !options.Views.Any(x => x.Equals(tableModel.Table.DbName, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (tableModel.Table.Type == TableType.Table && options.Tables.Any() && !options.Tables.Any(x => x.Equals(tableModel.Table.DbName, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private void ParseIndices(DatabaseDefinition database, information_schema information_Schema)
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

                column?.ValueProperty.AddAttribute(new IndexAttribute(dbIndexGroup.First().INDEX_NAME, indexCharacteristic, indexType, columnNames));
            }
        }
    }

    private void ParseRelations(DatabaseDefinition database, information_schema information_Schema)
    {
        foreach (var key in information_Schema
            .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_COLUMN_NAME != null))
        {
            var foreignKeyColumn = database
                .TableModels.SingleOrDefault(x => x.Table.DbName == key.TABLE_NAME)?
                .Table.Columns.SingleOrDefault(x => x.DbName == key.COLUMN_NAME);

            if (foreignKeyColumn == null)
                continue;

            foreignKeyColumn.SetForeignKey();
            foreignKeyColumn.ValueProperty.AddAttribute(new ForeignKeyAttribute(key.REFERENCED_TABLE_NAME, key.REFERENCED_COLUMN_NAME, key.CONSTRAINT_NAME));

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

    

    private TableModel ParseTable(DatabaseDefinition database, information_schema information_Schema, TABLES dbTables)
    {
        var type = dbTables.TABLE_TYPE == "BASE TABLE" ? TableType.Table : TableType.View;

        if (dbTables.TABLE_NAME == null)
            throw new Exception("Table name is null");

        var table = type == TableType.Table
            ? new TableDefinition(dbTables.TABLE_NAME)
            : new ViewDefinition(dbTables.TABLE_NAME);

        if (table is ViewDefinition view)
        {
            view.SetDefinition(information_Schema
                .VIEWS.Where(x => x.TABLE_SCHEMA == database.DbName && x.TABLE_NAME == view.DbName)
                .AsEnumerable()
                .Select(x => x.VIEW_DEFINITION)
                .FirstOrDefault()?
                .Replace($"`{database.DbName}`.", "") ?? "");
        }

        table.SetColumns(information_Schema
            .COLUMNS.Where(x => x.TABLE_SCHEMA == database.DbName && x.TABLE_NAME == table.DbName)
            .AsEnumerable()
            .Select(x => ParseColumn(table, x)));

        var csName = options.CapitaliseNames
            ? table.DbName.FirstCharToUpper()
            : table.DbName;

        return new TableModel(table.Model.CsType.Name, database, table, csName);
    }

    private ColumnDefinition ParseColumn(TableDefinition table, COLUMNS dbColumns)
    {
        var dbType = new DatabaseColumnType(DatabaseType.MySQL, dbColumns.DATA_TYPE);

        if (dbColumns.COLUMN_TYPE.Contains("unsigned"))
            dbType.SetSigned(false);
       
        if (dbType.Name == "decimal" || dbType.Name == "bit")
        {
            dbType.SetLength(dbColumns.NUMERIC_PRECISION);
            dbType.SetDecimals(dbColumns.NUMERIC_SCALE);
        }
        else if (dbType.Name != "enum")
        {
            dbType.SetLength(dbColumns.CHARACTER_MAXIMUM_LENGTH);
        }

        var column = new ColumnDefinition(dbColumns.COLUMN_NAME, table);
        column.SetNullable(dbColumns.IS_NULLABLE == "YES");
        column.SetPrimaryKey(dbColumns.COLUMN_KEY == "PRI");
        column.SetAutoIncrement(dbColumns.EXTRA.Contains("auto_increment"));
        column.AddDbType(dbType);

        var csType = ParseCsType(dbType.Name);
        var valueProp = MetadataFactory.AttachValueProperty(column, csType, options.CapitaliseNames);

        if (csType == "enum")
        {
            MetadataFactory.AttachEnumProperty(valueProp, new List<(string name, int value)>(), ParseEnumType(dbColumns.COLUMN_TYPE), true);
            if (valueProp.CsType.Name == "enum")
                valueProp.SetCsType(valueProp.CsType.MutateName(valueProp.PropertyName + "Value"));
            //valueProp.CsTypeName = valueProp.CsTypeName == "enum" ? valueProp.PropertyName + "Value" : valueProp.CsTypeName;
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