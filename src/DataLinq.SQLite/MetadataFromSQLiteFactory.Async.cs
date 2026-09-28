using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Execution;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.Query;
using Microsoft.Data.Sqlite;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.SQLite;

public partial class MetadataFromSQLiteFactory : IAsyncMetadataFactory
{
    MetadataFromDatabaseFactoryOptions IAsyncMetadataFactory.Options => options;

    IAsyncMetadataReadPlan IAsyncMetadataFactory.CaptureImport(MetadataImportRequest request)
    {
        if (GetType() != typeof(MetadataFromSQLiteFactory))
            throw new NotSupportedException("This derived metadata factory does not support captured asynchronous reading.");
        var connectionString = ReadOnlyConnectionString(SQLiteConnectionStringFactory.NormalizeConnectionString(
            request.ConnectionString, request.DatabaseName));
        return CaptureNativeRead(request, () => SQLiteAdministrativeSession.CreateOwned(connectionString,
            CapturedSql.Capture(new Sql("SELECT 1")), ExecutionOperationKind.MetadataRead, applyVisibility: false));
    }

    internal static string ReadOnlyConnectionString(string effectiveConnectionString)
    {
        var options = new SqliteConnectionStringBuilder(effectiveConnectionString);
        if (!SQLiteConnectionStringFactory.IsInMemory(options)) options.Mode = SqliteOpenMode.ReadOnly;
        return options.ConnectionString;
    }

    internal static IAsyncMetadataReadPlan CaptureNativeRead(MetadataImportRequest request,
        Func<IAsyncMetadataSession> createSession, Action? validateLifecycle = null)
    {
        var parser = new MetadataFromSQLiteFactory(new()
        {
            CapitaliseNames = request.Settings.CapitaliseNames,
            DeclareEnumsInClass = request.Settings.DeclareEnumsInClass,
            Include = request.Settings.Include.ToList(),
            Log = request.Settings.Log
        });
        return new NativeMetadataPlan(parser, request, createSession, validateLifecycle);
    }

    private sealed class NativeMetadataPlan(MetadataFromSQLiteFactory parser, MetadataImportRequest request,
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
        // Session opening never creates a file or acquires an owning keeper.
        // Even connection visibility setup goes through the context so an
        // explicit metadata timeout reaches every actual native command.
        await context.ExecuteScalarAsync(new Sql(SQLiteConnectionPolicy.CommittedVisibilitySql)).ConfigureAwait(false);
        var database = new SQLiteProviderDatabaseDraft(request.Name,
            new CsTypeDeclaration(request.CsTypeName, request.CsNamespace, ModelCsType.Class), request.DatabaseName);
        var catalog = await context.ReadAsync(new Sql("SELECT type, tbl_name, sql FROM sqlite_master WHERE type <> 'index' AND tbl_name <> 'sqlite_sequence'"),
            row => (Type: row.GetString(0), Name: row.GetString(1), Definition: row.IsDbNull(2) ? null : row.GetString(2))).ConfigureAwait(false);
        var failures = new List<IDLOptionFailure>();
        foreach (var item in catalog.Where(row => IsTableOrViewNameInOptionsList(row.Name)))
        {
            token.ThrowIfCancellationRequested();
            var type = item.Type == "table" ? TableType.Table : TableType.View;
            var table = new SQLiteProviderTableDraft(item.Name, type);
            if (type == TableType.View) table.Definition = ParseViewDefinition(item.Definition!);
            var name = table.DbName.ToCSharpIdentifier(options.CapitaliseNames);
            var columns = await context.ReadAsync(new Sql($"SELECT * FROM pragma_table_info({QuoteSqlLiteral(table.DbName)})"),
                row => ParseColumn(table, row, type == TableType.Table ? item.Definition : null)).ConfigureAwait(false);
            foreach (var column in columns)
            {
                token.ThrowIfCancellationRequested();
                if (column.TryUnwrap(out var imported, out var failure)) table.Columns.Add(imported);
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
            var indexes = await context.ReadAsync(new Sql(IndexListSql(table.DbName)), CatalogIndex.Read).ConfigureAwait(false);
            foreach (var index in indexes)
            {
                token.ThrowIfCancellationRequested();
                if (!ShouldReadIndexColumns(table, index)) continue;
                var columns = await context.ReadAsync(new Sql(IndexColumnsSql(index.Name)), CatalogIndexColumn.Read).ConfigureAwait(false);
                ParseIndexColumns(table, index, columns);
            }
        }
        foreach (var table in database.TableModels.Select(x => x.Table).Where(x => x.Type == TableType.Table))
        {
            var relations = await context.ReadAsync(new Sql(RelationsSql(table.DbName)), CatalogRelation.Read).ConfigureAwait(false);
            foreach (var relation in relations)
            {
                token.ThrowIfCancellationRequested();
                ParseRelation(database, table, relation, failures);
            }
        }
        token.ThrowIfCancellationRequested();
        if (failures.Count != 0) return SingleOrAggregate(failures);
        // Publication happens only after complete reads and the coordinator's
        // final cancellation/cleanup checks. This is not an atomic DDL snapshot.
        return new MetadataDefinitionFactory().BuildProviderMetadata(database.ToMetadataDraft());
    }
}
