using System;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.MariaDB.information_schema;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Shared;
using ThrowAway;

namespace DataLinq.MariaDB;

public class MetadataFromMariaDBFactoryCreator : IMetadataFromDatabaseFactoryCreator
{
    public IMetadataFromSqlFactory GetMetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options)
    {
        return new MetadataFromMariaDBFactory(options);
    }
}

public class MetadataFromMariaDBFactory(MetadataFromDatabaseFactoryOptions options)
    : MetadataFromSqlFactory(options, DatabaseType.MariaDB)
{
    public override Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString) => DLOptionFailure.CatchAll<DatabaseDefinition>(() =>
    {
        var information_Schema = new MariaDBDatabase<MariaDBInformationSchema>(connectionString, "information_schema").Query();


        var database = new DatabaseDefinition(name, new CsTypeDeclaration(csTypeName, csNamespace, ModelCsType.Class), dbName);

        database.SetTableModels(information_Schema
            .TABLES.Where(x => x.TABLE_SCHEMA == dbName)
            .AsEnumerable()
            .Select(x => ParseTable(database, information_Schema, x))
            .Where(IsTableOrViewInOptionsList));

        var missingTables = FindMissingTablesOrViewInOptionsList(database.TableModels).ToList();
        if (missingTables.Count != 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Could not find the specified tables or views: {missingTables.ToJoinedString(", ")}");

        if (database.TableModels.Length == 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"No tables or views found in database '{dbName}'. Please check the connection string and database name.");

        ParseIndices(database, information_Schema);
        ParseRelations(database, information_Schema);
        MetadataFactory.ParseIndices(database);
        MetadataFactory.ParseRelations(database);
        MetadataFactory.ParseInterfaces(database);

        return database;
    });

    protected void ParseIndices(DatabaseDefinition database, MariaDBInformationSchema information_Schema)
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

    protected void ParseRelations(DatabaseDefinition database, MariaDBInformationSchema information_Schema)
    {
        foreach (var key in information_Schema
            .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_COLUMN_NAME != null))
        {
            var foreignKeyColumn = database
                .TableModels.SingleOrDefault(x => x.Table.DbName == key.TABLE_NAME)?
                .Table.Columns.SingleOrDefault(x => x.DbName == key.COLUMN_NAME);

            if (foreignKeyColumn == null || key.REFERENCED_TABLE_NAME == null || key.REFERENCED_COLUMN_NAME == null)
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

    protected TableModel ParseTable(DatabaseDefinition database, MariaDBInformationSchema information_Schema, TABLES dbTables)
    {
        var type = dbTables.TABLE_TYPE == TABLE_TYPE.BASE_TABLE ? TableType.Table : TableType.View;

        if (dbTables.TABLE_NAME == null)
            throw new Exception("Table name is null");

        var table = type == TableType.Table
             ? new TableDefinition(dbTables.TABLE_NAME)
             : new ViewDefinition(dbTables.TABLE_NAME);

        var csName = options.CapitaliseNames
            ? table.DbName.ToPascalCase()
            : table.DbName;

        var tableModel = new TableModel(csName, database, table, csName);

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
            .Select(x => ParseColumn(table, (ICOLUMNS)x)));

        return tableModel;
    }

    protected override string ParseCsType(DatabaseColumnType dbType)
    {
        if (dbType.Name.ToLower().StartsWith("uuid"))
            return "Guid";

        return base.ParseCsType(dbType);
    }
}