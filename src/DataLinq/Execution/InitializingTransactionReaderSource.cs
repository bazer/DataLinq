using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>
/// One captured reader invocation whose provider access uses the same lazy resource bundle.
/// Initialization and command acquisition share the enclosing reader's private execution owner.
/// The inner source must validate without accessing unpublished native resources.
/// </summary>
internal sealed class InitializingTransactionReaderSource<T> : IAsyncTransactionReaderSource, IAsyncReadFailureEvidence, IAsyncCommandDispatchEvidence
    where T : class, ITransactionResource
{
    private readonly LazyTransactionResource<T> resource;
    private readonly IAsyncReaderSource source;
    private bool commandStarted;
    private Exception? beforeCommandCancellation;
    public bool CommandDispatched => commandStarted &&
        (source is not IAsyncCommandDispatchEvidence evidence || evidence.CommandDispatched);

    internal InitializingTransactionReaderSource(LazyTransactionResource<T> resource, IAsyncReaderSource source)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(source);
        this.resource = resource;
        this.source = source;
    }

    public void Validate()
    {
        resource.Validate();
        source.Validate();
    }

    public Task<IAsyncDataReader> OpenReaderAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Transaction initialization requires a private execution owner.");

    public async Task<IAsyncDataReader> OpenReaderAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken)
    {
        try { await resource.GetOrInitializeAsync(owner, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException failure) when (failure.CancellationToken == cancellationToken &&
            cancellationToken.IsCancellationRequested && resource.State is TransactionInitializationState.Unused or TransactionInitializationState.Ready)
        {
            beforeCommandCancellation = failure;
            throw;
        }
        // Initialization may succeed despite a late request. This is a separate boundary:
        // no application command has run, and the successfully published bundle stays usable.
        try { cancellationToken.ThrowIfCancellationRequested(); }
        catch (OperationCanceledException failure) { beforeCommandCancellation = failure; throw; }
        commandStarted = true;
        return await (source is IAsyncTransactionReaderSource ownedSource
            ? ownedSource.OpenReaderAsync(owner, cancellationToken)
            : source.OpenReaderAsync(cancellationToken)).ConfigureAwait(false);
    }

    public ReadFailureEvidence GetReadFailureEvidence(Exception failure)
    {
        if (resource.State == TransactionInitializationState.Failed)
            return new(Effects: ExecutionEffects.Initialization);
        var evidence = source is IAsyncReadFailureEvidence classifier ? classifier.GetReadFailureEvidence(failure) : new();
        if (!commandStarted && ReferenceEquals(failure, beforeCommandCancellation))
            return evidence.WithNoDispatch(ExecutionFailureCause.Cancellation);
        return evidence;
    }
}
