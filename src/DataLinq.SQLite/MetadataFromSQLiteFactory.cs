using System;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using DataLinq.Attributes;
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
    private SQLiteDatabaseTransaction dbAccess;

    public MetadataFromSQLiteFactory(MetadataFromDatabaseFactoryOptions options)
    {
        this.options = options;
    }

    public Option<DatabaseMetadata> ParseDatabase(string name, string csTypeName, string dbName, string connectionString)
    {
        dbAccess = new SQLiteDatabaseTransaction(connectionString, Mutation.TransactionType.ReadOnly);

        var database = new DatabaseMetadata(name, null, csTypeName, dbName);
        database.TableModels = dbAccess
            .ReadReader("SELECT *\r\nFROM sqlite_master m\r\nWHERE\r\nm.type <> 'index' AND\r\nm.tbl_name <> 'sqlite_sequence'")
            .Select(x => ParseTable(database, x))
            .ToList();

        ParseIndices(database);
        ParseRelations(database);
        MetadataFactory.ParseIndices(database);
        MetadataFactory.ParseRelations(database);

        return database;
    }

    private void ParseIndices(DatabaseMetadata database)
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

                column.ValueProperty.Attributes.Add(new IndexAttribute(name, indexCharacteristic, indexType));
            }
        }
    }


    //private void ParseIndices(DatabaseMetadata database)
    //{
    //    foreach (var tableModel in database.TableModels.Where(x => x.Table.Type == TableType.Table))
    //    {
    //        foreach (var reader in dbAccess.ReadReader($"SELECT l.`name`, l.`origin`, l.`partial`, i.`seqno`, i.`name` FROM pragma_index_list('{tableModel.Table.DbName}') l\r\nJOIN pragma_index_info(l.`name`) i WHERE\r\nl.`unique` = 1 AND\r\nl.`origin` <> 'pk'"))
    //        {
    //            var column = tableModel
    //                .Table.Columns.Single(x => x.DbName == reader.GetString(4));

    //            var name = reader.GetString(0);

    //            if (name.StartsWith("sqlite_autoindex"))
    //                name = column.DbName;

    //            column.Unique = true;
    //            column.ValueProperty.Attributes.Add(new IndexAttribute(name));
    //        }
    //    }
    //}

    private void ParseRelations(DatabaseMetadata database)
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

                foreignKeyColumn.ForeignKey = true;
                foreignKeyColumn.ValueProperty.Attributes.Add(new ForeignKeyAttribute(tableName, toColumn, keyName));

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

    private TableModelMetadata ParseTable(DatabaseMetadata database, IDataLinqDataReader reader)
    {
        var type = reader.GetString(0) == "table" ? TableType.Table : TableType.View; //sqlite_master.type
        var table = type == TableType.Table
             ? new TableMetadata()
             : new ViewMetadata();

        table.Database = database;
        table.DbName = reader.GetString(2); //sqlite_master.tbl_name
        MetadataFactory.AttachModel(table, options.CapitaliseNames);

        if (table is ViewMetadata view)
        {
            view.Definition = ParseViewDefinition(reader.GetString(4)); //sqlite_master.sql
        }

        table.Columns = dbAccess
            .ReadReader($"SELECT * FROM pragma_table_info(\"{table.DbName}\")")
            .Select(x => ParseColumn(table, x))
            .ToArray();

        return new TableModelMetadata
        {
            Table = table,
            Model = table.Model,
            CsPropertyName = table.Model.CsTypeName
        };
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

    private Column ParseColumn(TableMetadata table, IDataLinqDataReader reader)
    {
        var dbType = new DatabaseColumnType
        {
            DatabaseType = DatabaseType.SQLite,
            Name = reader.GetString(2).ToLower(),
            //Length = 2147483647,
            Signed = null
        };

        // For whatever reason, sometimes the data type for columns in views return ""
        if (string.IsNullOrEmpty(dbType.Name))
            dbType.Name = "text";

        var dbName = reader.GetString(1);

        var createStatement = dbAccess.ExecuteScalar<string>($"SELECT sql FROM sqlite_master WHERE type='table' AND name='{table.DbName}'");
        var hasAutoIncrement = false;

        if (createStatement != null)
        {
            // Check if the specified column is defined as AUTOINCREMENT
            var pattern = $@"\""({dbName})\""\s+INTEGER\s+PRIMARY\s+KEY\s+AUTOINCREMENT\b";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            hasAutoIncrement = regex.IsMatch(createStatement);
        }

        var column = new Column
        {
            Table = table,
            DbName = dbName,
            Nullable = reader.GetBoolean(3) == false, // For views, this seems to indicate all columns as Nullable
            AutoIncrement = hasAutoIncrement
        };

        column.SetPrimaryKey(reader.GetBoolean(5));

        column.AddDbType(dbType);

        var csType = ParseCsType(dbType.Name);

        MetadataFactory.AttachValueProperty(column, csType, options.CapitaliseNames);

        return column;
    }

    // https://www.sqlite.org/datatype3.html
    private string ParseCsType(string dbType)
    {
        return dbType.ToLower() switch
        {
            "integer" => "int",
            "real" => "double",
            "text" => "string",
            "blob" => "byte[]",
            _ => throw new NotImplementedException($"Unknown type '{dbType}'"),
        };
    }
}