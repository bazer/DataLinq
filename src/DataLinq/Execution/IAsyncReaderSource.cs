using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>
/// Deferred reader acquisition for row-enumerator orchestration. Construction performs
/// no I/O; each explicit open transfers one reader to the caller, which must dispose it.
/// If acquisition fails before transfer, the source must clean up its partial resources.
/// A successfully returned reader owns any source-created resources needed for its lifetime;
/// a borrowed command remains caller-owned.
/// </summary>
internal interface IAsyncReaderSource
{
    // Must not acquire resources, execute user conversion, or perform database I/O.
    void Validate();
    Task<IAsyncDataReader> OpenReaderAsync(CancellationToken cancellationToken);
}

/// <summary>Reader acquisition that requires the caller's existing private transaction step.</summary>
internal interface IAsyncTransactionReaderSource : IAsyncReaderSource
{
    Task<IAsyncDataReader> OpenReaderAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken);
}

/// <summary>
/// Captures the identity of a borrowed command, not a snapshot of its mutable parameters.
/// The command must remain stable through each operation and reader lifetime. No connection,
/// transaction or command ownership is transferred to this source.
/// </summary>
internal sealed class BorrowedCommandReaderSource : IAsyncReaderSource, IAsyncReadFailureEvidence
{
    private readonly IAsyncDatabaseAccess access;
    private readonly IDbCommand command;

    internal BorrowedCommandReaderSource(IAsyncDatabaseAccess access, IDbCommand command)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(command);
        this.access = access;
        this.command = command;
    }

    public void Validate() => access.ValidateReader(command);

    public ReadFailureEvidence GetReadFailureEvidence(Exception failure) =>
        access is IAsyncReadFailureEvidence evidence ? evidence.GetReadFailureEvidence(failure) : new();

    public Task<IAsyncDataReader> OpenReaderAsync(CancellationToken cancellationToken)
        => access.ExecuteReaderAsync(command, cancellationToken);
}
