using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Query;
using MySqlConnector;

namespace DataLinq.MySql;

public abstract partial class SqlProvider<T> : IAsyncExistenceProbeSource, IAsyncRootDisposal, IAsyncProviderMetadataSource
{
    private OwnedRootDisposal? rootDisposal;
    private OwnedRootDisposal RootDisposal => LazyInitializer.EnsureInitialized(ref rootDisposal, () => new(
        () => [RootCleanupStep.Resource(State.Dispose, State.DisposeAsyncCore, State.ValidateDisposal),
            RootCleanupStep.Resource(dataSource.Dispose, dataSource.DisposeAsync)], TelemetryInstanceId));

    ValueTask IAsyncRootDisposal.DisposeAsyncCore() => RootDisposal.DisposeAsync();

    IAsyncMetadataReadPlan IAsyncProviderMetadataSource.CaptureValidationMetadata(MetadataReadSettings settings)
    {
        RootDisposal.EnsureUsable();
        var request = new MetadataImportRequest(Metadata.Name, Metadata.CsType.Name, Metadata.CsType.Namespace,
            NormalizeMetadataSchemaName(DatabaseName) ?? throw new InvalidOperationException("DatabaseName not defined."),
            dataSource.ConnectionString, settings);
        return MetadataFromSqlFactory.CaptureNativeRead(DatabaseType, request,
            () => new SqlAdministrativeSession(dataSource.CreateConnection(), dbAccess,
                CapturedSql.Capture(new Sql("SELECT 1")), ExecutionOperationKind.MetadataRead), RootDisposal.EnsureUsable);
    }

    ExistenceProbePlan IAsyncExistenceProbeSource.CaptureExistenceProbe(ExistenceProbeRequest request)
    {
        RootDisposal.EnsureUsable();
        var sql = new Sql("SELECT 1");
        if (request.Kind is ExistenceProbeKind.Database or ExistenceProbeKind.Table)
        {
            var schema = NormalizeMetadataSchemaName(request.DatabaseName ?? DatabaseName)
                ?? throw new ArgumentNullException(nameof(request.DatabaseName));
            sql = request.Kind == ExistenceProbeKind.Database
                ? new Sql("SELECT 1 FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @databaseName LIMIT 1")
                : new Sql("SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA = @databaseName AND TABLE_NAME = @tableName LIMIT 1");
            sql.AddParameter("@databaseName", schema);
            if (request.Kind == ExistenceProbeKind.Table)
            {
                if (string.IsNullOrEmpty(request.TableName)) throw new ArgumentNullException(nameof(request.TableName));
                sql.AddParameter("@tableName", schema == "information_schema" ? request.TableName.ToUpperInvariant() : request.TableName);
            }
        }
        else if (request.Kind != ExistenceProbeKind.FileOrServer)
            throw new ArgumentOutOfRangeException(nameof(request));

        var captured = CapturedSql.Capture(sql);
        return ExistenceProbePlan.Scalar(RootDisposal.EnsureUsable,
            () => new SqlAdministrativeSession(dataSource.CreateConnection(), dbAccess, captured, ExecutionOperationKind.ExistenceCheck),
            static value => value is not null && value != DBNull.Value,
            // The coordinator permits this mapping only for availability after clean
            // settlement, and rejects cancellation, callback and cleanup failures.
            static failure => failure is MySqlException);
    }
}
