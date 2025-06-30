using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using ThrowAway;

namespace DataLinq.SQLite;

public class MetadataFromSQLiteFactoryCreator : IMetadataFromDatabaseFactoryCreator
{
    public IMetadataFromSqlFactory GetMetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options)
    {
        return new MetadataFromSQLiteFactory(options);
    }
}

public class MetadataFromSQLiteFactory : IMetadataFromSqlFactory
{
    private readonly MetadataFromDatabaseFactoryOptions options;

    public MetadataFromSQLiteFactory(MetadataFromDatabaseFactoryOptions options)
    {
        this.options = options;
    }

    public Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString) => DLOptionFailure.CatchAll<DatabaseDefinition>(() =>
    {
        var dbAccess = new SQLiteDatabaseTransaction(connectionString, Mutation.TransactionType.ReadOnly);

        var database = new DatabaseDefinition(name, new CsTypeDeclaration(csTypeName, csNamespace, ModelCsType.Class), dbName);
        database.SetTableModels(dbAccess
            .ReadReader("SELECT *\r\nFROM sqlite_master m\r\nWHERE\r\nm.type <> 'index' AND\r\nm.tbl_name <> 'sqlite_sequence'")
            .Select(x => ParseTable(database, x, dbAccess))
            .Where(IsTableOrViewInOptionsList));

        var missingTables = FindMissingTablesOrViewInOptionsList(database.TableModels).ToList();
        if (missingTables.Count != 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Could not find the specified tables or views: {missingTables.ToJoinedString(", ")}");

        if (database.TableModels.Length == 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"No tables or views found in database '{dbName}'. Please check the connection string and database name.");

        ParseIndices(database, dbAccess);
        ParseRelations(database, dbAccess);
        MetadataFactory.ParseIndices(database);
        MetadataFactory.ParseRelations(database);
        MetadataFactory.ParseInterfaces(database);

        return database;
    });

    private IEnumerable<string> FindMissingTablesOrViewInOptionsList(TableModel[] tableModels)
    {
        foreach (var tableName in options.Include ?? [])
        {
            if (!tableModels.Any(x => tableName.Equals(x.Table.DbName, StringComparison.OrdinalIgnoreCase)))
                yield return tableName;
        }
    }
    private bool IsTableOrViewInOptionsList(TableModel tableModel)
    {
        if (options.Include == null || !options.Include.Any())
            return true;

        return options.Include.Any(x => x.Equals(tableModel.Table.DbName, StringComparison.OrdinalIgnoreCase));
    }

    private void ParseIndices(DatabaseDefinition database, SQLiteDatabaseTransaction dbAccess)
    {
        foreach (var tableModel in database.TableModels.Where(x => x.Table.Type == TableType.Table))
        {
            foreach (var reader in dbAccess.ReadReader($"SELECT l.`name`, l.`origin`, l.`unique`, i.`seqno`, i.`name` FROM pragma_index_list('{tableModel.Table.DbName}') l JOIN pragma_index_info(l.`name`) i"))
            {
                var column = tableModel
                    .Table.Columns.Single(x => x.DbName == reader.GetString(4));

                var name = reader.GetString(0);
                if (name.StartsWith("sqlite_autoindex"))
                    name = column.DbName;

                // Determine the type and characteristic of the index.
                var indexType = IndexType.BTREE;  // SQLite predominantly uses B-tree
                var indexCharacteristic = reader.GetInt32(2) == 1
                    ? IndexCharacteristic.Unique
                    : IndexCharacteristic.Simple;

                column.ValueProperty.AddAttribute(new IndexAttribute(name, indexCharacteristic, indexType));
            }
        }
    }

    private void ParseRelations(DatabaseDefinition database, SQLiteDatabaseTransaction dbAccess)
    {
        foreach (var tableModel in database.TableModels.Where(x => x.Table.Type == TableType.Table))
        {
            foreach (var reader in dbAccess.ReadReader($"SELECT `id`, `table`, `from`, `to` FROM pragma_foreign_key_list('{tableModel.Table.DbName}')"))
            {
                var keyName = reader.GetString(0);
                var tableName = reader.GetString(1);
                var fromColumn = reader.GetString(2);
                var toColumn = reader.GetString(3);

                var foreignKeyColumn = tableModel
                    .Table.Columns.Single(x => x.DbName == fromColumn);

                foreignKeyColumn.SetForeignKey();
                foreignKeyColumn.ValueProperty.AddAttribute(new ForeignKeyAttribute(tableName, toColumn, keyName));

                var referencedColumn = database
                   .TableModels.SingleOrDefault(x => x.Table.DbName == tableName)?
                   .Table.Columns.SingleOrDefault(x => x.DbName == toColumn);

                if (referencedColumn != null)
                {
                    MetadataFactory.AddRelationProperty(referencedColumn, foreignKeyColumn, keyName);
                    MetadataFactory.AddRelationProperty(foreignKeyColumn, referencedColumn, keyName);
                }
            }
        }
    }

    private TableModel ParseTable(DatabaseDefinition database, IDataLinqDataReader reader, SQLiteDatabaseTransaction dbAccess)
    {
        var type = reader.GetString(0) == "table" ? TableType.Table : TableType.View;
        var table = type == TableType.Table
             ? new TableDefinition(reader.GetString(2))
             : new ViewDefinition(reader.GetString(2));

        var csName = options.CapitaliseNames
            ? table.DbName.ToPascalCase()
            : table.DbName;

        var tableModel = new TableModel(csName, database, table, csName);

        if (table is ViewDefinition view)
            view.SetDefinition(ParseViewDefinition(reader.GetString(4)));

        table.SetColumns(dbAccess
            .ReadReader($"SELECT * FROM pragma_table_info(\"{table.DbName}\")")
            .Select(x => ParseColumn(table, x, dbAccess)));

        return tableModel;
    }

    private static string ParseViewDefinition(string definition)
    {
        definition = definition
            .ReplaceLineEndings(" ")
            .Replace("\"", @"\""");

        var selectIndex = definition.IndexOf("SELECT ", StringComparison.OrdinalIgnoreCase);

        if (selectIndex != -1)
            definition = definition.Substring(selectIndex);

        return definition;
    }

    private ColumnDefinition ParseColumn(TableDefinition table, IDataLinqDataReader reader, SQLiteDatabaseTransaction dbAccess)
    {
        var dbType = new DatabaseColumnType(DatabaseType.SQLite, reader.GetString(2).ToLower());

        if (string.IsNullOrEmpty(dbType.Name))
            dbType.SetName("text");

        var dbName = reader.GetString(1);

        var createStatement = dbAccess.ExecuteScalar<string>($"SELECT sql FROM sqlite_master WHERE type='table' AND name='{table.DbName}'");
        var hasAutoIncrement = false;

        if (createStatement != null)
        {
            var pattern = $@"\""({dbName})\""\s+INTEGER\s+PRIMARY\s+KEY\s+AUTOINCREMENT\b";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            hasAutoIncrement = regex.IsMatch(createStatement);
        }

        var column = new ColumnDefinition(dbName, table);
        column.SetNullable(reader.GetBoolean(3) == false);
        column.SetAutoIncrement(hasAutoIncrement);
        column.SetPrimaryKey(reader.GetBoolean(5));
        column.AddDbType(dbType);

        var csType = ParseCsType(dbType, dbName);

        MetadataFactory.AttachValueProperty(column, csType, options.CapitaliseNames);

        return column;
    }

    private string ParseCsType(DatabaseColumnType dbType, string columnName)
    {
        var lowerColumnName = columnName.ToLowerInvariant();

        // Name-based override for specific types
        if (dbType.Name == "text")
        {
            if (lowerColumnName.EndsWith("_date")) return "DateOnly";
            if (lowerColumnName.EndsWith("_time")) return "TimeOnly";
            if (lowerColumnName.EndsWith("_at") || lowerColumnName.Contains("datetime") || lowerColumnName.Contains("timestamp")) return "DateTime";
            if (lowerColumnName == "guid" || lowerColumnName.EndsWith("_guid") || lowerColumnName == "uuid" || lowerColumnName.EndsWith("_uuid")) return "Guid";
        }
        else if (dbType.Name == "integer")
        {
            if (lowerColumnName.StartsWith("is_") || lowerColumnName.StartsWith("has_")) return "bool";
        }
        else if (dbType.Name == "blob")
        {
            if (lowerColumnName == "guid" || lowerColumnName.EndsWith("_guid") || lowerColumnName == "uuid" || lowerColumnName.EndsWith("_uuid")) return "Guid";
        }

        // Default mapping based on affinity
        return dbType.Name.ToLower() switch
        {
            "integer" => "int",
            "real" => "double",
            "text" => "string",
            "blob" => "byte[]",
            _ => throw new NotImplementedException($"Unknown type '{dbType.Name}' for column '{columnName}'"),
        };
    }
}