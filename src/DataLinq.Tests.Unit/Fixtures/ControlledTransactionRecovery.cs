using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Fixtures;

internal sealed class ControlledTransactionRecovery : IAsyncTransactionRecovery
{
    internal ConcurrentQueue<string> Calls { get; } = new();
    internal AsyncCheckpoint Rollback { get; set; } = new();
    internal AsyncCheckpoint TransactionCleanup { get; set; } = new();
    internal AsyncCheckpoint ConnectionCleanup { get; set; } = new();
    internal CancellationToken RollbackToken { get; private set; }
    internal bool IgnoreCancellation { get; set; }
    internal Action? RollingBack { get; set; }
    internal Action? DisposingTransaction { get; set; }
    internal Action? DisposingConnection { get; set; }

    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        Calls.Enqueue("rollback");
        RollbackToken = cancellationToken;
        RollingBack?.Invoke();
        return Rollback.ReachAsync(IgnoreCancellation ? CancellationToken.None : cancellationToken);
    }

    public ValueTask DisposeTransactionAsync()
    {
        Calls.Enqueue("dispose-transaction");
        DisposingTransaction?.Invoke();
        return new(TransactionCleanup.ReachAsync(CancellationToken.None));
    }

    public ValueTask DisposeConnectionAsync()
    {
        Calls.Enqueue("dispose-connection");
        DisposingConnection?.Invoke();
        return new(ConnectionCleanup.ReachAsync(CancellationToken.None));
    }
}

/// <summary>Manually fires the real CancellationTokenSource timer callback; never sleeps.</summary>
internal sealed class ControlledRecoveryTimeProvider : TimeProvider
{
    internal int Created { get; private set; }
    internal TimeSpan DueTime { get; private set; }
    internal TimeSpan Period { get; private set; }
    internal ControlledTimer? Timer { get; private set; }
    internal Exception? CreationFailure { get; set; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Created++;
        if (CreationFailure is not null)
            throw CreationFailure;
        DueTime = dueTime;
        Period = period;
        return Timer = new ControlledTimer(callback, state);
    }

    internal sealed class ControlledTimer(TimerCallback callback, object? state) : ITimer
    {
        private int disposed;
        internal bool IsDisposed => Volatile.Read(ref disposed) != 0;
        internal void Fire()
        {
            if (!IsDisposed)
                callback(state);
        }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !IsDisposed;
        public void Dispose() => Interlocked.Exchange(ref disposed, 1);
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
