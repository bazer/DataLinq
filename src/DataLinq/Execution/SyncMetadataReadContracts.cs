using System;
using System.Data;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway;

namespace DataLinq.Execution;

// Private observational capability; legacy import factories are not a fallback.
internal interface ISyncProviderMetadataSource
{
    ISyncMetadataReadPlan CaptureValidationMetadata(MetadataReadSettings settings);
}

internal interface ISyncMetadataReadPlan
{
    void Validate();
    // Successful construction transfers unopened resources. A failed factory
    // must settle any partially constructed resources it cannot hand off.
    ISyncMetadataSession CreateSession();
    Option<DatabaseDefinition, IDLOptionFailure> Read(SyncMetadataReadContext context);
}

internal interface ISyncMetadataSession : IDisposable
{
    // I/O-free capability validation, before opening the owned connection.
    void Validate(int? commandTimeoutSeconds);
    void Open();
    // Commands belong to the context; the session owns only its connection.
    IDbCommand CreateCommand(CapturedSql sql);
    IDataLinqDataReader ExecuteReader(IDbCommand command);
    object? ExecuteScalar(IDbCommand command);
}
