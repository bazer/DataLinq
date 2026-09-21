using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>
/// Captured lower-level invocation. The adapter is an explicit native capability, not a
/// recursive call to the managed entry point. Only owned commands are created or cleaned up.
/// </summary>
internal sealed class AsyncEagerCommand : IAsyncReadFailureEvidence
{
    private readonly IAsyncDatabaseAccess access;
    private readonly OwnedCommandExecution? owned;
    private readonly IDbCommand? borrowed;
    private readonly IAsyncCommandInitialization? initialization;
    private int started;
    private bool borrowedDispatched;

    internal AsyncEagerCommand(IAsyncDatabaseAccess access, IAsyncOwnedCommandFactory factory,
        IAsyncCommandInitialization? initialization = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(factory);
        this.access = access;
        owned = new(access, factory);
        this.initialization = initialization;
    }

    internal AsyncEagerCommand(IAsyncDatabaseAccess access, IDbCommand command,
        IAsyncCommandInitialization? initialization = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(command);
        this.access = access;
        borrowed = command;
        this.initialization = initialization;
    }

    internal bool Dispatched => owned?.Dispatched ?? borrowedDispatched;

    internal void Validate(AsyncCommandKind kind, bool hasOwner)
    {
        if (kind is not (AsyncCommandKind.Scalar or AsyncCommandKind.NonQuery))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (initialization is not null && !hasOwner)
            throw new InvalidOperationException("Transaction initialization requires a private execution owner.");
        initialization?.Validate();
        if (owned is not null) owned.Validate(kind);
        else access.ValidateCommand(borrowed!, kind);
        if (Volatile.Read(ref started) != 0)
            throw new InvalidOperationException("This captured command invocation has already started.");
    }

    internal Task<object?> ExecuteScalarAsync(TransactionOperationGate.Step? owner, CancellationToken token) =>
        ExecuteAsync(AsyncCommandKind.Scalar, owner, token,
            static (owned, token) => owned.ExecuteScalarAsync(token),
            static (access, command, token) => access.ExecuteScalarAsync(command, token));

    internal Task<int> ExecuteNonQueryAsync(TransactionOperationGate.Step? owner, CancellationToken token) =>
        ExecuteAsync(AsyncCommandKind.NonQuery, owner, token,
            static (owned, token) => owned.ExecuteNonQueryAsync(token),
            static (access, command, token) => access.ExecuteNonQueryAsync(command, token));

    private async Task<T> ExecuteAsync<T>(AsyncCommandKind kind, TransactionOperationGate.Step? owner, CancellationToken token,
        Func<OwnedCommandExecution, CancellationToken, Task<T>> executeOwned,
        Func<IAsyncDatabaseAccess, IDbCommand, CancellationToken, Task<T>> executeBorrowed)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        Validate(kind, owner is not null);
        token.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
            throw new InvalidOperationException("This captured command invocation has already started.");
        if (initialization is not null)
            await initialization.InitializeAsync(owner!, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (owned is not null)
            return await executeOwned(owned, token).ConfigureAwait(false);

        // Revalidate the actual borrowed command after initialization; never replace,
        // mutate or dispose it. Validation and pre-dispatch cancellation preserve work.
        access.ValidateCommand(borrowed!, kind);
        token.ThrowIfCancellationRequested();
        borrowedDispatched = true;
        try { return await executeBorrowed(access, borrowed!, token).ConfigureAwait(false); }
        catch (Exception failure)
        {
            if (CommandDispatchEvidence.ProvesNoDispatch(failure, borrowed!)) borrowedDispatched = false;
            throw;
        }
    }

    public ReadFailureEvidence GetReadFailureEvidence(Exception failure)
    {
        if (initialization?.State == TransactionInitializationState.Failed)
            return new(Effects: ExecutionEffects.Initialization);
        var evidence = access is IAsyncReadFailureEvidence classifier ? classifier.GetReadFailureEvidence(failure) : new();
        return Dispatched ? evidence : evidence.WithNoDispatch();
    }
}
