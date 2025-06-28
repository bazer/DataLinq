using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.MySql.Shared;
using ThrowAway;

namespace DataLinq.MySql;

public abstract class MetadataFromSqlFactory : IMetadataFromSqlFactory
{
    private readonly MetadataFromDatabaseFactoryOptions options;
    private readonly DatabaseType databaseType;

    public static MetadataFromSqlFactory GetSqlFactory(MetadataFromDatabaseFactoryOptions options, DatabaseType databaseType)
    {
        if (databaseType == DatabaseType.MariaDB)
            return new MetadataFromMariaDBFactory(options);
        if (databaseType == DatabaseType.MySQL)
            return new MetadataFromMySqlFactory(options);

        throw new NotImplementedException($"No metadata factory for {databaseType}");
    }

    public MetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options, DatabaseType databaseType)
    {
        this.options = options;
        this.databaseType = databaseType;
    }

    public abstract Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString);

    //public virtual Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString) => DLOptionFailure.CatchAll<DatabaseDefinition>(() =>
    //{
    //    IInformationSchema information_Schema = databaseType == DatabaseType.MariaDB
    //        ? new MariaDBDatabase<MariaDBInformationSchema>(connectionString, "information_schema").Query()
    //        : new MySqlDatabase<MySQLInformationSchema>(connectionString, "information_schema").Query();


    //    var database = new DatabaseDefinition(name, new CsTypeDeclaration(csTypeName, csNamespace, ModelCsType.Class), dbName);

    //    database.SetTableModels(information_Schema
    //        .TABLES.Where(x => x.TABLE_SCHEMA == dbName)
    //        .AsEnumerable()
    //        .Select(x => ParseTable(database, information_Schema, x))
    //        .Where(IsTableOrViewInOptionsList));

    //    var missingTables = FindMissingTablesOrViewInOptionsList(database.TableModels).ToList();
    //    if (missingTables.Count != 0)
    //        return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Could not find the specified tables or views: {missingTables.ToJoinedString(", ")}");

    //    if (database.TableModels.Length == 0)
    //        return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"No tables or views found in database '{dbName}'. Please check the connection string and database name.");

    //    ParseIndices(database, information_Schema);
    //    ParseRelations(database, information_Schema);
    //    MetadataFactory.ParseIndices(database);
    //    MetadataFactory.ParseRelations(database);
    //    MetadataFactory.ParseInterfaces(database);

    //    return database;
    //});

    protected IEnumerable<string> FindMissingTablesOrViewInOptionsList(TableModel[] tableModels)
    {
        foreach (var tableName in options.Include ?? [])
        {
            if (!tableModels.Any(x => tableName.Equals(x.Table.DbName, StringComparison.OrdinalIgnoreCase)))
                yield return tableName;
        }
    }
    protected bool IsTableOrViewInOptionsList(TableModel tableModel)
    {
        // If the Include list is null or empty, always include the item.
        if (options.Include == null || !options.Include.Any())
            return true;

        // Otherwise, the table/view name must exist in the Include list.
        return options.Include.Any(x => x.Equals(tableModel.Table.DbName, StringComparison.OrdinalIgnoreCase));
    }

    //protected void ParseIndices(DatabaseDefinition database, IInformationSchema information_Schema_both)
    //{
    //    var information_Schema = databaseType == DatabaseType.MariaDB
    //        ? (IInformationSchema)information_Schema_both
    //        : (IInformationSchema)new MySQLInformationSchema(information_Schema_both);

    //    // Fetch table-column pairs that are part of a foreign key relationship
    //    var foreignKeyColumns = information_Schema.KEY_COLUMN_USAGE
    //        .Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_TABLE_NAME != null)
    //        .Select(x => new { x.TABLE_NAME, x.COLUMN_NAME })
    //        .ToList();

    //    var indexGroups = information_Schema
    //        .STATISTICS.Where(x => x.TABLE_SCHEMA == database.DbName && x.INDEX_NAME != "PRIMARY")
    //        .ToList()
    //        .Where(x => !foreignKeyColumns.Any(fk => fk.TABLE_NAME == x.TABLE_NAME && fk.COLUMN_NAME == x.COLUMN_NAME))
    //        .GroupBy(x => x.INDEX_NAME);

    //    foreach (var dbIndexGroup in indexGroups)
    //    {
    //        var indexedColumns = dbIndexGroup.OrderBy(x => x.SEQ_IN_INDEX).ToList();
    //        var columnNames = indexedColumns.Select(x => x.COLUMN_NAME).ToArray();

    //        // Determine the type and characteristic of the index.
    //        var indexType = dbIndexGroup.First().INDEX_TYPE.ToUpper() switch
    //        {
    //            "BTREE" => IndexType.BTREE,
    //            "FULLTEXT" => IndexType.FULLTEXT,
    //            "HASH" => IndexType.HASH,
    //            "RTREE" => IndexType.RTREE,
    //            _ => throw new NotImplementedException($"Unknown index type '{dbIndexGroup.First().INDEX_TYPE.ToUpper()}'"),
    //        };

    //        var indexCharacteristic = dbIndexGroup.First().NON_UNIQUE == 0
    //            ? IndexCharacteristic.Unique
    //            : IndexCharacteristic.Simple;

    //        foreach (var indexColumn in indexedColumns)
    //        {
    //            var column = database
    //                .TableModels.SingleOrDefault(x => x.Table.DbName == indexColumn.TABLE_NAME)?
    //                .Table.Columns.SingleOrDefault(x => x.DbName == indexColumn.COLUMN_NAME);

    //            column?.ValueProperty.AddAttribute(new IndexAttribute(dbIndexGroup.First().INDEX_NAME, indexCharacteristic, indexType, columnNames));
    //        }
    //    }
    //}

    //protected void ParseRelations(DatabaseDefinition database, IInformationSchema information_Schema)
    //{
    //    foreach (var key in information_Schema
    //        .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_COLUMN_NAME != null))
    //    {
    //        var foreignKeyColumn = database
    //            .TableModels.SingleOrDefault(x => x.Table.DbName == key.TABLE_NAME)?
    //            .Table.Columns.SingleOrDefault(x => x.DbName == key.COLUMN_NAME);

    //        if (foreignKeyColumn == null || key.REFERENCED_TABLE_NAME == null || key.REFERENCED_COLUMN_NAME == null)
    //            continue;

    //        foreignKeyColumn.SetForeignKey();
    //        foreignKeyColumn.ValueProperty.AddAttribute(new ForeignKeyAttribute(key.REFERENCED_TABLE_NAME, key.REFERENCED_COLUMN_NAME, key.CONSTRAINT_NAME));

    //        var referencedColumn = database
    //            .TableModels.SingleOrDefault(x => x.Table.DbName == key.REFERENCED_TABLE_NAME)?
    //            .Table.Columns.SingleOrDefault(x => x.DbName == key.REFERENCED_COLUMN_NAME);

    //        if (referencedColumn != null)
    //        {
    //            MetadataFactory.AddRelationProperty(referencedColumn, foreignKeyColumn, key.CONSTRAINT_NAME);
    //            MetadataFactory.AddRelationProperty(foreignKeyColumn, referencedColumn, key.CONSTRAINT_NAME);
    //        }
    //    }
    //}

    //protected TableModel ParseTable(DatabaseDefinition database, IInformationSchema information_Schema, ITABLES dbTables)
    //{
    //    var type = dbTables.TABLE_TYPE == "BASE TABLE" ? TableType.Table : TableType.View;

    //    if (dbTables.TABLE_NAME == null)
    //        throw new Exception("Table name is null");

    //    var table = type == TableType.Table
    //         ? new TableDefinition(dbTables.TABLE_NAME)
    //         : new ViewDefinition(dbTables.TABLE_NAME);

    //    var csName = options.CapitaliseNames
    //        ? table.DbName.ToPascalCase()
    //        : table.DbName;

    //    var tableModel = new TableModel(csName, database, table, csName);

    //    if (table is ViewDefinition view)
    //    {
    //        view.SetDefinition(information_Schema
    //            .VIEWS.Where(x => x.TABLE_SCHEMA == database.DbName && x.TABLE_NAME == view.DbName)
    //            .AsEnumerable()
    //            .Select(x => x.VIEW_DEFINITION)
    //            .FirstOrDefault()?
    //            .Replace($"`{database.DbName}`.", "") ?? "");
    //    }

    //    table.SetColumns(information_Schema
    //        .COLUMNS.Where(x => x.TABLE_SCHEMA == database.DbName && x.TABLE_NAME == table.DbName)
    //        .AsEnumerable()
    //        .Select(x => ParseColumn(table, x)));

    //    return tableModel;
    //}

    protected ColumnDefinition ParseColumn(TableDefinition table, ICOLUMNS dbColumns)
    {
        var dbType = new DatabaseColumnType(databaseType, dbColumns.DATA_TYPE);

        if (dbColumns.COLUMN_TYPE.Contains("unsigned"))
            dbType.SetSigned(false);

        if (dbType.Name == "decimal" || dbType.Name == "bit")
        {
            dbType.SetLength(dbColumns.NUMERIC_PRECISION);
            dbType.SetDecimals(dbColumns.NUMERIC_SCALE);
        }
        else if (dbType.Name == "int" || dbType.Name == "tinyint" || dbType.Name == "smallint" || dbType.Name == "mediumint" || dbType.Name == "bigint")
        {
            // Parse length from COLUMN_TYPE string
            var length = ParseLengthFromColumnType(dbColumns.COLUMN_TYPE);
            dbType.SetLength(length);
        }
        else if (dbType.Name != "enum")
        {
            dbType.SetLength(dbColumns.CHARACTER_MAXIMUM_LENGTH);
        }

        var column = new ColumnDefinition(dbColumns.COLUMN_NAME, table);


        column.SetNullable(dbColumns.IS_NULLABLE == "YES");
        column.SetPrimaryKey(dbColumns.COLUMN_KEY == COLUMN_KEYValue.PRI);
        column.SetAutoIncrement(dbColumns.EXTRA.Contains("auto_increment"));
        column.AddDbType(dbType);

        var csType = ParseCsType(dbType);
        var valueProp = MetadataFactory.AttachValueProperty(column, csType, options.CapitaliseNames);

        if (csType == "enum")
        {
            var (dbValues, csValues) = ParseEnumType(dbColumns.COLUMN_TYPE);
            valueProp.SetEnumProperty(new EnumProperty(dbValues, csValues, true));

            if (valueProp.CsType.Name == "enum")
                valueProp.SetCsType(valueProp.CsType.MutateName(valueProp.PropertyName + "Value"));
            //valueProp.CsTypeName = valueProp.CsTypeName == "enum" ? valueProp.PropertyName + "Value" : valueProp.CsTypeName;
        }

        var defaultAttr = ParseDefaultValue(dbColumns, valueProp);
        if (defaultAttr != null)
            valueProp.AddAttribute(defaultAttr);

        return column;
    }

    protected static DefaultAttribute? ParseDefaultValue(ICOLUMNS dbColumns, ValueProperty property)
    {
        if (dbColumns.COLUMN_DEFAULT == null || string.Equals(dbColumns.COLUMN_DEFAULT, "NULL", StringComparison.CurrentCultureIgnoreCase))
            return null;

        if (dbColumns.COLUMN_DEFAULT == "" && property.CsType.Type != typeof(string))
            return null;

        if (dbColumns.COLUMN_DEFAULT.StartsWith("CURRENT_TIMESTAMP", StringComparison.CurrentCultureIgnoreCase))
            return new DefaultCurrentTimestampAttribute();

        if (property.CsType.Type == typeof(bool) && dbColumns.COLUMN_DEFAULT.StartsWith("b'"))
            return new DefaultAttribute(dbColumns.COLUMN_DEFAULT == "b'1'");

        var value = property.CsType.Type != null ?
                Convert.ChangeType(dbColumns.COLUMN_DEFAULT, property.CsType.Type, CultureInfo.InvariantCulture)
                : dbColumns.COLUMN_DEFAULT;

        return new DefaultAttribute(value);

    }

    protected uint? ParseLengthFromColumnType(string columnType)
    {
        var startIndex = columnType.IndexOf('(') + 1;
        var endIndex = columnType.IndexOf(')');
        if (startIndex > 0 && endIndex > startIndex)
        {
            var lengthStr = columnType.Substring(startIndex, endIndex - startIndex);
            if (uint.TryParse(lengthStr, out var length))
            {
                return length;
            }
        }

        return null; // Default length if parsing fails
    }

    private (IEnumerable<(string, int)> dbValues, IEnumerable<(string, int)> csValues) ParseEnumType(string COLUMN_TYPE)
    {
        var regex = new Regex(@"'([^']*)'");
        var matches = regex.Matches(COLUMN_TYPE);

        var dbValues = new List<(string, int)>();
        var csValues = new List<(string, int)>();

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var dbName = match.Groups[1].Value;
            dbValues.Add((dbName, i + 1));

            // It's crucial to ensure the resulting name is a valid C# identifier.
            var csName = string.IsNullOrWhiteSpace(dbName)
                ? "Empty" // Or "None", "Default", etc. "Empty" is clear.
                : options.CapitaliseNames ? dbName.ToPascalCase() : dbName.Replace(" ", "_");

            csValues.Add((csName, i + 1));
        }
        return (dbValues, csValues);
    }

    protected virtual string ParseCsType(DatabaseColumnType dbType)
    {
        // Use a switch on the lowercased DB type name
        switch (dbType.Name.ToLower())
        {
            case "int":
                return (dbType.Signed == false) ? "uint" : "int";
            case "tinyint":
                return (dbType.Signed == false) ? "byte" : "sbyte"; // Use byte for unsigned, sbyte for signed
            case "smallint":
                return (dbType.Signed == false) ? "ushort" : "short";
            case "mediumint":
                return (dbType.Signed == false) ? "uint" : "int"; // No direct ushort equivalent, maps to uint/int
            case "bigint":
                return (dbType.Signed == false) ? "ulong" : "long";

            case "bit":
                return "bool";
            case "double":
                return "double";
            case "float":
                return "float";
            case "decimal":
                return "decimal";

            case "varchar":
            case "tinytext":
            case "text":
            case "mediumtext":
            case "longtext":
            case "char":
                return "string";

            case "datetime":
            case "timestamp":
                return "DateTime";
            case "date":
                return "DateOnly";
            case "time":
                return "TimeOnly";

            case "year":
                return "int";

            case "binary":
                return "Guid";

            case "enum":
                return "enum";

            case "varbinary":
            case "blob":
            case "tinyblob":
            case "mediumblob":
            case "longblob":
                return "byte[]";

            default:
                throw new NotImplementedException($"Unknown type '{dbType.Name}'");
        }
        ;
    }
}