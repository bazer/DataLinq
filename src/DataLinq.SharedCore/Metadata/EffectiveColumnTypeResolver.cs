using System;

namespace DataLinq.Metadata;

/// <summary>
/// Resolves the effective physical column type for one concrete provider.
/// Provider-specific SQL factories and provider-neutral metadata features use
/// this shared implementation so translated and CLR-inferred types cannot
/// silently diverge.
/// </summary>
internal static class EffectiveColumnTypeResolver
{
    internal static DatabaseColumnType? Resolve(
        ColumnDefinition column,
        DatabaseType databaseType)
    {
        if (column is null)
            throw new ArgumentNullException(nameof(column));

        foreach (var dbType in column.DbTypes)
        {
            if (dbType.DatabaseType == databaseType)
                return dbType;
        }

        foreach (var dbType in column.DbTypes)
        {
            var translated = databaseType switch
            {
                DatabaseType.MySQL => TranslateToMySql(dbType),
                DatabaseType.MariaDB => TranslateToMariaDb(dbType),
                DatabaseType.SQLite => TranslateToSQLite(dbType),
                _ => null
            };

            if (translated is not null)
                return translated;
        }

        // Once a model declares physical type metadata, an unrecognized
        // translation is not equivalent to having no declaration. Falling
        // back to the CLR type here would fabricate a built-in UUID policy
        // that a custom provider hook could interpret differently.
        return column.DbTypes.Count == 0
            ? ResolveFromCanonicalProviderType(column, databaseType)
            : null;
    }

    private static DatabaseColumnType? TranslateToMySql(DatabaseColumnType sourceType)
    {
        if (sourceType.DatabaseType == DatabaseType.MariaDB)
        {
            if (IsType(sourceType, "uuid"))
                return new DatabaseColumnType(DatabaseType.MySQL, "binary", 16);

            if (IsType(sourceType, "inet6"))
                return new DatabaseColumnType(DatabaseType.MySQL, "varchar", 45);

            return sourceType.Mutate(DatabaseType.MySQL);
        }

        if (sourceType.DatabaseType == DatabaseType.Default)
            return TranslateDefaultType(sourceType, DatabaseType.MySQL);

        if (sourceType.DatabaseType == DatabaseType.SQLite)
            return TranslateSQLiteType(sourceType, DatabaseType.MySQL);

        return null;
    }

    private static DatabaseColumnType? TranslateToMariaDb(DatabaseColumnType sourceType)
    {
        if (sourceType.DatabaseType == DatabaseType.MySQL)
            return sourceType.Mutate(DatabaseType.MariaDB);

        if (sourceType.DatabaseType == DatabaseType.Default)
        {
            if (IsType(sourceType, "uuid"))
            {
                return new DatabaseColumnType(
                    DatabaseType.MariaDB,
                    "uuid",
                    sourceType.Length,
                    sourceType.Decimals,
                    sourceType.Signed);
            }

            return TranslateDefaultType(sourceType, DatabaseType.MariaDB);
        }

        if (sourceType.DatabaseType == DatabaseType.SQLite)
            return TranslateSQLiteType(sourceType, DatabaseType.MariaDB);

        return null;
    }

    private static DatabaseColumnType? TranslateToSQLite(DatabaseColumnType sourceType)
    {
        if (sourceType.DatabaseType == DatabaseType.Default)
            return TranslateDefaultTypeToSQLite(sourceType);

        if (sourceType.DatabaseType == DatabaseType.MySQL)
            return TranslateMySqlTypeToSQLite(sourceType);

        if (sourceType.DatabaseType == DatabaseType.MariaDB)
        {
            if (IsType(sourceType, "uuid"))
                return new DatabaseColumnType(DatabaseType.SQLite, "TEXT");

            return TranslateMySqlTypeToSQLite(sourceType);
        }

        return null;
    }

    private static DatabaseColumnType? TranslateDefaultType(
        DatabaseColumnType sourceType,
        DatabaseType databaseType)
    {
        if (IsType(sourceType, "uuid"))
        {
            return new DatabaseColumnType(
                databaseType,
                "binary",
                sourceType.Length ?? 16,
                sourceType.Decimals,
                sourceType.Signed);
        }

        var name = FirstMatchingType(
            sourceType,
            ("integer", "int"),
            ("big-integer", "bigint"),
            ("decimal", "decimal"),
            ("float", "float"),
            ("double", "double"),
            ("text", "text"),
            ("boolean", "bit"),
            ("datetime", "datetime"),
            ("timestamp", "timestamp"),
            ("date", "date"),
            ("time", "time"),
            ("blob", "blob"),
            ("json", "json"),
            ("xml", "longtext"));

        return name is null
            ? null
            : new DatabaseColumnType(
                databaseType,
                name,
                sourceType.Length,
                sourceType.Decimals,
                sourceType.Signed);
    }

    private static DatabaseColumnType? TranslateSQLiteType(
        DatabaseColumnType sourceType,
        DatabaseType databaseType)
    {
        var name = FirstMatchingType(
            sourceType,
            ("integer", "int"),
            ("text", "varchar"),
            ("real", "double"),
            ("blob", "binary"));

        return name is null
            ? null
            : new DatabaseColumnType(
                databaseType,
                name,
                sourceType.Length,
                sourceType.Decimals,
                sourceType.Signed);
    }

    private static DatabaseColumnType? TranslateDefaultTypeToSQLite(
        DatabaseColumnType sourceType)
    {
        var name = FirstMatchingType(
            sourceType,
            ("integer", "INTEGER"),
            ("big-integer", "INTEGER"),
            ("boolean", "INTEGER"),
            ("decimal", "REAL"),
            ("float", "REAL"),
            ("double", "REAL"),
            ("text", "TEXT"),
            ("json", "TEXT"),
            ("xml", "TEXT"),
            ("datetime", "TEXT"),
            ("timestamp", "TEXT"),
            ("date", "TEXT"),
            ("time", "TEXT"),
            ("uuid", "TEXT"),
            ("blob", "BLOB"));

        return name is null
            ? null
            : new DatabaseColumnType(
                DatabaseType.SQLite,
                name,
                sourceType.Length,
                sourceType.Decimals,
                sourceType.Signed);
    }

    private static DatabaseColumnType? TranslateMySqlTypeToSQLite(
        DatabaseColumnType sourceType)
    {
        string? name;
        if (IsAnyType(sourceType, "int", "tinyint", "smallint", "mediumint", "bigint", "bit", "year", "enum"))
            name = "INTEGER";
        else if (IsAnyType(sourceType, "double", "float", "decimal"))
            name = "REAL";
        else if (IsAnyType(sourceType, "varchar", "text", "mediumtext", "longtext", "char", "datetime", "timestamp", "date", "time", "json"))
            name = "TEXT";
        else if (IsAnyType(sourceType, "binary", "varbinary", "blob", "tinyblob", "mediumblob", "longblob"))
            name = "BLOB";
        else
            name = null;

        return name is null
            ? null
            : new DatabaseColumnType(
                DatabaseType.SQLite,
                name,
                sourceType.Length,
                sourceType.Decimals,
                sourceType.Signed);
    }

    internal static DatabaseColumnType? ResolveFromCanonicalProviderType(
        ColumnDefinition column,
        DatabaseType databaseType)
    {
        var clrType = column.ProviderClrType;
        if (clrType is not null)
        {
            clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;
            if (clrType.IsEnum)
                return ResolveCanonicalTypeName("enum", databaseType);

            return ResolveCanonicalClrType(clrType, databaseType);
        }

        var providerType = column.ProviderCsType;
        return ResolveCanonicalTypeName(
            providerType.ModelCsType == ModelCsType.Enum
                ? "enum"
                : providerType.Name,
            databaseType);
    }

    private static DatabaseColumnType? ResolveCanonicalClrType(
        Type clrType,
        DatabaseType databaseType)
    {
        if (clrType == typeof(sbyte)) return ResolveCanonicalTypeName("sbyte", databaseType);
        if (clrType == typeof(byte)) return ResolveCanonicalTypeName("byte", databaseType);
        if (clrType == typeof(short)) return ResolveCanonicalTypeName("short", databaseType);
        if (clrType == typeof(ushort)) return ResolveCanonicalTypeName("ushort", databaseType);
        if (clrType == typeof(int)) return ResolveCanonicalTypeName("int", databaseType);
        if (clrType == typeof(uint)) return ResolveCanonicalTypeName("uint", databaseType);
        if (clrType == typeof(long)) return ResolveCanonicalTypeName("long", databaseType);
        if (clrType == typeof(ulong)) return ResolveCanonicalTypeName("ulong", databaseType);
        if (clrType == typeof(decimal)) return ResolveCanonicalTypeName("decimal", databaseType);
        if (clrType == typeof(float)) return ResolveCanonicalTypeName("float", databaseType);
        if (clrType == typeof(double)) return ResolveCanonicalTypeName("double", databaseType);
        if (clrType == typeof(string)) return ResolveCanonicalTypeName("string", databaseType);
        if (clrType == typeof(char)) return ResolveCanonicalTypeName("char", databaseType);
        if (clrType == typeof(DateTime)) return ResolveCanonicalTypeName("datetime", databaseType);
        if (string.Equals(clrType.FullName, "System.DateOnly", StringComparison.Ordinal)) return ResolveCanonicalTypeName("dateonly", databaseType);
        if (string.Equals(clrType.FullName, "System.TimeOnly", StringComparison.Ordinal)) return ResolveCanonicalTypeName("timeonly", databaseType);
        if (clrType == typeof(bool)) return ResolveCanonicalTypeName("bool", databaseType);
        if (clrType == typeof(Guid)) return ResolveCanonicalTypeName("guid", databaseType);
        if (clrType == typeof(byte[])) return ResolveCanonicalTypeName("byte[]", databaseType);

        return null;
    }

    private static DatabaseColumnType? ResolveCanonicalTypeName(
        string typeName,
        DatabaseType databaseType)
    {
        if (databaseType == DatabaseType.SQLite)
        {
            if (IsAnyName(typeName, "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "bool", "enum"))
                return new DatabaseColumnType(databaseType, "INTEGER");

            if (IsAnyName(typeName, "decimal", "float", "double"))
                return new DatabaseColumnType(databaseType, "REAL");

            if (IsAnyName(typeName, "string", "char", "datetime", "dateonly", "timeonly", "guid", "System.Guid"))
                return new DatabaseColumnType(databaseType, "TEXT");

            if (IsAnyName(typeName, "byte[]", "System.Byte[]"))
                return new DatabaseColumnType(databaseType, "BLOB");

            return null;
        }

        if (databaseType is not (DatabaseType.MySQL or DatabaseType.MariaDB))
            return null;

        if (databaseType == DatabaseType.MariaDB && IsAnyName(typeName, "guid", "System.Guid"))
            return new DatabaseColumnType(databaseType, "uuid");

        if (IsAnyName(typeName, "sbyte")) return new DatabaseColumnType(databaseType, "tinyint", signed: true);
        if (IsAnyName(typeName, "byte")) return new DatabaseColumnType(databaseType, "tinyint", signed: false);
        if (IsAnyName(typeName, "short")) return new DatabaseColumnType(databaseType, "smallint", signed: true);
        if (IsAnyName(typeName, "ushort")) return new DatabaseColumnType(databaseType, "smallint", signed: false);
        if (IsAnyName(typeName, "int")) return new DatabaseColumnType(databaseType, "int", signed: true);
        if (IsAnyName(typeName, "uint")) return new DatabaseColumnType(databaseType, "int", signed: false);
        if (IsAnyName(typeName, "long")) return new DatabaseColumnType(databaseType, "bigint", signed: true);
        if (IsAnyName(typeName, "ulong")) return new DatabaseColumnType(databaseType, "bigint", signed: false);
        if (IsAnyName(typeName, "decimal")) return new DatabaseColumnType(databaseType, "decimal", 18, 4);
        if (IsAnyName(typeName, "float")) return new DatabaseColumnType(databaseType, "float");
        if (IsAnyName(typeName, "double")) return new DatabaseColumnType(databaseType, "double");
        if (IsAnyName(typeName, "string")) return new DatabaseColumnType(databaseType, "varchar", 255);
        if (IsAnyName(typeName, "char")) return new DatabaseColumnType(databaseType, "char", 1);
        if (IsAnyName(typeName, "datetime")) return new DatabaseColumnType(databaseType, "datetime");
        if (IsAnyName(typeName, "dateonly")) return new DatabaseColumnType(databaseType, "date");
        if (IsAnyName(typeName, "timeonly")) return new DatabaseColumnType(databaseType, "time");
        if (IsAnyName(typeName, "bool")) return new DatabaseColumnType(databaseType, "bit", 1);
        if (IsAnyName(typeName, "guid", "System.Guid")) return new DatabaseColumnType(databaseType, "binary", 16);
        if (IsAnyName(typeName, "byte[]", "System.Byte[]")) return new DatabaseColumnType(databaseType, "blob");
        if (IsAnyName(typeName, "enum")) return new DatabaseColumnType(databaseType, "enum");

        return null;
    }

    private static string? FirstMatchingType(
        DatabaseColumnType sourceType,
        params (string Source, string Target)[] mappings)
    {
        for (var i = 0; i < mappings.Length; i++)
        {
            if (string.Equals(sourceType.Name, mappings[i].Source, StringComparison.OrdinalIgnoreCase))
                return mappings[i].Target;
        }

        return null;
    }

    private static bool IsType(DatabaseColumnType dbType, string name) =>
        string.Equals(dbType.Name, name, StringComparison.OrdinalIgnoreCase);

    private static bool IsAnyType(DatabaseColumnType dbType, params string[] names) =>
        IsAnyName(dbType.Name, names);

    private static bool IsAnyName(string value, params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            if (string.Equals(value, names[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
