using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

internal enum TransactionInitializationState { Unused, Initializing, Ready, Failed, Disposed }

internal sealed record TransactionInitializationFailure(Exception Cause, Exception? CleanupFailure);

/// <summary>
/// First-use publication under an existing operation lease. Provider resources are private
/// until the entire initialization succeeds. A started failure is terminal, not retryable.
/// W2 will bind this contract to native resources; public diagnostics remain W3.
/// </summary>
internal sealed class LazyTransactionResource<T> : IAsyncCommandInitialization, ISyncCommandInitialization where T : class, ITransactionResource
{
    private readonly TransactionOperationGate gate;
    private readonly Func<T> create;
    private T? owned;
    private int activeCall;
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

    TransactionInitializationState IAsyncCommandInitialization.State => State;
    void IAsyncCommandInitialization.Validate() => Validate();
    async Task IAsyncCommandInitialization.InitializeAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken)
        => _ = await GetOrInitializeAsync(owner, cancellationToken).ConfigureAwait(false);

    TransactionInitializationState ISyncCommandInitialization.State => State;
    void ISyncCommandInitialization.Validate() => Validate();
    void ISyncCommandInitialization.Initialize(TransactionOperationGate.Step owner) => GetOrInitialize(owner);

    internal T GetOrInitialize(TransactionOperationGate.Lease operation)
    {
        using var step = gate.EnterStep(operation);
        return GetOrInitialize(step);
    }

    internal T GetOrInitialize(TransactionOperationGate.Step step)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        using var call = EnterCall(step);
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
            var failures = CaptureInitializationFailure(failure, CancellationToken.None, step.Kind);
            Publish(new(TransactionInitializationState.Failed, Failure: new(failure, null)));
            Exception? cleanupFailure = null;
            if (owned is not null)
            {
                using var cleanupDiagnostics = ExecutionFailureScope.Begin();
                try { owned.Dispose(); owned = null; }
                catch (Exception cleanup) { cleanupFailure = cleanup; failures.AddCleanup(cleanup); }
            }
            Publish(new(TransactionInitializationState.Failed, Failure: new(failure, cleanupFailure)));
            ReportInitializationFailure(failures, step.Kind);
            // Cleanup may throw the same instance and overwrite its live stack.
            failures.ThrowIfAny();
            throw;
        }
    }

    internal async Task<T> GetOrInitializeAsync(TransactionOperationGate.Lease operation, CancellationToken cancellationToken)
    {
        using var step = gate.EnterStep(operation);
        return await GetOrInitializeAsync(step, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<T> GetOrInitializeAsync(TransactionOperationGate.Step step, CancellationToken cancellationToken)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        using var call = EnterCall(step);
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
            var failures = CaptureInitializationFailure(failure, cancellationToken, step.Kind);
            Publish(new(TransactionInitializationState.Failed, Failure: new(failure, null)));
            Exception? cleanupFailure = null;
            if (owned is not null)
            {
                using var cleanupDiagnostics = ExecutionFailureScope.Begin();
                try { await owned.DisposeAsync().ConfigureAwait(false); owned = null; }
                catch (Exception cleanup) { cleanupFailure = cleanup; failures.AddCleanup(cleanup); }
            }
            Publish(new(TransactionInitializationState.Failed, Failure: new(failure, cleanupFailure)));
            ReportInitializationFailure(failures, step.Kind);
            failures.ThrowIfAny();
            throw;
        }
    }

    internal void Dispose(TransactionOperationGate.Lease operation)
    {
        using var step = gate.EnterStep(operation);
        Dispose(step);
    }

    internal void Dispose(TransactionOperationGate.Step step)
    {
        using var call = EnterCall(step);
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
        await DisposeAsync(step).ConfigureAwait(false);
    }

    internal async ValueTask DisposeAsync(TransactionOperationGate.Step step)
    {
        using var call = EnterCall(step);
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
        if (current.State == TransactionInitializationState.Initializing)
            throw new InvalidOperationException("Transaction initialization is already in progress.");
        return current.Resource;
    }

    internal void Validate() => _ = GetReadyOrValidate();

    private ResourceCall EnterCall(TransactionOperationGate.Step step)
    {
        gate.ValidateStep(step);
        // A private step may be passed down a call chain, but does not authorize
        // overlapping initialization/disposal calls inside that chain.
        if (Interlocked.CompareExchange(ref activeCall, 1, 0) != 0)
            throw new InvalidOperationException("The transaction resource already has an active initialization or disposal call.");
        return new(this);
    }

    private readonly struct ResourceCall(LazyTransactionResource<T> resource) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref resource.activeCall, 0);
    }

    private static ExecutionFailures CaptureInitializationFailure(Exception failure, CancellationToken token, ExecutionOperationKind operation)
    {
        var failures = new ExecutionFailures();
        var cause = failure is OperationCanceledException canceled &&
            canceled.CancellationToken == token && token.IsCancellationRequested
                ? ExecutionFailureCause.Cancellation : ExecutionFailureContexts.GetCurrent(failure)?.Cause ?? ExecutionFailureCause.Unknown;
        // Initialization is the enclosing boundary; import any nested cleanup details
        // without allowing an older exception attachment to change that boundary.
        failures.Add(failure, cause, ExecutionFailureStage.Initialization, operation);
        failures.AddReported(failure, ExecutionFailureStage.Initialization);
        return failures;
    }

    private void ReportInitializationFailure(ExecutionFailures failures, ExecutionOperationKind operation) =>
        ExecutionFailureContexts.Attach(failures.Primary!, failures.Snapshot(new(Effects: ExecutionEffects.Initialization),
            ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, gate.TransactionId,
            operation, gate.ProviderInstanceId, providerIdentityIsAuthoritative: true));

    private void Publish(Snapshot value) => Volatile.Write(ref snapshot, value);
}
