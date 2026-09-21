using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Fixtures;

internal sealed class ControlledTransactionResource : ITransactionResource
{
    internal AsyncCheckpoint Open { get; set; } = new();
    internal AsyncCheckpoint Configure { get; set; } = new();
    internal AsyncCheckpoint Begin { get; set; } = new();
    internal AsyncCheckpoint Cleanup { get; set; } = new();
    internal List<string> Calls { get; } = [];
    internal Exception? SyncInitializationFailure { get; set; }
    internal Exception? SyncCleanupFailure { get; set; }
    internal Action? SyncDisposing { get; set; }
    internal Action? Initialized { get; set; }

    public void Initialize()
    {
        Calls.Add("sync-initialize");
        if (SyncInitializationFailure is not null)
            throw SyncInitializationFailure;
        Initialized?.Invoke();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Calls.Add("async-open");
        await Open.ReachAsync(cancellationToken).ConfigureAwait(false);
        Calls.Add("async-configure");
        await Configure.ReachAsync(cancellationToken).ConfigureAwait(false);
        Calls.Add("async-begin");
        await Begin.ReachAsync(cancellationToken).ConfigureAwait(false);
        Initialized?.Invoke();
    }

    public void Dispose()
    {
        Calls.Add("sync-dispose");
        SyncDisposing?.Invoke();
        if (SyncCleanupFailure is not null)
            throw SyncCleanupFailure;
    }

    public async ValueTask DisposeAsync()
    {
        Calls.Add("async-dispose");
        await Cleanup.ReachAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
