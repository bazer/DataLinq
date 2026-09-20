using System.Data;
using System.Runtime.CompilerServices;

namespace DataLinq.Execution;

/// <summary>
/// Failure-only dispatch evidence. The opaque identity retains no command, and current
/// invocation scopes reject stale occurrences. It grants no admission or native certainty.
/// </summary>
internal sealed class CommandDispatchEvidence
{
    private static readonly ConditionalWeakTable<IDbCommand, object> identities = new();
    private readonly object identity;
    internal bool Dispatched { get; }

    internal CommandDispatchEvidence(IDbCommand command, bool dispatched)
    {
        identity = identities.GetValue(command, static _ => new());
        Dispatched = dispatched;
    }

    internal static void Attach(System.Exception failure, ExecutionFailureContext context, IDbCommand command, bool dispatched) =>
        ExecutionFailureContexts.Attach(failure, new(context.Cause, context.Stage, context.Completion, context.Recovery,
            context.TransactionId, context.SecondaryFailures, context.HasCleanupFailure, context.Operation,
            context.ProviderInstanceId, context.ActiveOperation) { Scope = context.Scope, CommandDispatch = new(command, dispatched) });

    internal static bool ProvesNoDispatch(System.Exception failure, IDbCommand command) =>
        ExecutionFailureScope.Current is not null &&
        ExecutionFailureContexts.GetCurrent(failure)?.CommandDispatch is { Dispatched: false } evidence &&
        identities.TryGetValue(command, out var identity) && ReferenceEquals(identity, evidence.identity);
}
