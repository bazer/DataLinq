using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Fixtures;

internal sealed class ControlledOwnedCommand : IAsyncOwnedCommand
{
    internal ControlledCommand Borrowed { get; } = new();
    public IDbCommand Command => CommandFailure is { } failure ? throw failure : Borrowed;
    internal AsyncCheckpoint Cleanup { get; set; } = new();
    internal Exception? CommandFailure { get; set; }
    internal Exception? SyncCleanupFailure { get; set; }
    internal int AsyncDisposals { get; private set; }
    internal int SyncDisposals { get; private set; }
    internal Action? Disposing { get; set; }

    public async ValueTask DisposeAsync()
    {
        AsyncDisposals++;
        Disposing?.Invoke();
        await Cleanup.ReachAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        SyncDisposals++;
        Disposing?.Invoke();
        if (SyncCleanupFailure is not null) throw SyncCleanupFailure;
    }
}

internal sealed class ControlledOwnedCommandFactory : IAsyncOwnedCommandFactory
{
    internal Func<IAsyncOwnedCommand>? Creating { get; set; }
    internal ControlledOwnedCommand Resource { get; } = new();
    internal List<AsyncCommandKind> Validations { get; } = [];
    internal Exception? ValidationFailure { get; set; }
    internal int Creates { get; private set; }
    public void Validate(AsyncCommandKind kind)
    {
        Validations.Add(kind);
        if (ValidationFailure is not null) throw ValidationFailure;
    }
    public IAsyncOwnedCommand Create()
    {
        Creates++;
        return Creating is null ? Resource : Creating();
    }
}
