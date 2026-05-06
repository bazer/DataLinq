using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.MariaDB.information_schema;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Shared;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.MariaDB;

public class MetadataFromMariaDBFactoryCreator : IMetadataFromDatabaseFactoryCreator
{
    public IMetadataFromSqlFactory GetMetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options)
    {
        return new MetadataFromMariaDBFactory(options);
    }
}

public class MetadataFromMariaDBFactory(MetadataFromDatabaseFactoryOptions options)
    : MetadataFromSqlFactory(options, DatabaseType.MariaDB)
{
    public override Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString) => DLOptionFailure.CatchAll<DatabaseDefinition>(() =>
    {
        var informationSchemaDb = new MariaDBDatabase<MariaDBInformationSchema>(connectionString, "information_schema");

        var database = new ProviderDatabaseDraft(
            name,
            new CsTypeDeclaration(csTypeName, csNamespace, ModelCsType.Class),
            dbName);

        if (!informationSchemaDb.Query()
            .TABLES.Where(x => x.TABLE_SCHEMA == dbName)
            .AsEnumerable()
            .Select(x => ParseTable(database, informationSchemaDb, x))
            .Transpose()
            .TryUnwrap(out var tableModels, out var tableFailure))
            return SingleOrAggregate(tableFailure);

        database.TableModels.AddRange(tableModels.Where(IsTableOrViewInOptionsList));

        var missingTables = FindMissingTablesOrViewInOptionsList(database.TableModels).ToList();
        if (missingTables.Count != 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Could not find the specified tables or views: {missingTables.ToJoinedString(", ")}");

        if (database.TableModels.Count == 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"No tables or views found in database '{dbName}'. Please check the connection string and database name.");

        if (!ParseIndices(database, informationSchemaDb).TryUnwrap(out _, out var indexFailure))
            return indexFailure;

        if (!ParseRelations(database, informationSchemaDb).TryUnwrap(out _, out var relationFailure))
            return relationFailure;

        ParseCheckConstraints(database, informationSchemaDb.Provider.DatabaseAccess);
        return new MetadataDefinitionFactory()
            .BuildProviderMetadata(database.ToMetadataDraft());
    });

    protected Option<bool, IDLOptionFailure> ParseIndices(ProviderDatabaseDraft database, MariaDBDatabase<MariaDBInformationSchema> informationSchemaDb)
    {
        // Fetch table-column pairs that are part of a foreign key relationship
        var foreignKeyColumns = informationSchemaDb.Query().KEY_COLUMN_USAGE
            .Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_TABLE_NAME != null)
            .Select(x => new { x.TABLE_NAME, x.COLUMN_NAME, x.CONSTRAINT_NAME })
            .ToList();

        var indexGroups = informationSchemaDb.Query()
            .STATISTICS.Where(x => x.TABLE_SCHEMA == database.DbName && x.INDEX_NAME != "PRIMARY")
            .ToList()
            .Where(x => !foreignKeyColumns.Any(fk =>
                fk.TABLE_NAME == x.TABLE_NAME &&
                fk.COLUMN_NAME == x.COLUMN_NAME &&
                fk.CONSTRAINT_NAME == x.INDEX_NAME))
            .GroupBy(x => new { x.TABLE_NAME, x.INDEX_NAME });

        foreach (var dbIndexGroup in indexGroups)
        {
            var indexedColumns = dbIndexGroup.OrderBy(x => x.SEQ_IN_INDEX).ToList();
            var unsupportedIndexReason = GetUnsupportedIndexReason(indexedColumns);
            if (unsupportedIndexReason != null)
            {
                options.Log?.Invoke($"Warning: Skipping unsupported MariaDB {unsupportedIndexReason} index '{dbIndexGroup.First().INDEX_NAME}' on table '{dbIndexGroup.First().TABLE_NAME}'.");
                continue;
            }

            var dbIndex = dbIndexGroup.First();
            if (string.IsNullOrWhiteSpace(dbIndex.TABLE_NAME) || string.IsNullOrWhiteSpace(dbIndex.INDEX_NAME))
                return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"MariaDB index metadata is missing a table or index name in database '{database.DbName}'.");

            var tableName = dbIndex.TABLE_NAME;
            var indexName = dbIndex.INDEX_NAME;
            if (!ParseIndexType(dbIndex.INDEX_TYPE, tableName, indexName).TryUnwrap(out var indexType, out var indexTypeFailure))
                return indexTypeFailure;

            var indexCharacteristic = dbIndex.NON_UNIQUE == 0
                ? IndexCharacteristic.Unique
                : IndexCharacteristic.Simple;

            var columns = new List<ProviderValuePropertyDraft>();
            var skipIndex = false;
            foreach (var indexColumn in indexedColumns)
            {
                var column = database
                    .TableModels.SingleOrDefault(x => x.Table.DbName == indexColumn.TABLE_NAME)?
                    .Table.Columns.SingleOrDefault(x => x.Column.DbName == indexColumn.COLUMN_NAME);

                if (column == null)
                {
                    options.Log?.Invoke($"Warning: Skipping MariaDB index '{dbIndexGroup.First().INDEX_NAME}' on table '{dbIndexGroup.First().TABLE_NAME}' because column '{indexColumn.COLUMN_NAME}' was not imported.");
                    skipIndex = true;
                    break;
                }

                columns.Add(column);
            }

            if (skipIndex || columns.Count == 0)
                continue;

            var columnNames = columns.Select(x => x.Column.DbName).ToArray();
            foreach (var column in columns)
                column.Attributes.Add(new IndexAttribute(indexName, indexCharacteristic, indexType, columnNames));
        }

        return true;
    }

    private static string? GetUnsupportedIndexReason(IReadOnlyList<STATISTICS> indexedColumns)
    {
        if (indexedColumns.Any(x => x.SUB_PART.HasValue))
            return "prefix-length";

        if (indexedColumns.Any(x => string.Equals(x.COLLATION, "D", StringComparison.OrdinalIgnoreCase)))
            return "descending";

        if (indexedColumns.Any(x => string.Equals(x.IGNORED, "YES", StringComparison.OrdinalIgnoreCase)))
            return "ignored";

        return null;
    }

    protected Option<bool, IDLOptionFailure> ParseRelations(ProviderDatabaseDraft database, MariaDBDatabase<MariaDBInformationSchema> informationSchemaDb)
    {
        var referentialActions = ParseReferentialActions(database, informationSchemaDb.Provider.DatabaseAccess);

        foreach (var key in informationSchemaDb.Query()
            .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_COLUMN_NAME != null))
        {
            if (!ParseForeignKeyReference(
                database.DbName,
                key.TABLE_NAME,
                key.COLUMN_NAME,
                key.REFERENCED_TABLE_NAME,
                key.REFERENCED_COLUMN_NAME,
                key.CONSTRAINT_NAME).TryUnwrap(out var foreignKey, out var foreignKeyFailure))
                return foreignKeyFailure;

            var foreignKeyColumn = database
                .TableModels.SingleOrDefault(x => x.Table.DbName == foreignKey.TableName)?
                .Table.Columns.SingleOrDefault(x => x.Column.DbName == foreignKey.ColumnName);

            if (foreignKeyColumn == null)
                continue;

            var candidateColumn = database
                .TableModels.SingleOrDefault(x => x.Table.DbName == foreignKey.ReferencedTableName)?
                .Table.Columns.SingleOrDefault(x => x.Column.DbName == foreignKey.ReferencedColumnName);

            if (candidateColumn == null)
            {
                options.Log?.Invoke($"Warning: Skipping foreign key '{foreignKey.ConstraintName}' on table '{foreignKey.TableName}' because referenced column '{foreignKey.ReferencedTableName}.{foreignKey.ReferencedColumnName}' was not imported.");
                continue;
            }

            referentialActions.TryGetValue((foreignKey.TableName, foreignKey.ConstraintName), out var actions);

            // The only job of this method is to mark the column and add the attribute.
            foreignKeyColumn.Column.ForeignKey = true;
            foreignKeyColumn.Attributes.Add(new ForeignKeyAttribute(
                foreignKey.ReferencedTableName,
                foreignKey.ReferencedColumnName,
                foreignKey.ConstraintName,
                (int)key.ORDINAL_POSITION,
                actions.OnUpdate,
                actions.OnDelete));
        }

        return true;
    }

    protected Option<ProviderTableModelDraft, IDLOptionFailure> ParseTable(ProviderDatabaseDraft database, MariaDBDatabase<MariaDBInformationSchema> informationSchemaDb, TABLES dbTables)
    {
        var type = dbTables.TABLE_TYPE == TABLE_TYPE.BASE_TABLE ? TableType.Table : TableType.View;

        if (dbTables.TABLE_NAME == null)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"MariaDB table metadata is missing a table name in database '{database.DbName}'.");

        var table = new ProviderTableDraft(dbTables.TABLE_NAME, type);

        var csName = table.DbName.ToCSharpIdentifier(options.CapitaliseNames);

        var tableModel = new ProviderTableModelDraft(
            csName,
            new CsTypeDeclaration(csName, database.CsType.Namespace, ModelCsType.Class),
            table);
        if (!string.IsNullOrWhiteSpace(dbTables.TABLE_COMMENT))
            tableModel.ModelAttributes.Add(new CommentAttribute(dbTables.TABLE_COMMENT));

        if (type == TableType.View)
            table.Definition = GetViewDefinition(informationSchemaDb, database.DbName, table.DbName);

        if (!informationSchemaDb.Query()
            .COLUMNS.Where(x => x.TABLE_SCHEMA == database.DbName && x.TABLE_NAME == table.DbName)
            .AsEnumerable()
            .OrderBy(x => x.ORDINAL_POSITION)
            .Select(x => ParseColumn(table, (ICOLUMNS)x))
            .Transpose()
            .TryUnwrap(out var columnImports, out var columnFailure))
            return SingleOrAggregate(columnFailure);

        table.Columns.AddRange(columnImports.Select(x => x.Property).OfType<ProviderValuePropertyDraft>());

        return tableModel;
    }

    private static IDLOptionFailure SingleOrAggregate(IReadOnlyCollection<IDLOptionFailure> failures) =>
        failures.Count == 1
            ? failures.Single()
            : DLOptionFailure.AggregateFail(failures);

    private static string GetViewDefinition(MariaDBDatabase<MariaDBInformationSchema> informationSchemaDb, string databaseName, string viewName)
    {
        var definition = informationSchemaDb.Query()
            .VIEWS.Where(x => x.TABLE_SCHEMA == databaseName && x.TABLE_NAME == viewName)
            .AsEnumerable()
            .Select(x => x.VIEW_DEFINITION)
            .FirstOrDefault();

        definition = CleanViewDefinition(definition, databaseName);
        if (!string.IsNullOrWhiteSpace(definition))
            return definition;

        var fallback = TryGetViewDefinitionFromShowCreate(informationSchemaDb, databaseName, viewName);
        fallback = CleanViewDefinition(fallback, databaseName);

        return fallback ?? string.Empty;
    }

    private static string? TryGetViewDefinitionFromShowCreate(MariaDBDatabase<MariaDBInformationSchema> informationSchemaDb, string databaseName, string viewName)
    {
        try
        {
            using var reader = informationSchemaDb.Provider.DatabaseAccess.ExecuteReader($"SHOW CREATE VIEW `{databaseName}`.`{viewName}`");
            if (!reader.ReadNextRow())
                return null;

            string? createViewSql = null;
            try
            {
                var ordinal = reader.GetOrdinal("Create View");
                createViewSql = reader.GetString(ordinal);
            }
            catch
            {
                try
                {
                    createViewSql = reader.GetString(1);
                }
                catch
                {
                    return null;
                }
            }

            if (string.IsNullOrWhiteSpace(createViewSql))
                return null;

            return ExtractSelectFromCreateView(createViewSql, databaseName, viewName);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractSelectFromCreateView(string createViewSql, string databaseName, string viewName)
    {
        var markerWithDb = $"VIEW `{databaseName}`.`{viewName}` AS ";
        var markerWithoutDb = $"VIEW `{viewName}` AS ";

        var index = createViewSql.IndexOf(markerWithDb, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
            return createViewSql[(index + markerWithDb.Length)..];

        index = createViewSql.IndexOf(markerWithoutDb, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
            return createViewSql[(index + markerWithoutDb.Length)..];

        var asIndex = createViewSql.IndexOf(" AS ", StringComparison.OrdinalIgnoreCase);
        return asIndex >= 0 ? createViewSql[(asIndex + 4)..] : createViewSql;
    }

    private static string? CleanViewDefinition(string? definition, string databaseName)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        return definition
            .Replace($"`{databaseName}`.", "")
            .Replace($"{databaseName}.", "")
            .Trim();
    }

    protected override Option<string, IDLOptionFailure> ParseCsType(DatabaseColumnType dbType, string tableName, string columnName)
    {
        if (dbType.Name.ToLower().StartsWith("uuid"))
            return "Guid";

        return base.ParseCsType(dbType, tableName, columnName);
    }
}
