using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace DataLinq.Execution;

/// <summary>Rejects overlapping calls before touching an iterator's mutable state.</summary>
internal sealed class EnumeratorCallGate
{
    private int active;

    internal Call Enter()
    {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
            throw new InvalidOperationException("An enumerator call is already in progress.");
        return new Call(this);
    }

    internal readonly struct Call(EnumeratorCallGate gate) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref gate.active, 0);
    }
}

/// <summary>
/// Guards the outer iterator, including its finally blocks. A rejected concurrent
/// move/disposal must not run cleanup or clear the first call's current position.
/// </summary>
internal sealed class GuardedEnumerable<T>(IEnumerable<T> source) : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator() => new Enumerator(source.GetEnumerator());
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator(IEnumerator<T> inner) : IEnumerator<T>
    {
        private readonly EnumeratorCallGate calls = new();
        private bool finished;

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
            using var call = calls.Enter();
            DisposeCore();
        }

        private void DisposeCore()
        {
            if (finished)
                return;
            finished = true;
            inner.Dispose();
        }

        public void Reset() => throw new NotSupportedException();
    }
}
