using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>
/// Deferred reader acquisition for later row-enumerator orchestration. Construction performs
/// no I/O; each explicit open transfers one reader to the caller, which must dispose it.
/// </summary>
internal interface IAsyncReaderSource
{
    Task<IAsyncDataReader> OpenReaderAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Captures the identity of a borrowed command, not a snapshot of its mutable parameters.
/// The command must remain stable through each operation and reader lifetime. No connection,
/// transaction or command ownership is transferred to this source.
/// </summary>
internal sealed class BorrowedCommandReaderSource : IAsyncReaderSource
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

    public Task<IAsyncDataReader> OpenReaderAsync(CancellationToken cancellationToken)
        => access.ExecuteReaderAsync(command, cancellationToken);
}
