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
internal sealed class BorrowedCommandReaderSource : IAsyncReaderSource, IAsyncReadFailureEvidence, IAsyncCommandDispatchEvidence
{
    private readonly IAsyncDatabaseAccess access;
    private readonly IDbCommand command;
    public bool CommandDispatched { get; private set; }

    internal BorrowedCommandReaderSource(IAsyncDatabaseAccess access, IDbCommand command)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(command);
        this.access = access;
        this.command = command;
    }

    public void Validate() => access.ValidateReader(command);

    public ReadFailureEvidence GetReadFailureEvidence(Exception failure)
    {
        var evidence = access is IAsyncReadFailureEvidence classifier ? classifier.GetReadFailureEvidence(failure) : new();
        return CommandDispatched ? evidence : evidence with { Effects = ExecutionEffects.NoStatement, Integrity = TransactionIntegrity.Confirmed };
    }

    public Task<IAsyncDataReader> OpenReaderAsync(CancellationToken cancellationToken)
    {
        CommandDispatched = false;
        Validate();
        cancellationToken.ThrowIfCancellationRequested();
        return OpenReaderCoreAsync(cancellationToken);
    }

    private async Task<IAsyncDataReader> OpenReaderCoreAsync(CancellationToken cancellationToken)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        CommandDispatched = true;
        try { return await access.ExecuteReaderAsync(command, cancellationToken).ConfigureAwait(false); }
        catch (Exception failure)
        {
            if (CommandDispatchEvidence.ProvesNoDispatch(failure, command)) CommandDispatched = false;
            throw;
        }
    }
}
