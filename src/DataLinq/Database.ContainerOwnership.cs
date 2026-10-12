using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Interfaces;

namespace DataLinq;

public abstract partial class Database<T> where T : class, IDatabaseModel<T>
{
    private OwnedRootDisposal? containerDisposal;

    internal void ValidateContainerOwnership()
    {
        // Delegate targets inspect the actual interface dispatch without scanning
        // members, invoking application cleanup, or requiring container internals.
        Action sync = ((IDisposable)this).Dispose;
        Func<ValueTask> async = ((IAsyncDisposable)this).DisposeAsync;
        if (sync.Method != ((Action)Dispose).Method || async.Method != ((Func<ValueTask>)DisposeAsync).Method)
            throw new NotSupportedException("Container-owned databases must use the Database<T> disposal implementations. UseInstance with application ownership supports custom database disposal.");
    }

    // Called only after activation validation has succeeded, immediately before
    // publishing the root. Registration and never-resolved shutdown do not call it.
    internal void EnableContainerOwnership()
    {
        ValidateContainerOwnership();
        Volatile.Write(ref containerDisposal, ContainerRootDisposal.For(Provider));
    }
}

internal static class ContainerRootDisposal
{
    // More than one Database wrapper may refer to the same provider. Aliases and
    // wrappers share one underlying attempt, including when custom cleanup fails.
    private static readonly ConditionalWeakTable<IDatabaseProvider, OwnedRootDisposal> lifetimes = new();

    internal static OwnedRootDisposal For(IDatabaseProvider provider) => lifetimes.GetValue(provider, static root =>
    {
        Func<ValueTask> async = ((IAsyncDisposable)root).DisposeAsync;
        // The default implementation explicitly has no async capability. Preserve
        // rejection before cleanup/closure, allowing a later explicit sync owner.
        var supportsAsync = async.Method.DeclaringType != typeof(DatabaseProvider) &&
            async.Method.DeclaringType != typeof(IDatabaseProvider);
        return new(() => [RootCleanupStep.Resource(root.Dispose, supportsAsync ? async : null,
            root.State.ValidateDisposal)], root.TelemetryInstanceId);
    });
}
