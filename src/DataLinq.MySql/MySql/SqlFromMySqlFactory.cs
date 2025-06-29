using System;
using System.Linq;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;

namespace DataLinq.MySql;

public class SqlFromMySqlFactory : SqlFromMetadataFactory
{
    public override DatabaseColumnType GetDbType(ColumnDefinition column)
    {
        if (column.DbTypes.Any(x => x.DatabaseType == DatabaseType.MySQL))
            return column.DbTypes.First(x => x.DatabaseType == DatabaseType.MySQL);

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

        if (dbType.DatabaseType == DatabaseType.MariaDB)
            type = ParseMariaDBType(dbType);
        else if (dbType.DatabaseType == DatabaseType.Default)
            type = ParseDefaultType(dbType);
        else if (dbType.DatabaseType == DatabaseType.SQLite)
            type = ParseSQLiteType(dbType, DatabaseType.MySQL);

        return type;
    }

    protected virtual DatabaseColumnType? ParseMariaDBType(DatabaseColumnType mariaDbType)
    {
        // Handle MariaDB-specific types when generating for MySQL
        return mariaDbType.Name.ToLower() switch
        {
            "uuid" => new DatabaseColumnType(DatabaseType.MySQL, "binary", 16), // Translate MariaDB UUID to MySQL BINARY(16)
            "inet6" => new DatabaseColumnType(DatabaseType.MySQL, "varchar", 45), // Translate INET6 to VARCHAR
            _ => mariaDbType.Mutate(DatabaseType.MySQL) // Assume other types are compatible
        };
    }

    protected DatabaseColumnType? GetDbTypeFromCsType(ValueProperty property) =>
        base.GetDbTypeFromCsType(property, DatabaseType.MySQL);

    protected virtual DatabaseColumnType? ParseDefaultType(DatabaseColumnType defaultType) =>
        base.ParseDefaultType(defaultType, DatabaseType.MySQL);
}