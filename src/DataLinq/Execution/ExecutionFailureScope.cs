using System;
using System.Threading;

namespace DataLinq.Execution;

/// <summary>
/// Diagnostic provenance only. A marker contains no source, resource or admission
/// authority. Child reports may flow to their caller; a sibling or earlier call's
/// report cannot classify a newly thrown occurrence of the same exception object.
/// </summary>
internal sealed class ExecutionFailureScope
{
    private static readonly AsyncLocal<ExecutionFailureScope?> current = new();
    private readonly ExecutionFailureScope? parent;

    private ExecutionFailureScope(ExecutionFailureScope? parent) => this.parent = parent;
    internal static ExecutionFailureScope? Current => current.Value;

    internal static Call Begin()
    {
        var previous = current.Value;
        current.Value = new(previous);
        return new(previous);
    }

    internal bool Contains(ExecutionFailureScope? reported)
    {
        for (var candidate = reported; candidate is not null; candidate = candidate.parent)
            if (ReferenceEquals(candidate, this)) return true;
        return false;
    }

    internal readonly struct Call(ExecutionFailureScope? previous) : IDisposable
    {
        public void Dispose() => current.Value = previous;
    }
}
