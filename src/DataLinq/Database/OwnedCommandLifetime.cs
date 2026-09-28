using System;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DataLinq;

// Explicit command identity links the virtual command overload to its string
// caller's cleanup. No ambient transaction permission flows to user callbacks.
internal sealed class OwnedCommandLifetime : IDisposable
{
    private static readonly ConditionalWeakTable<IDbCommand, OwnedCommandLifetime> owners = new();
    private readonly IDbCommand command;
    private IDisposable? hold;

    internal OwnedCommandLifetime(IDbCommand command)
    {
        this.command = command;
        owners.Add(command, this);
    }

    internal static void Retain(IDbCommand command, StandaloneTransactionOperation operation)
    {
        if (owners.TryGetValue(command, out var owner))
            owner.hold = operation.RetainThroughCommandCleanup();
    }

    public void Dispose()
    {
        owners.Remove(command);
        Interlocked.Exchange(ref hold, null)?.Dispose();
    }
}
