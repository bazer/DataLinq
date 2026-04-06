using System;

namespace DataLinq.Testing.CLI;

internal static class ConsoleSync
{
    private static readonly object SyncRoot = new();

    public static void Run(Action action)
    {
        lock (SyncRoot)
            action();
    }

    public static void WriteLine(string value) => Run(() => Console.WriteLine(value));

    public static void WriteErrorLine(string value) => Run(() => Console.Error.WriteLine(value));
}
