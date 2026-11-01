using System;
using DataLinq.Execution;

namespace DataLinq.MySql;

public abstract partial class SqlProvider<T> : ISyncProviderMetadataSource
{
    ISyncMetadataReadPlan ISyncProviderMetadataSource.CaptureValidationMetadata(MetadataReadSettings settings)
    {
        RootDisposal.EnsureUsable();
        var request = new MetadataImportRequest(Metadata.Name, Metadata.CsType.Name, Metadata.CsType.Namespace,
            NormalizeMetadataSchemaName(DatabaseName) ?? throw new InvalidOperationException("DatabaseName not defined."),
            dataSource.ConnectionString, settings);
        return MetadataFromSqlFactory.CaptureNativeSyncRead(DatabaseType, request,
            () => new SqlMetadataSession(dataSource.CreateConnection(), dbAccess), RootDisposal.EnsureUsable);
    }
}
