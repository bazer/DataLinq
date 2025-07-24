using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.MariaDB;
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
        column.SetPrimaryKey(dbColumns.COLUMN_KEY == COLUMN_KEY.PRI);
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

        if (dbColumns.COLUMN_DEFAULT.StartsWith("UUID()", StringComparison.CurrentCultureIgnoreCase))
            return new DefaultNewUUIDAttribute();

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
        var startIndex = COLUMN_TYPE.IndexOf('(') + 1;
        var endIndex = COLUMN_TYPE.LastIndexOf(')');
        if (startIndex == 0 || endIndex == -1 || endIndex < startIndex)
            return ([], []);

        var enumContent = COLUMN_TYPE.Substring(startIndex, endIndex - startIndex);

        var regex = new Regex(@"'([^']*)'");
        var matches = regex.Matches(enumContent);

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
        var dbTypeName = dbType.Name.ToLower();

        if (dbTypeName.StartsWith("enum"))
            return "enum";

        // Use a switch on the lowercased DB type name
        switch (dbTypeName)
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