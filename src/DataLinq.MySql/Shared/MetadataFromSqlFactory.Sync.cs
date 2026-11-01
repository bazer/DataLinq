using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Execution;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.Query;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.MySql;

public abstract partial class MetadataFromSqlFactory
{
    internal static ISyncMetadataReadPlan CaptureNativeSyncRead(DatabaseType type, MetadataImportRequest request,
        Func<ISyncMetadataSession> createSession, Action validateLifecycle)
    {
        var parser = GetSqlFactory(new()
        {
            CapitaliseNames = request.Settings.CapitaliseNames,
            DeclareEnumsInClass = request.Settings.DeclareEnumsInClass,
            Include = request.Settings.Include.ToList(),
            Log = request.Settings.Log
        }, type);
        return new NativeSyncMetadataPlan(parser, request, createSession, validateLifecycle);
    }

    private sealed class NativeSyncMetadataPlan(MetadataFromSqlFactory parser, MetadataImportRequest request,
        Func<ISyncMetadataSession> createSession, Action validateLifecycle) : ISyncMetadataReadPlan
    {
        public void Validate() => validateLifecycle();
        public ISyncMetadataSession CreateSession() => createSession();
        public Option<DatabaseDefinition, IDLOptionFailure> Read(SyncMetadataReadContext context) =>
            parser.ReadNativeMetadata(context, request);
    }

    private Option<DatabaseDefinition, IDLOptionFailure> ReadNativeMetadata(
        SyncMetadataReadContext context, MetadataImportRequest request)
    {
        var database = new ProviderDatabaseDraft(request.Name,
            new CsTypeDeclaration(request.CsTypeName, request.CsNamespace, ModelCsType.Class), request.DatabaseName);
        Sql Query(string text) => new Sql(text).AddParameter("@schema", request.DatabaseName);

        // An empty result from information_schema alone cannot distinguish an
        // existing empty schema from a missing/inaccessible schema.
        var exists = context.ExecuteScalar(Query("SELECT 1 FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @schema LIMIT 1"));
        if (exists is null || exists == DBNull.Value)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Database '{request.DatabaseName}' does not exist or is not visible to this connection.");

        var tables = context.Read(Query("SELECT TABLE_NAME, TABLE_TYPE, TABLE_COMMENT FROM information_schema.TABLES WHERE TABLE_SCHEMA = @schema ORDER BY TABLE_NAME"),
            row => new NativeTable(Text(row, "TABLE_NAME")!, Text(row, "TABLE_TYPE"), Text(row, "TABLE_COMMENT")));
        var failures = new List<IDLOptionFailure>();
        foreach (var item in tables.Where(item => IsTableOrViewNameInOptionsList(item.Name)))
        {
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
                table.Definition = ReadView(context, database.DbName, table.DbName);
            var columns = context.Read(Query($"""
                SELECT TABLE_SCHEMA, TABLE_NAME, DATA_TYPE, COLUMN_TYPE, NUMERIC_PRECISION, NUMERIC_SCALE,
                    CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, COLUMN_KEY, EXTRA, GENERATION_EXPRESSION,
                    COLUMN_DEFAULT, COLUMN_NAME, COLUMN_COMMENT,
                    {(databaseType == DatabaseType.MariaDB ? "IS_GENERATED" : "'NEVER' AS IS_GENERATED")}
                FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                ORDER BY ORDINAL_POSITION
                """).AddParameter("@table", table.DbName), NativeColumn.Read);
            foreach (var column in columns)
            {
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

        var keys = context.Read(Query("""
            SELECT TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME, CONSTRAINT_NAME, ORDINAL_POSITION
            FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = @schema AND REFERENCED_TABLE_NAME IS NOT NULL
            ORDER BY TABLE_NAME, CONSTRAINT_NAME, ORDINAL_POSITION
            """), row => new NativeKey(Text(row, "TABLE_NAME"), Text(row, "COLUMN_NAME"), Text(row, "REFERENCED_TABLE_NAME"),
                Text(row, "REFERENCED_COLUMN_NAME"), Text(row, "CONSTRAINT_NAME"), Number(row, "ORDINAL_POSITION")));
        var indexes = context.Read(Query($"""
            SELECT TABLE_NAME, INDEX_NAME, COLUMN_NAME, SEQ_IN_INDEX, INDEX_TYPE, NON_UNIQUE, SUB_PART, COLLATION,
                {(databaseType == DatabaseType.MySQL ? "EXPRESSION, IS_VISIBLE" : "NULL AS EXPRESSION, IF(IGNORED='YES', 'NO', 'YES') AS IS_VISIBLE")}
            FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = @schema AND INDEX_NAME <> 'PRIMARY'
            ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX
            """), row => new NativeIndex(Text(row, "TABLE_NAME"), Text(row, "INDEX_NAME"), Text(row, "COLUMN_NAME"),
                Number(row, "SEQ_IN_INDEX"), Text(row, "INDEX_TYPE")!, Number(row, "NON_UNIQUE"),
                ULong(row, "SUB_PART"), Text(row, "COLLATION"), Text(row, "EXPRESSION"), Text(row, "IS_VISIBLE")));
        ParseNativeIndexes(database, indexes, keys, failures);
        if (failures.Count != 0) return Combine(failures);

        var rules = context.Read(Query("""
            SELECT TABLE_NAME, CONSTRAINT_NAME, UPDATE_RULE, DELETE_RULE
            FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = @schema
            """), row => (Table: Text(row, "TABLE_NAME")!, Constraint: Text(row, "CONSTRAINT_NAME")!,
                Update: ParseReferentialAction(Text(row, "UPDATE_RULE")), Delete: ParseReferentialAction(Text(row, "DELETE_RULE"))));
        var actions = rules.ToDictionary(row => (row.Table, row.Constraint), row => (row.Update, row.Delete));
        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key.Table) && !IsTableOrViewImported(database, key.Table)) continue;
            if (!ParseForeignKeyReference(database.DbName, key.Table, key.Column, key.ReferencedTable, key.ReferencedColumn, key.Constraint)
                .TryUnwrap(out var reference, out var failure)) { failures.Add(failure); continue; }
            var column = database.TableModels.SingleOrDefault(x => x.Table.DbName == reference.TableName)?
                .Table.Columns.SingleOrDefault(x => x.Column.DbName == reference.ColumnName);
            if (column is null) continue;
            var target = database.TableModels.SingleOrDefault(x => x.Table.DbName == reference.ReferencedTableName)?
                .Table.Columns.SingleOrDefault(x => x.Column.DbName == reference.ReferencedColumnName);
            if (target is null)
            {
                options.Log?.Invoke($"Warning: Skipping foreign key '{reference.ConstraintName}' on table '{reference.TableName}' because referenced column '{reference.ReferencedTableName}.{reference.ReferencedColumnName}' was not imported.");
                continue;
            }
            actions.TryGetValue((reference.TableName, reference.ConstraintName), out var action);
            column.Column.ForeignKey = true;
            column.Attributes.Add(new ForeignKeyAttribute(reference.ReferencedTableName, reference.ReferencedColumnName,
                reference.ConstraintName, key.Ordinal, action.Update, action.Delete));
        }
        if (failures.Count != 0) return Combine(failures);

        var checks = context.Read(Query("""
            SELECT tc.TABLE_NAME, cc.CONSTRAINT_NAME, cc.CHECK_CLAUSE
            FROM information_schema.TABLE_CONSTRAINTS tc
            JOIN information_schema.CHECK_CONSTRAINTS cc ON cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA AND cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
            WHERE tc.TABLE_SCHEMA = @schema AND tc.CONSTRAINT_TYPE = 'CHECK'
            ORDER BY tc.TABLE_NAME, cc.CONSTRAINT_NAME
            """), row => (Table: Text(row, "TABLE_NAME"), Constraint: Text(row, "CONSTRAINT_NAME")!, Clause: Text(row, "CHECK_CLAUSE")!));
        foreach (var check in checks)
            database.TableModels.SingleOrDefault(x => x.Table.DbName == check.Table)?.ModelAttributes
                .Add(new CheckAttribute(databaseType, check.Constraint, NormalizeCheckClause(check.Clause)));
        return new MetadataDefinitionFactory().BuildProviderMetadata(database.ToMetadataDraft());
    }

    private static string ReadView(SyncMetadataReadContext context, string database, string view)
    {
        var definitions = context.Read(new Sql("SELECT VIEW_DEFINITION FROM information_schema.VIEWS WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @view")
            .AddParameter("@schema", database).AddParameter("@view", view), row => Text(row, "VIEW_DEFINITION"));
        var definition = definitions.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(definition))
        {
            // Unlike the legacy CatchAll fallback, actual command failures remain
            // operational failures. A canceled read cannot publish partial metadata.
            var create = context.Read(new Sql($"SHOW CREATE VIEW {SqlIdentifier.Quote(database, "`")}.{SqlIdentifier.Quote(view, "`")}"),
                row => Text(row, "Create View"));
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

}
