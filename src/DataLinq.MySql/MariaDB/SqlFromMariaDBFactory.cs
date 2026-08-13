using System;
using System.Linq;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.MySql;

namespace DataLinq.MariaDB;

public class SqlFromMariaDBFactory : SqlFromMetadataFactory
{
    protected override DatabaseType DatabaseType => DataLinq.DatabaseType.MariaDB;

    public override DatabaseColumnType GetDbType(ColumnDefinition column)
    {
        if (column.DbTypes.Any(x => x.DatabaseType == DatabaseType.MariaDB))
            return column.DbTypes.First(x => x.DatabaseType == DatabaseType.MariaDB);

        var fallbackType = column.HasScalarConverter
            ? EffectiveColumnTypeResolver.ResolveFromCanonicalProviderType(column, DatabaseType.MariaDB)
            : GetDbTypeFromCsType(column.ValueProperty);
        var type = column.DbTypes
            .Select(x => TryGetColumnType(column, x))
            .Concat(fallbackType.Yield())
            .Where(x => x != null)
            .FirstOrDefault();

        if (type == null)
            throw new Exception($"Could not find a MySQL database type for '{column.Table.Model.CsType.Name}.{column.ValueProperty.PropertyName}'");

        return type;
    }

    protected virtual DatabaseColumnType? TryGetColumnType(
        ColumnDefinition column,
        DatabaseColumnType dbType)
    {
        var translated = TryGetColumnType(dbType);
        if ((column.IsGuidColumn ||
             (column.ProviderClrType is null &&
              (column.ProviderCsType.Name.Equals("Guid", StringComparison.Ordinal) ||
               column.ProviderCsType.Name.Equals("System.Guid", StringComparison.Ordinal)))) &&
            dbType.DatabaseType == DatabaseType.Default &&
            dbType.Name.Equals("uuid", StringComparison.OrdinalIgnoreCase) &&
            translated is not null &&
            translated.Name.Equals("binary", StringComparison.OrdinalIgnoreCase) &&
            translated.Length == dbType.Length &&
            translated.Decimals == dbType.Decimals &&
            translated.Signed == dbType.Signed)
        {
            return ParseDefaultType(dbType);
        }

        return translated;
    }

    protected virtual DatabaseColumnType? TryGetColumnType(DatabaseColumnType dbType)
    {
        DatabaseColumnType? type = null;

        if (dbType.DatabaseType == DatabaseType.MySQL)
            type = ParseMySQLType(dbType);
        else if (dbType.DatabaseType == DatabaseType.Default)
            type = base.ParseDefaultType(dbType, DatabaseType.MariaDB);
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
        {
            return new DatabaseColumnType(
                DatabaseType.MariaDB,
                "uuid",
                defaultType.Length,
                defaultType.Decimals,
                defaultType.Signed);
        }

        return base.ParseDefaultType(defaultType, DatabaseType.MariaDB);
    }
}
