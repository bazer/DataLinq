using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>Rejects overlapping calls before touching an iterator's mutable state.</summary>
internal sealed class EnumeratorCallGate
{
    private int active;
    private TaskCompletionSource? closing;
    internal bool IsClosed => Volatile.Read(ref closing) is not null;

    internal Call Enter()
    {
        if (IsClosed)
            throw new InvalidOperationException("The helper has closed this enumerator.");
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
            throw new InvalidOperationException("An enumerator call is already in progress.");
        if (IsClosed)
        {
            Release();
            throw new InvalidOperationException("The helper has closed this enumerator.");
        }
        return new Call(this);
    }

    internal void StopAdmission()
    {
        if (!IsClosed)
            Interlocked.CompareExchange(ref closing, new(TaskCreationOptions.RunContinuationsAsynchronously), null);
        if (Volatile.Read(ref active) == 0)
            closing!.TrySetResult();
    }

    internal Task WaitForIdleAsync()
    {
        StopAdmission();
        return closing!.Task;
    }

    private void Release()
    {
        Volatile.Write(ref active, 0);
        Volatile.Read(ref closing)?.TrySetResult();
    }

    internal readonly struct Call(EnumeratorCallGate gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

/// <summary>
/// Guards the outer iterator, including its finally blocks. A rejected concurrent
/// move/disposal must not run cleanup or clear the first call's current position.
/// </summary>
internal sealed class GuardedEnumerable<T> : IEnumerable<T>
{
    private readonly Func<IHelperTrackedReader, IEnumerable<T>> source;
    internal GuardedEnumerable(IEnumerable<T> source) : this(_ => source) { }
    internal GuardedEnumerable(Func<IHelperTrackedReader, IEnumerable<T>> source) => this.source = source;
    public IEnumerator<T> GetEnumerator() => new Enumerator(source);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator : IEnumerator<T>, IHelperTrackedReader
    {
        private readonly IEnumerator<T> inner;
        private readonly EnumeratorCallGate calls = new();
        private bool finished;
        private bool helperDrained;

        internal Enumerator(Func<IHelperTrackedReader, IEnumerable<T>> source) => inner = source(this).GetEnumerator();

        public T Current
        {
            get
            {
                using var call = calls.Enter();
                if (finished)
                    throw new InvalidOperationException("The enumerator is not positioned on a row.");
                return inner.Current;
            }
        }
        object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            using var call = calls.Enter();
            if (finished)
                return false;
            using var diagnostics = ExecutionFailureScope.Begin();
            try
            {
                if (inner.MoveNext())
                    return true;
                DisposeCore();
                return false;
            }
            catch
            {
                DisposeCore();
                throw;
            }
        }

        public void Dispose()
        {
            if (Volatile.Read(ref helperDrained))
                return;
            using var call = calls.Enter();
            // DisposeCore marks finished before invoking cleanup. Keep admission
            // ahead of this check so a concurrent finishing call is still rejected.
            if (finished)
                return;
            using var diagnostics = ExecutionFailureScope.Begin();
            DisposeCore();
        }

        private void DisposeCore()
        {
            if (finished)
                return;
            finished = true;
            inner.Dispose();
        }

        public void StopAdmission() => calls.StopAdmission();
        public async ValueTask DrainAsync()
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            await calls.WaitForIdleAsync().ConfigureAwait(false);
            try { DisposeCore(); }
            finally { Volatile.Write(ref helperDrained, true); }
        }

        public void Reset() => throw new NotSupportedException();
    }
}
