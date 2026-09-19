using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

// Internal W1 capability. A legacy provider cannot acquire async support from its
// synchronous Dispose implementation; native provider adoption remains W2.
internal interface IAsyncRootDisposal
{
    ValueTask DisposeAsyncCore();
}

internal sealed class RootCleanupStep
{
    private RootCleanupStep(Action dispose, Func<ValueTask>? disposeAsync, Action? validate)
    {
        Dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        DisposeAsync = disposeAsync;
        Validate = validate;
    }

    internal Action Dispose { get; }
    internal Func<ValueTask>? DisposeAsync { get; }
    internal Action? Validate { get; }

    internal static RootCleanupStep Local(Action dispose) =>
        new(dispose, () => { dispose(); return default; }, null);

    internal static RootCleanupStep Resource(Action dispose, Func<ValueTask>? disposeAsync, Action? validate = null) =>
        new(dispose, disposeAsync, validate);
}

/// <summary>
/// One attempt shared by both disposal forms. Captures only owned resources;
/// dependent transactions/readers are never canceled or drained here.
/// </summary>
internal sealed class OwnedRootDisposal(Func<RootCleanupStep[]> capture, string? providerInstanceId = null)
{
    private readonly Func<RootCleanupStep[]> capture = capture ?? throw new ArgumentNullException(nameof(capture));
    private int state; // 0 usable, 1 disposing, 2 disposed (including failed cleanup).

    internal bool IsDisposed => Volatile.Read(ref state) == 2;
    internal void EnsureUsable()
    {
        if (Volatile.Read(ref state) != 0) throw new ObjectDisposedException(nameof(OwnedRootDisposal));
    }

    private RootCleanupStep[]? Begin(bool asynchronous)
    {
        var current = Volatile.Read(ref state);
        if (current == 2) return null;
        if (current == 1) throw new InvalidOperationException("Root disposal is already in progress.");
        // Capability/lifecycle validation must not close an unsupported or reentrant
        // root. The capture callback is I/O-free and returns a resource snapshot.
        var steps = (RootCleanupStep[])(capture() ?? throw new InvalidOperationException("Root cleanup capture returned no steps.")).Clone();
        foreach (var step in steps)
        {
            ArgumentNullException.ThrowIfNull(step);
            if (asynchronous && step.DisposeAsync is null)
                throw new NotSupportedException("An owned resource does not support asynchronous disposal.");
            step.Validate?.Invoke();
        }
        current = Interlocked.CompareExchange(ref state, 1, 0);
        if (current == 2) return null;
        if (current != 0) throw new InvalidOperationException("Root disposal is already in progress.");
        return steps;
    }

    internal void Dispose()
    {
        if (Begin(asynchronous: false) is not { } steps) return;
        ExecutionFailures? failures = null;
        try
        {
            foreach (var step in steps)
                try { step.Dispose(); }
                catch (Exception failure) { (failures ??= new()).AddCleanup(failure); }
        }
        finally { Volatile.Write(ref state, 2); }
        ThrowFailures(failures, providerInstanceId);
    }

    internal ValueTask DisposeAsync()
        => Begin(asynchronous: true) is { } steps ? DisposeAsync(steps) : default;

    private async ValueTask DisposeAsync(RootCleanupStep[] steps)
    {
        ExecutionFailures? failures = null;
        try
        {
            foreach (var step in steps)
                try { await step.DisposeAsync!().ConfigureAwait(false); }
                catch (Exception failure) { (failures ??= new()).AddCleanup(failure); }
        }
        finally { Volatile.Write(ref state, 2); }
        ThrowFailures(failures, providerInstanceId);
    }

    internal static void ThrowFailures(ExecutionFailures? failures, string? providerInstanceId = null)
    {
        if (failures?.Primary is not { } failure) return;
        ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
            ExecutionRecoveryActions.None, transactionId: null, ExecutionOperationKind.Dispose, providerInstanceId));
        failures.ThrowIfAny();
    }
}
