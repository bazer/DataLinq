using System;

namespace DataLinq.Validation;

public sealed class SchemaValidationCapabilities
{
    private SchemaValidationCapabilities(
        DatabaseType databaseType,
        SchemaIdentifierComparison tableNameComparison,
        SchemaIdentifierComparison columnNameComparison,
        bool compareTables,
        bool compareColumns,
        bool compareColumnOrder,
        bool compareNullability,
        bool comparePrimaryKeys,
        bool compareAutoIncrement,
        bool compareDefaults,
        bool compareIndexes,
        bool compareForeignKeys,
        bool compareChecks,
        bool compareComments)
    {
        DatabaseType = databaseType;
        TableNameComparison = tableNameComparison;
        ColumnNameComparison = columnNameComparison;
        CompareTables = compareTables;
        CompareColumns = compareColumns;
        CompareColumnOrder = compareColumnOrder;
        CompareNullability = compareNullability;
        ComparePrimaryKeys = comparePrimaryKeys;
        CompareAutoIncrement = compareAutoIncrement;
        CompareDefaults = compareDefaults;
        CompareIndexes = compareIndexes;
        CompareForeignKeys = compareForeignKeys;
        CompareChecks = compareChecks;
        CompareComments = compareComments;
    }

    public DatabaseType DatabaseType { get; }
    public SchemaIdentifierComparison TableNameComparison { get; }
    public SchemaIdentifierComparison ColumnNameComparison { get; }
    public bool CompareTables { get; }
    public bool CompareColumns { get; }
    public bool CompareColumnOrder { get; }
    public bool CompareNullability { get; }
    public bool ComparePrimaryKeys { get; }
    public bool CompareAutoIncrement { get; }
    public bool CompareDefaults { get; }
    public bool CompareIndexes { get; }
    public bool CompareForeignKeys { get; }
    public bool CompareChecks { get; }
    public bool CompareComments { get; }

    public StringComparer TableNameComparer => GetComparer(TableNameComparison);
    public StringComparer ColumnNameComparer => GetComparer(ColumnNameComparison);

    public static SchemaValidationCapabilities For(DatabaseType databaseType)
    {
        switch (databaseType)
        {
            case DatabaseType.SQLite:
                return new SchemaValidationCapabilities(
                    databaseType,
                    SchemaIdentifierComparison.OrdinalIgnoreCase,
                    SchemaIdentifierComparison.OrdinalIgnoreCase,
                    compareTables: true,
                    compareColumns: true,
                    compareColumnOrder: false,
                    compareNullability: false,
                    comparePrimaryKeys: false,
                    compareAutoIncrement: false,
                    compareDefaults: false,
                    compareIndexes: true,
                    compareForeignKeys: true,
                    compareChecks: false,
                    compareComments: false);

            case DatabaseType.MySQL:
            case DatabaseType.MariaDB:
                return new SchemaValidationCapabilities(
                    databaseType,
                    SchemaIdentifierComparison.RequiresProviderConfiguration,
                    SchemaIdentifierComparison.OrdinalIgnoreCase,
                    compareTables: true,
                    compareColumns: true,
                    compareColumnOrder: false,
                    compareNullability: false,
                    comparePrimaryKeys: false,
                    compareAutoIncrement: false,
                    compareDefaults: false,
                    compareIndexes: true,
                    compareForeignKeys: true,
                    compareChecks: true,
                    compareComments: true);

            default:
                return new SchemaValidationCapabilities(
                    databaseType,
                    SchemaIdentifierComparison.Ordinal,
                    SchemaIdentifierComparison.Ordinal,
                    compareTables: true,
                    compareColumns: true,
                    compareColumnOrder: false,
                    compareNullability: false,
                    comparePrimaryKeys: false,
                    compareAutoIncrement: false,
                    compareDefaults: false,
                    compareIndexes: false,
                    compareForeignKeys: false,
                    compareChecks: false,
                    compareComments: false);
        }
    }

    private static StringComparer GetComparer(SchemaIdentifierComparison comparison)
    {
        return comparison == SchemaIdentifierComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }
}
