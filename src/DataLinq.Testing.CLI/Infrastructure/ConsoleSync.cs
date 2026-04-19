using System;
using System.Threading;

namespace DataLinq.Testing.CLI;

internal static class ConsoleSync
{
    private static readonly object SyncRoot = new();
    private static int mutedDepth;

    public static void Run(Action action)
    {
        lock (SyncRoot)
            action();
    }

    public static IDisposable PushMuted()
    {
        Interlocked.Increment(ref mutedDepth);
        return new MuteScope();
    }

    public static void WriteLine(string value)
    {
        if (Volatile.Read(ref mutedDepth) > 0)
            return;

        Run(() => Console.WriteLine(value));
    }

    public static void WriteErrorLine(string value)
    {
        if (Volatile.Read(ref mutedDepth) > 0)
            return;

        Run(() => Console.Error.WriteLine(value));
    }

    private sealed class MuteScope : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                Interlocked.Decrement(ref mutedDepth);
        }
    }
}
