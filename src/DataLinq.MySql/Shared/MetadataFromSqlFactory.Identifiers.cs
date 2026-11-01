using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Core.Factories;
using DataLinq.Validation;

namespace DataLinq.MySql;

public abstract partial class MetadataFromSqlFactory
{
    private static string? RuntimeSchemaName(IReadOnlyList<string> visibleDatabases, string expected, SchemaIdentifierRules identifiers)
    {
        var matches = visibleDatabases.Where(name => identifiers.DatabaseNames.Equals(name, expected)).ToArray();
        SchemaIdentifierRules.RequireUnambiguous(matches, identifiers.DatabaseNames, "database", model: false);
        return matches.SingleOrDefault();
    }

    private static string RuntimeCatalogQuery(string query) => query
        .Replace("WHERE SCHEMA_NAME = @schema", "WHERE CAST(SCHEMA_NAME AS BINARY) = CAST(@schema AS BINARY)")
        .Replace("WHERE TABLE_SCHEMA = @schema", "WHERE CAST(TABLE_SCHEMA AS BINARY) = CAST(@schema AS BINARY)")
        .Replace("WHERE tc.TABLE_SCHEMA = @schema", "WHERE CAST(tc.TABLE_SCHEMA AS BINARY) = CAST(@schema AS BINARY)")
        .Replace("WHERE CONSTRAINT_SCHEMA = @schema", "WHERE CAST(CONSTRAINT_SCHEMA AS BINARY) = CAST(@schema AS BINARY)")
        .Replace("AND TABLE_NAME = @table", "AND CAST(TABLE_NAME AS BINARY) = CAST(@table AS BINARY)")
        .Replace("AND TABLE_NAME = @view", "AND CAST(TABLE_NAME AS BINARY) = CAST(@view AS BINARY)")
        .Replace("cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA", "CAST(cc.CONSTRAINT_SCHEMA AS BINARY) = CAST(tc.CONSTRAINT_SCHEMA AS BINARY)")
        .Replace("cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME", "CAST(cc.CONSTRAINT_NAME AS BINARY) = CAST(tc.CONSTRAINT_NAME AS BINARY)");

    private string NativeCheckQuery(bool runtime) => $"""
        SELECT tc.TABLE_NAME, cc.CONSTRAINT_NAME, cc.CHECK_CLAUSE
        FROM information_schema.TABLE_CONSTRAINTS tc
        JOIN information_schema.CHECK_CONSTRAINTS cc ON cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA AND cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
            {(runtime && databaseType == DatabaseType.MariaDB ? "AND CAST(cc.TABLE_NAME AS BINARY) = CAST(tc.TABLE_NAME AS BINARY)" : "")}
        WHERE tc.TABLE_SCHEMA = @schema AND tc.CONSTRAINT_TYPE = 'CHECK'
        ORDER BY tc.TABLE_NAME, cc.CONSTRAINT_NAME
        """;

    // Native catalog collations must not merge distinct case-sensitive names.
    // Resolve references by identifier kind, then use canonical observed names
    // when building the existing finalized metadata graph.
    private static string CanonicalTable(ProviderDatabaseDraft database, string? name, SchemaIdentifierRules identifiers) =>
        database.TableModels.SingleOrDefault(table => identifiers.TableNames.Equals(table.Table.DbName, name))?.Table.DbName ?? name!;

    private static string CanonicalColumn(ProviderDatabaseDraft database, string? tableName, string? columnName, SchemaIdentifierRules identifiers) =>
        database.TableModels.SingleOrDefault(table => identifiers.TableNames.Equals(table.Table.DbName, tableName))?
            .Table.Columns.SingleOrDefault(column => identifiers.ColumnNames.Equals(column.Column.DbName, columnName))?.Column.DbName ?? columnName!;

    private static IReadOnlyList<NativeKey> CanonicalKeys(ProviderDatabaseDraft database, IReadOnlyList<NativeKey> keys, SchemaIdentifierRules? identifiers) =>
        identifiers is null ? keys : keys.Select(key => key with
        {
            Table = CanonicalTable(database, key.Table, identifiers),
            Column = CanonicalColumn(database, key.Table, key.Column, identifiers),
            ReferencedTable = identifiers.DatabaseNames.Equals(key.ReferencedSchema, database.DbName)
                ? CanonicalTable(database, key.ReferencedTable, identifiers) : key.ReferencedTable,
            ReferencedColumn = identifiers.DatabaseNames.Equals(key.ReferencedSchema, database.DbName)
                ? CanonicalColumn(database, key.ReferencedTable, key.ReferencedColumn, identifiers) : key.ReferencedColumn
        }).ToArray();

    private static IReadOnlyList<NativeIndex> CanonicalIndexes(ProviderDatabaseDraft database, IReadOnlyList<NativeIndex> indexes, SchemaIdentifierRules? identifiers) =>
        identifiers is null ? indexes : indexes.Select(index => index with
        {
            Table = CanonicalTable(database, index.Table, identifiers),
            Column = CanonicalColumn(database, index.Table, index.Column, identifiers)
        }).ToArray();

    private sealed class CatalogObjectComparer(SchemaIdentifierRules identifiers, StringComparer names) : IEqualityComparer<(string? Table, string? Name)>
    {
        public bool Equals((string? Table, string? Name) x, (string? Table, string? Name) y) =>
            identifiers.TableNames.Equals(x.Table, y.Table) && names.Equals(x.Name, y.Name);
        public int GetHashCode((string? Table, string? Name) value) =>
            HashCode.Combine(value.Table is null ? 0 : identifiers.TableNames.GetHashCode(value.Table), value.Name is null ? 0 : names.GetHashCode(value.Name));
    }
}
