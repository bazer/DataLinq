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
using DataLinq.Validation;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.SQLite;

public partial class MetadataFromSQLiteFactory
{
    internal static ISyncMetadataReadPlan CaptureNativeSyncRead(MetadataImportRequest request,
        Func<ISyncMetadataSession> createSession, Action validateLifecycle)
    {
        var parser = new MetadataFromSQLiteFactory(new()
        {
            CapitaliseNames = request.Settings.CapitaliseNames,
            DeclareEnumsInClass = request.Settings.DeclareEnumsInClass,
            Include = request.Settings.Include.ToList(),
            Log = request.Settings.Log
        });
        parser.limitations = request.Settings.Limitations;
        return new NativeSyncMetadataPlan(parser, request, createSession, validateLifecycle);
    }

    private sealed class NativeSyncMetadataPlan(MetadataFromSQLiteFactory parser, MetadataImportRequest request,
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
        if (request.Settings.Purpose == MetadataReadPurpose.RuntimeValidation)
            request.Settings.SetIdentifiers(SchemaIdentifierRules.SQLite);
        // Session opening never creates a file or acquires an owning keeper.
        // Even connection visibility setup goes through the context so an
        // explicit metadata timeout reaches every actual native command.
        context.ExecuteScalar(new Sql(SQLiteConnectionPolicy.CommittedVisibilitySql));
        var database = new SQLiteProviderDatabaseDraft(request.Name,
            new CsTypeDeclaration(request.CsTypeName, request.CsNamespace, ModelCsType.Class), request.DatabaseName);
        var catalog = context.Read(new Sql(limitations is null
            ? "SELECT type, tbl_name, sql FROM sqlite_master WHERE type <> 'index' AND tbl_name <> 'sqlite_sequence'"
            : RuntimeCatalogSql),
            row => (Type: row.GetString(0), Name: row.GetString(1), Definition: row.IsDbNull(2) ? null : row.GetString(2)));
        var failures = new List<IDLOptionFailure>();
        foreach (var item in catalog.Where(row => IsTableOrViewNameInOptionsList(row.Name)))
        {
            var type = item.Type == "table" ? TableType.Table : TableType.View;
            var table = new SQLiteProviderTableDraft(item.Name, type);
            if (type == TableType.View) table.Definition = ParseViewDefinition(item.Definition!);
            var name = table.DbName.ToCSharpIdentifier(options.CapitaliseNames);
            var columns = context.Read(new Sql($"SELECT * FROM {(limitations is null ? "pragma_table_info" : "pragma_table_xinfo")}({QuoteSqlLiteral(table.DbName)})"),
                row => ReadNativeColumn(table, row, type == TableType.Table ? item.Definition : null));
            foreach (var column in columns)
            {
                if (column.Omitted) continue;
                if (column.Column.TryUnwrap(out var imported, out var failure)) table.Columns.Add(imported);
                else failures.Add(failure);
            }
            database.TableModels.Add(new(name, new CsTypeDeclaration(name, database.CsType.Namespace, ModelCsType.Class), table));
        }
        if (failures.Count != 0) return SingleOrAggregate(failures);
        var missing = FindMissingTablesOrViewInOptionsList(database.TableModels).ToList();
        if (missing.Count != 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Could not find the specified tables or views: {missing.ToJoinedString(", ")}");
        if (database.TableModels.Count == 0 && request.Settings.Purpose == MetadataReadPurpose.Import)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"No tables or views found in database '{database.DbName}'. Please check the connection string and database name.");

        foreach (var table in database.TableModels.Select(x => x.Table).Where(x => x.Type == TableType.Table))
        {
            var indexes = context.Read(new Sql(IndexListSql(table.DbName)), CatalogIndex.Read);
            foreach (var index in indexes)
            {
                if (!ShouldReadIndexColumns(table, index)) continue;
                var columns = context.Read(new Sql(IndexColumnsSql(index.Name)), CatalogIndexColumn.Read);
                ParseIndexColumns(table, index, columns);
            }
        }
        foreach (var table in database.TableModels.Select(x => x.Table).Where(x => x.Type == TableType.Table))
        {
            var relations = context.Read(new Sql(RelationsSql(table.DbName)), CatalogRelation.Read);
            ValidateNativeRelationCompleteness(database, table, relations, failures);
            if (failures.Count != 0) return SingleOrAggregate(failures);
            foreach (var relation in relations)
            {
                ParseRelation(database, table, relation, failures,
                    limitations is null ? null : relations.Where(part => part.Name == relation.Name).ToArray());
            }
        }
        if (failures.Count != 0) return SingleOrAggregate(failures);
        // Publication happens only after complete reads and the coordinator's
        // final cleanup checks. This is not an atomic DDL snapshot.
        return new MetadataDefinitionFactory().BuildProviderMetadata(database.ToMetadataDraft());
    }
}
