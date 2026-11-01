using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Execution;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.MySql.Shared;
using DataLinq.Query;
using DataLinq.Validation;
using MySqlConnector;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.MySql;

public abstract partial class MetadataFromSqlFactory : IAsyncMetadataFactory
{
    /// <summary>Imports live metadata asynchronously. Cancellation escapes the non-cancellation Option failure contract.</summary>
    public virtual Task<Option<DatabaseDefinition, IDLOptionFailure>> ParseDatabaseAsync(string name, string csTypeName,
        string csNamespace, string dbName, string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbName);
        return this.ParseDatabaseAsyncCore(name, csTypeName, csNamespace, dbName, connectionString, cancellationToken);
    }

    MetadataFromDatabaseFactoryOptions IAsyncMetadataFactory.Options => options;

    IAsyncMetadataReadPlan IAsyncMetadataFactory.CaptureImport(MetadataImportRequest request)
    {
        // A captured built-in parser cannot preserve arbitrary synchronous
        // overrides in a derived factory. Never silently substitute its behavior.
        if (GetType() != typeof(MetadataFromMySqlFactory) && GetType() != typeof(DataLinq.MariaDB.MetadataFromMariaDBFactory))
            throw new NotSupportedException("This derived metadata factory does not support captured asynchronous reading.");
        var connectionString = new MySqlConnectionStringBuilder(request.ConnectionString).ConnectionString;
        return CaptureNativeRead(databaseType, request,
            () => SqlAdministrativeSession.CreateOwned(connectionString, CapturedSql.Capture(new Sql("SELECT 1")), ExecutionOperationKind.MetadataRead));
    }

    internal static IAsyncMetadataReadPlan CaptureNativeRead(DatabaseType type, MetadataImportRequest request,
        Func<IAsyncMetadataSession> createSession, Action? validateLifecycle = null)
    {
        // Only local parsing is reused. This factory never constructs a runtime
        // provider, registers generated information-schema models or performs setup.
        var parser = GetSqlFactory(new()
        {
            CapitaliseNames = request.Settings.CapitaliseNames,
            DeclareEnumsInClass = request.Settings.DeclareEnumsInClass,
            Include = request.Settings.Include.ToList(),
            Log = request.Settings.Log
        }, type);
        parser.limitations = request.Settings.Limitations;
        return new NativeMetadataPlan(parser, request, createSession, validateLifecycle);
    }

    private sealed class NativeMetadataPlan(MetadataFromSqlFactory parser, MetadataImportRequest request,
        Func<IAsyncMetadataSession> createSession, Action? validateLifecycle) : IAsyncMetadataReadPlan
    {
        public void Validate() => validateLifecycle?.Invoke();
        public IAsyncMetadataSession CreateSession() => createSession();
        public Task<Option<DatabaseDefinition, IDLOptionFailure>> ReadAsync(MetadataReadContext context, CancellationToken token) =>
            parser.ReadNativeMetadataAsync(context, request, token);
    }

    private async Task<Option<DatabaseDefinition, IDLOptionFailure>> ReadNativeMetadataAsync(
        MetadataReadContext context, MetadataImportRequest request, CancellationToken token)
    {
        SchemaIdentifierRules? identifiers = null;
        if (request.Settings.Purpose == MetadataReadPurpose.RuntimeValidation)
        {
            identifiers = SchemaIdentifierRules.Server(databaseType, await context.ExecuteScalarAsync(new Sql("SELECT @@lower_case_table_names")).ConfigureAwait(false));
            request.Settings.SetIdentifiers(identifiers);
        }
        var database = new ProviderDatabaseDraft(request.Name,
            new CsTypeDeclaration(request.CsTypeName, request.CsNamespace, ModelCsType.Class), request.DatabaseName);
        var schema = identifiers is null ? request.DatabaseName
            : RuntimeSchemaName(await context.ReadAsync(new Sql("SELECT SCHEMA_NAME FROM information_schema.SCHEMATA"), row => Text(row, "SCHEMA_NAME")!).ConfigureAwait(false), request.DatabaseName, identifiers);
        Sql Query(string text) => new Sql(identifiers is null ? text : RuntimeCatalogQuery(text)).AddParameter("@schema", schema);

        // An empty result from information_schema alone cannot distinguish an
        // existing empty schema from a missing/inaccessible schema.
        var exists = identifiers is null ? await context.ExecuteScalarAsync(Query("SELECT 1 FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @schema LIMIT 1")).ConfigureAwait(false) : schema;
        if (exists is null || exists == DBNull.Value)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Database '{request.DatabaseName}' does not exist or is not visible to this connection.");

        var tables = await context.ReadAsync(Query("SELECT TABLE_NAME, TABLE_TYPE, TABLE_COMMENT FROM information_schema.TABLES WHERE TABLE_SCHEMA = @schema ORDER BY TABLE_NAME"),
            row => new NativeTable(Text(row, "TABLE_NAME")!, Text(row, "TABLE_TYPE"), Text(row, "TABLE_COMMENT"))).ConfigureAwait(false);
        var failures = new List<IDLOptionFailure>();
        if (identifiers is not null)
            SchemaIdentifierRules.RequireUnambiguous(tables.Select(table => table.Name), identifiers.TableNames, "table/view", model: false);
        foreach (var item in tables.Where(item => IsTableOrViewNameInOptionsList(item.Name)))
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                failures.Add(DLOptionFailure.Fail(DLFailureType.InvalidModel, $"{databaseType} table metadata is missing a table name in database '{database.DbName}'."));
                continue;
            }
            var table = new ProviderTableDraft(item.Name, item.Type == "BASE TABLE" ? TableType.Table : TableType.View);
            var csName = table.DbName.ToCSharpIdentifier(options.CapitaliseNames);
            var model = new ProviderTableModelDraft(csName, new CsTypeDeclaration(csName, database.CsType.Namespace, ModelCsType.Class), table);
            if (!string.IsNullOrWhiteSpace(item.Comment)) model.ModelAttributes.Add(new CommentAttribute(item.Comment));
            if (table.Type == TableType.View)
                table.Definition = await ReadViewAsync(context, schema!, table.DbName, identifiers is not null).ConfigureAwait(false);
            var columns = await context.ReadAsync(Query($"""
                SELECT TABLE_SCHEMA, TABLE_NAME, DATA_TYPE, COLUMN_TYPE, NUMERIC_PRECISION, NUMERIC_SCALE,
                    CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, COLUMN_KEY, EXTRA, GENERATION_EXPRESSION,
                    COLUMN_DEFAULT, COLUMN_NAME, COLUMN_COMMENT,
                    {(databaseType == DatabaseType.MariaDB ? "IS_GENERATED" : "'NEVER' AS IS_GENERATED")}
                FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                ORDER BY ORDINAL_POSITION
                """).AddParameter("@table", table.DbName), NativeColumn.Read).ConfigureAwait(false);
            if (identifiers is not null)
                SchemaIdentifierRules.RequireUnambiguous(columns.Select(column => column.COLUMN_NAME!), identifiers.ColumnNames, $"column in '{table.DbName}'", model: false);
            foreach (var column in columns)
            {
                token.ThrowIfCancellationRequested();
                if (ParseColumn(table, column).TryUnwrap(out var imported, out var failure))
                {
                    if (imported.Property is { } property) table.Columns.Add(property);
                }
                else failures.Add(failure);
            }
            database.TableModels.Add(model);
        }
        if (failures.Count != 0) return Combine(failures);
        var missing = FindMissingTablesOrViewInOptionsList(database.TableModels).ToList();
        if (missing.Count != 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Could not find the specified tables or views: {missing.ToJoinedString(", ")}");
        if (database.TableModels.Count == 0 && request.Settings.Purpose == MetadataReadPurpose.Import)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"No tables or views found in database '{database.DbName}'. Please check the connection string and database name.");

        var keys = await context.ReadAsync(Query("""
            SELECT TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_SCHEMA, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME, CONSTRAINT_NAME, ORDINAL_POSITION
            FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = @schema AND REFERENCED_TABLE_NAME IS NOT NULL
            ORDER BY TABLE_NAME, CONSTRAINT_NAME, ORDINAL_POSITION
            """), row => new NativeKey(Text(row, "TABLE_NAME"), Text(row, "COLUMN_NAME"), Text(row, "REFERENCED_TABLE_SCHEMA"), Text(row, "REFERENCED_TABLE_NAME"),
                Text(row, "REFERENCED_COLUMN_NAME"), Text(row, "CONSTRAINT_NAME"), Number(row, "ORDINAL_POSITION"))).ConfigureAwait(false);
        var indexes = await context.ReadAsync(Query($"""
            SELECT TABLE_NAME, INDEX_NAME, COLUMN_NAME, SEQ_IN_INDEX, INDEX_TYPE, NON_UNIQUE, SUB_PART, COLLATION,
                {(databaseType == DatabaseType.MySQL ? "EXPRESSION, IS_VISIBLE" : "NULL AS EXPRESSION, IF(IGNORED='YES', 'NO', 'YES') AS IS_VISIBLE")}
            FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = @schema AND INDEX_NAME <> 'PRIMARY'
            ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX
            """), row => new NativeIndex(Text(row, "TABLE_NAME"), Text(row, "INDEX_NAME"), Text(row, "COLUMN_NAME"),
                Number(row, "SEQ_IN_INDEX"), Text(row, "INDEX_TYPE")!, Number(row, "NON_UNIQUE"),
                ULong(row, "SUB_PART"), Text(row, "COLLATION"), Text(row, "EXPRESSION"), Text(row, "IS_VISIBLE"))).ConfigureAwait(false);
        keys = CanonicalKeys(database, keys, identifiers);
        indexes = CanonicalIndexes(database, indexes, identifiers);
        ParseNativeIndexes(database, indexes, keys, failures, identifiers);
        if (failures.Count != 0) return Combine(failures);

        var rules = await context.ReadAsync(Query("""
            SELECT TABLE_NAME, CONSTRAINT_NAME, UPDATE_RULE, DELETE_RULE
            FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = @schema
            """), row => (Table: Text(row, "TABLE_NAME")!, Constraint: Text(row, "CONSTRAINT_NAME")!,
                Update: ParseReferentialAction(Text(row, "UPDATE_RULE")), Delete: ParseReferentialAction(Text(row, "DELETE_RULE")))).ConfigureAwait(false);
        var actions = rules.ToDictionary(row => ((string?)row.Table, (string?)row.Constraint), row => (row.Update, row.Delete),
            identifiers is null ? null : new CatalogObjectComparer(identifiers, identifiers.ConstraintNames));
        ValidateRuntimeKeyCompleteness(database, keys, identifiers, failures);
        if (failures.Count != 0) return Combine(failures);
        foreach (var key in keys)
        {
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(key.Table) && !IsTableOrViewImported(database, key.Table)) continue;
            if (!ParseForeignKeyReference(database.DbName, key.Table, key.Column, key.ReferencedTable, key.ReferencedColumn, key.Constraint)
                .TryUnwrap(out var reference, out var failure)) { failures.Add(failure); continue; }
            if (ReportExternalReference(database, key, identifiers)) continue;
            if (!actions.TryGetValue((reference.TableName, reference.ConstraintName), out var action) && identifiers is not null)
            {
                failures.Add(DLOptionFailure.Fail(DLFailureType.InvalidModel,
                    $"Foreign key '{reference.ConstraintName}' on table '{reference.TableName}' has no referential action catalog row."));
                continue;
            }
            var column = database.TableModels.SingleOrDefault(x => x.Table.DbName == reference.TableName)?
                .Table.Columns.SingleOrDefault(x => x.Column.DbName == reference.ColumnName);
            if (column is null)
            {
                if (limitations is not null)
                    ReportForeignKeyLimitation(key, keys, identifiers, SchemaReadLimitationReason.UnrepresentedForeignKeySource,
                        $"Warning: Skipping foreign key '{reference.ConstraintName}' on table '{reference.TableName}' because source column '{reference.ColumnName}' was not imported.", action.Update, action.Delete);
                continue;
            }
            var target = database.TableModels.SingleOrDefault(x => x.Table.DbName == reference.ReferencedTableName)?
                .Table.Columns.SingleOrDefault(x => x.Column.DbName == reference.ReferencedColumnName);
            if (target is null)
            {
                ReportForeignKeyLimitation(key, keys, identifiers, SchemaReadLimitationReason.UnrepresentedForeignKeyTarget,
                    $"Warning: Skipping foreign key '{reference.ConstraintName}' on table '{reference.TableName}' because referenced column '{reference.ReferencedTableName}.{reference.ReferencedColumnName}' was not imported.", action.Update, action.Delete);
                continue;
            }
            column.Column.ForeignKey = true;
            column.Attributes.Add(new ForeignKeyAttribute(reference.ReferencedTableName, reference.ReferencedColumnName,
                reference.ConstraintName, key.Ordinal, action.Update, action.Delete));
        }
        if (failures.Count != 0) return Combine(failures);

        var checks = await context.ReadAsync(Query(NativeCheckQuery(identifiers is not null)),
            row => (Table: Text(row, "TABLE_NAME"), Constraint: Text(row, "CONSTRAINT_NAME")!, Clause: Text(row, "CHECK_CLAUSE")!)).ConfigureAwait(false);
        foreach (var check in checks)
            database.TableModels.SingleOrDefault(x => identifiers is null ? x.Table.DbName == check.Table : identifiers.TableNames.Equals(x.Table.DbName, check.Table))?.ModelAttributes
                .Add(new CheckAttribute(databaseType, check.Constraint, NormalizeCheckClause(check.Clause)));
        token.ThrowIfCancellationRequested();
        return new MetadataDefinitionFactory().BuildProviderMetadata(database.ToMetadataDraft());
    }

    private void ParseNativeIndexes(ProviderDatabaseDraft database, IReadOnlyList<NativeIndex> indexes,
        IReadOnlyList<NativeKey> keys, List<IDLOptionFailure> failures, SchemaIdentifierRules? identifiers)
    {
        var indexNames = identifiers?.IndexNames ?? StringComparer.Ordinal;
        foreach (var group in indexes.Where(index => !keys.Any(key => key.Table == index.Table && key.Column == index.Column && indexNames.Equals(key.Constraint, index.Name)))
            .GroupBy(index => (index.Table, index.Name), identifiers is null ? null : new CatalogObjectComparer(identifiers, identifiers.IndexNames)))
        {
            var index = group.First();
            if (identifiers is not null)
                SchemaIdentifierRules.RequireUnambiguous(group.Select(item => item.Name!).Distinct(StringComparer.Ordinal), identifiers.IndexNames, $"index in '{index.Table}'", model: false);
            if (string.IsNullOrWhiteSpace(index.Table) || string.IsNullOrWhiteSpace(index.Name))
            {
                failures.Add(DLOptionFailure.Fail(DLFailureType.InvalidModel, $"{databaseType} index metadata is missing a table or index name in database '{database.DbName}'."));
                continue;
            }
            if (!IsTableOrViewImported(database, index.Table)) continue;
            var columns = group.OrderBy(item => item.Ordinal).ToArray();
            var unsupported = databaseType == DatabaseType.MySQL && columns.Any(item => string.IsNullOrWhiteSpace(item.Column) || !string.IsNullOrWhiteSpace(item.Expression)) ? "expression"
                : columns.Any(item => item.Prefix.HasValue) ? "prefix-length"
                : columns.Any(item => string.Equals(item.Collation, "D", StringComparison.OrdinalIgnoreCase)) ? "descending"
                : columns.Any(item => string.Equals(item.Visible, "NO", StringComparison.OrdinalIgnoreCase)) ? databaseType == DatabaseType.MySQL ? "invisible" : "ignored" : null;
            if (unsupported is not null)
            {
                var reason = unsupported switch
                {
                    "expression" => SchemaReadLimitationReason.ExpressionIndex,
                    "prefix-length" => SchemaReadLimitationReason.PrefixLengthIndex,
                    "descending" => SchemaReadLimitationReason.DescendingIndex,
                    "invisible" => SchemaReadLimitationReason.InvisibleIndex,
                    "ignored" => SchemaReadLimitationReason.IgnoredIndex,
                    _ => throw new InvalidOperationException("Unknown detected index limitation.")
                };
                ReportLimitation(index.Table, index.Name, SchemaReadAspect.Index, reason,
                    $"Warning: Skipping unsupported {databaseType} {unsupported} index '{index.Name}' on table '{index.Table}'.");
                continue;
            }
            if (!ParseIndexType(index.Type, index.Table, index.Name).TryUnwrap(out var type, out var failure)) { failures.Add(failure); continue; }
            var properties = new List<ProviderValuePropertyDraft>();
            foreach (var item in columns)
            {
                var property = database.TableModels.SingleOrDefault(x => x.Table.DbName == item.Table)?.Table.Columns.SingleOrDefault(x => x.Column.DbName == item.Column);
                if (property is null)
                {
                    ReportLimitation(index.Table, index.Name, SchemaReadAspect.Index, SchemaReadLimitationReason.UnrepresentedIndexColumn,
                        $"Warning: Skipping {databaseType} index '{index.Name}' on table '{index.Table}' because column '{item.Column}' was not imported.");
                    properties.Clear();
                    break;
                }
                properties.Add(property);
            }
            if (properties.Count == 0) continue;
            var names = properties.Select(property => property.Column.DbName).ToArray();
            foreach (var property in properties)
                property.Attributes.Add(new IndexAttribute(index.Name, index.NonUnique == 0 ? IndexCharacteristic.Unique : IndexCharacteristic.Simple, type, names));
        }
    }

    private static async Task<string> ReadViewAsync(MetadataReadContext context, string database, string view, bool runtime)
    {
        const string viewQuery = "SELECT VIEW_DEFINITION FROM information_schema.VIEWS WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @view";
        var definitions = await context.ReadAsync(new Sql(runtime ? RuntimeCatalogQuery(viewQuery) : viewQuery)
            .AddParameter("@schema", database).AddParameter("@view", view), row => Text(row, "VIEW_DEFINITION")).ConfigureAwait(false);
        var definition = definitions.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(definition))
        {
            // Unlike the legacy CatchAll fallback, actual command failures remain
            // operational failures. A canceled read cannot publish partial metadata.
            var create = await context.ReadAsync(new Sql($"SHOW CREATE VIEW {SqlIdentifier.Quote(database, "`")}.{SqlIdentifier.Quote(view, "`")}"),
                row => Text(row, "Create View")).ConfigureAwait(false);
            definition = create.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(definition)) throw new InvalidOperationException($"No definition was returned for view '{view}'.");
            var markers = new[] { $"VIEW {SqlIdentifier.Quote(database, "`")}.{SqlIdentifier.Quote(view, "`")} AS ", $"VIEW {SqlIdentifier.Quote(view, "`")} AS ", " AS " };
            foreach (var marker in markers)
            {
                var position = definition.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (position < 0) continue;
                definition = definition[(position + marker.Length)..];
                break;
            }
        }
        return definition.Replace($"{SqlIdentifier.Quote(database, "`")}.", "").Replace($"{database}.", "").Trim();
    }

    private static IDLOptionFailure Combine(IReadOnlyCollection<IDLOptionFailure> failures) => failures.Count == 1 ? failures.Single() : DLOptionFailure.AggregateFail(failures);
    private static string? Text(IDataLinqDataReader row, string name) => row.IsDbNull(row.GetOrdinal(name)) ? null : row.GetString(row.GetOrdinal(name));
    private static ulong? ULong(IDataLinqDataReader row, string name) => row.IsDbNull(row.GetOrdinal(name)) ? null : Convert.ToUInt64(row.GetValue(row.GetOrdinal(name)), CultureInfo.InvariantCulture);
    private static int Number(IDataLinqDataReader row, string name) => Convert.ToInt32(row.GetValue(row.GetOrdinal(name)), CultureInfo.InvariantCulture);
    private sealed record NativeTable(string Name, string? Type, string? Comment);
    private sealed record NativeKey(string? Table, string? Column, string? ReferencedSchema, string? ReferencedTable, string? ReferencedColumn, string? Constraint, int Ordinal);
    private sealed record NativeIndex(string? Table, string? Name, string? Column, int Ordinal, string Type, int NonUnique, ulong? Prefix, string? Collation, string? Expression, string? Visible);

    private sealed record NativeColumn(string? TABLE_SCHEMA, string? TABLE_NAME, string? DATA_TYPE, string COLUMN_TYPE,
        ulong? NUMERIC_PRECISION, ulong? NUMERIC_SCALE, ulong? CHARACTER_MAXIMUM_LENGTH, string IS_NULLABLE, COLUMN_KEY COLUMN_KEY,
        string? EXTRA, string? GENERATION_EXPRESSION, string? COLUMN_DEFAULT, string? COLUMN_NAME, string COLUMN_COMMENT, string? IS_GENERATED) : ICOLUMNS
    {
        internal static NativeColumn Read(IDataLinqDataReader row) => new(Text(row, "TABLE_SCHEMA"), Text(row, "TABLE_NAME"), Text(row, "DATA_TYPE"), Text(row, "COLUMN_TYPE")!,
            ULong(row, "NUMERIC_PRECISION"), ULong(row, "NUMERIC_SCALE"), ULong(row, "CHARACTER_MAXIMUM_LENGTH"), Text(row, "IS_NULLABLE")!,
            Text(row, "COLUMN_KEY") switch { "PRI" => COLUMN_KEY.PRI, "UNI" => COLUMN_KEY.UNI, "MUL" => COLUMN_KEY.MUL, _ => COLUMN_KEY.Empty },
            Text(row, "EXTRA"), Text(row, "GENERATION_EXPRESSION"), Text(row, "COLUMN_DEFAULT"), Text(row, "COLUMN_NAME"), Text(row, "COLUMN_COMMENT")!, Text(row, "IS_GENERATED"));
    }
}
