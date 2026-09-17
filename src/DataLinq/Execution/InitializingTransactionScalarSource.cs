using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>Initializes privately under the managed scalar operation's owner before command creation.</summary>
internal sealed class InitializingTransactionScalarSource<T>(LazyTransactionResource<T> resource, IAsyncScalarSource source)
    : IAsyncTransactionScalarSource, IAsyncReadFailureEvidence where T : class, ITransactionResource
{
    private bool commandStarted;
    private Exception? beforeCommandCancellation;

    public void Validate() { resource.Validate(); source.Validate(); }

    public Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Transaction initialization requires a private execution owner.");

    public async Task<object?> ExecuteScalarAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken)
    {
        try { await resource.GetOrInitializeAsync(owner, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException failure) when (failure.CancellationToken == cancellationToken &&
            cancellationToken.IsCancellationRequested && resource.State is TransactionInitializationState.Unused or TransactionInitializationState.Ready)
        {
            beforeCommandCancellation = failure;
            throw;
        }
        try { cancellationToken.ThrowIfCancellationRequested(); }
        catch (OperationCanceledException failure) { beforeCommandCancellation = failure; throw; }
        commandStarted = true;
        return await (source is IAsyncTransactionScalarSource ownedSource
            ? ownedSource.ExecuteScalarAsync(owner, cancellationToken)
            : source.ExecuteScalarAsync(cancellationToken)).ConfigureAwait(false);
    }

    public ReadFailureEvidence GetReadFailureEvidence(Exception failure)
    {
        if (resource.State == TransactionInitializationState.Failed) return new(Effects: ExecutionEffects.Initialization);
        var evidence = source is IAsyncReadFailureEvidence classifier ? classifier.GetReadFailureEvidence(failure) : new();
        return !commandStarted && ReferenceEquals(failure, beforeCommandCancellation)
            ? evidence with { Cause = ExecutionFailureCause.Cancellation, Effects = ExecutionEffects.NoStatement, Integrity = TransactionIntegrity.Confirmed }
            : evidence;
    }
}
