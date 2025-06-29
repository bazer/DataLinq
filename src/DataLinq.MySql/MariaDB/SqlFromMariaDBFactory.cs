using System;
using System.Linq;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.MySql;

namespace DataLinq.MariaDB;

public class SqlFromMariaDBFactory : SqlFromMetadataFactory
{
    public override DatabaseColumnType GetDbType(ColumnDefinition column)
    {
        if (column.DbTypes.Any(x => x.DatabaseType == DatabaseType.MariaDB))
            return column.DbTypes.First(x => x.DatabaseType == DatabaseType.MariaDB);

        var type = column.DbTypes
            .Select(x => TryGetColumnType(x))
            .Concat(GetDbTypeFromCsType(column.ValueProperty).Yield())
            .Where(x => x != null)
            .FirstOrDefault();

        if (type == null)
            throw new Exception($"Could not find a MySQL database type for '{column.Table.Model.CsType.Name}.{column.ValueProperty.PropertyName}'");

        return type;
    }

    protected virtual DatabaseColumnType? TryGetColumnType(DatabaseColumnType dbType)
    {
        DatabaseColumnType? type = null;

        if (dbType.DatabaseType == DatabaseType.MySQL)
            type = ParseMySQLType(dbType);
        else if (dbType.DatabaseType == DatabaseType.Default)
            type = ParseDefaultType(dbType, DatabaseType.MariaDB);
        else if (dbType.DatabaseType == DatabaseType.SQLite)
            type = ParseSQLiteType(dbType, DatabaseType.MariaDB);

        return type;
    }

    protected virtual DatabaseColumnType? ParseMySQLType(DatabaseColumnType mysqlType)
    {
        // Handle MariaDB-specific types when generating for MySQL
        return mysqlType.Name.ToLower() switch
        {
            _ => mysqlType.Mutate(DatabaseType.MariaDB) // Assume other types are compatible
        };
    }

    protected DatabaseColumnType? GetDbTypeFromCsType(ValueProperty property)
    {
        if (property.CsType.Name.Equals("guid", StringComparison.CurrentCultureIgnoreCase))
            return new DatabaseColumnType(DatabaseType.MariaDB, "uuid");

        return base.GetDbTypeFromCsType(property, DatabaseType.MariaDB);
    }

    protected virtual DatabaseColumnType? ParseDefaultType(DatabaseColumnType defaultType)
    {
        if (defaultType.Name.Equals("uuid", StringComparison.CurrentCultureIgnoreCase))
            return new DatabaseColumnType(DatabaseType.MariaDB, "uuid");

        return base.ParseDefaultType(defaultType, DatabaseType.MariaDB);
    }
}