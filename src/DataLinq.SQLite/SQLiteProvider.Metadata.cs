using System;
using DataLinq.Execution;

namespace DataLinq.SQLite;

public partial class SQLiteProvider<T> : ISyncProviderMetadataSource
{
    ISyncMetadataReadPlan ISyncProviderMetadataSource.CaptureValidationMetadata(MetadataReadSettings settings)
    {
        RootDisposal.EnsureUsable();
        var request = new MetadataImportRequest(Metadata.Name, Metadata.CsType.Name, Metadata.CsType.Namespace,
            DatabaseName ?? throw new InvalidOperationException("DatabaseName not defined."), ConnectionString, settings);
        var connectionString = MetadataFromSQLiteFactory.ReadOnlyConnectionString(ConnectionString);
        return MetadataFromSQLiteFactory.CaptureNativeSyncRead(request,
            () => new SQLiteMetadataSession(connectionString, dbAccess), RootDisposal.EnsureUsable);
    }
}
