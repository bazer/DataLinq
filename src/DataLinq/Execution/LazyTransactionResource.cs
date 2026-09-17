using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

internal enum TransactionInitializationState { Unused, Initializing, Ready, Failed, Disposed }

internal sealed record TransactionInitializationFailure(Exception Cause, Exception? CleanupFailure);

/// <summary>
/// First-use publication under an existing operation lease. Provider resources are private
/// until the entire initialization succeeds. A started failure is terminal, not retryable.
/// W2 will bind this contract to native resources; W1.3 owns public failure diagnostics.
/// </summary>
internal sealed class LazyTransactionResource<T> where T : class, ITransactionResource
{
    private readonly TransactionOperationGate gate;
    private readonly Func<T> create;
    private T? owned;
    private Snapshot snapshot = new(TransactionInitializationState.Unused);
    private sealed record Snapshot(TransactionInitializationState State, T? Resource = null,
        TransactionInitializationFailure? Failure = null);

    internal LazyTransactionResource(TransactionOperationGate gate, Func<T> create)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(create);
        this.gate = gate;
        this.create = create;
    }

    internal TransactionInitializationState State => Volatile.Read(ref snapshot).State;
    internal T? PublishedResource => Volatile.Read(ref snapshot).Resource;
    internal TransactionInitializationFailure? Failure => Volatile.Read(ref snapshot).Failure;

    internal T GetOrInitialize(TransactionOperationGate.Lease operation)
    {
        using var step = gate.EnterStep(operation);
        var ready = GetReadyOrValidate();
        if (ready is not null)
            return ready;

        Publish(new(TransactionInitializationState.Initializing));
        try
        {
            owned = create() ?? throw new InvalidOperationException("The transaction resource factory returned null.");
            owned.Initialize();
            Publish(new(TransactionInitializationState.Ready, owned));
            return owned;
        }
        catch (Exception failure)
        {
            Publish(new(TransactionInitializationState.Failed, Failure: new(failure, null)));
            Exception? cleanupFailure = null;
            try { owned?.Dispose(); owned = null; }
            catch (Exception cleanup) { cleanupFailure = cleanup; }
            Publish(new(TransactionInitializationState.Failed, Failure: new(failure, cleanupFailure)));
            throw;
        }
    }

    internal async Task<T> GetOrInitializeAsync(TransactionOperationGate.Lease operation, CancellationToken cancellationToken)
    {
        using var step = gate.EnterStep(operation);
        var ready = GetReadyOrValidate();
        cancellationToken.ThrowIfCancellationRequested();
        if (ready is not null)
            return ready;

        Publish(new(TransactionInitializationState.Initializing));
        try
        {
            owned = create() ?? throw new InvalidOperationException("The transaction resource factory returned null.");
            await owned.InitializeAsync(cancellationToken).ConfigureAwait(false);
            // A request alone does not mean initialization failed. The next execution
            // boundary observes cancellation before dispatching an application command.
            Publish(new(TransactionInitializationState.Ready, owned));
            return owned;
        }
        catch (Exception failure)
        {
            Publish(new(TransactionInitializationState.Failed, Failure: new(failure, null)));
            Exception? cleanupFailure = null;
            try
            {
                if (owned is not null)
                    await owned.DisposeAsync().ConfigureAwait(false);
                owned = null;
            }
            catch (Exception cleanup) { cleanupFailure = cleanup; }
            Publish(new(TransactionInitializationState.Failed, Failure: new(failure, cleanupFailure)));
            throw;
        }
    }

    internal void Dispose(TransactionOperationGate.Lease operation)
    {
        using var step = gate.EnterStep(operation);
        var previous = Volatile.Read(ref snapshot);
        if (previous.State == TransactionInitializationState.Disposed)
            return;
        Publish(new(TransactionInitializationState.Disposed, Failure: previous.Failure));
        var resource = owned;
        owned = null;
        resource?.Dispose();
    }

    internal async ValueTask DisposeAsync(TransactionOperationGate.Lease operation)
    {
        using var step = gate.EnterStep(operation);
        var previous = Volatile.Read(ref snapshot);
        if (previous.State == TransactionInitializationState.Disposed)
            return;
        Publish(new(TransactionInitializationState.Disposed, Failure: previous.Failure));
        var resource = owned;
        owned = null;
        if (resource is not null)
            await resource.DisposeAsync().ConfigureAwait(false);
    }

    private T? GetReadyOrValidate()
    {
        var current = Volatile.Read(ref snapshot);
        if (current.State == TransactionInitializationState.Disposed)
            throw new ObjectDisposedException(nameof(LazyTransactionResource<T>));
        if (current.State == TransactionInitializationState.Failed)
            throw new InvalidOperationException("Transaction initialization failed; dispose it and use a new transaction.", current.Failure!.Cause);
        return current.Resource;
    }

    private void Publish(Snapshot value) => Volatile.Write(ref snapshot, value);
}
