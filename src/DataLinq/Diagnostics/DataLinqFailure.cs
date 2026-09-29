using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DataLinq.Execution;

namespace DataLinq.Diagnostics;

/// <summary>Accesses DataLinq's diagnostic snapshot attached to an exception.</summary>
public static class DataLinqFailure
{
    private static readonly ConditionalWeakTable<ExecutionFailureContext, DataLinqFailureContext> snapshots = new();

    /// <summary>Gets the context attached directly to this exception, or null if none is attached.</summary>
    /// <remarks>Does not inspect inner exceptions or aggregate branches. Null throws. No exception is replaced or sanitized.</remarks>
    public static DataLinqFailureContext? GetContext(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Capture(ExecutionFailureContexts.Get(exception));
    }

    internal static DataLinqFailureContext? Capture(ExecutionFailureContext? context) =>
        context is null ? null : snapshots.GetValue(context, static source => new(source));
}

/// <summary>An immutable snapshot of failure facts and permitted recovery at the time of reporting.</summary>
/// <remarks>
/// Adds no SQL, keys or live database resources. Original exceptions may contain sensitive data;
/// they are not sanitized or automatically serialized. A later transaction snapshot can differ.
/// </remarks>
public sealed class DataLinqFailureContext
{
    /// <summary>Gets the evidence-based failure cause.</summary>
    public DataLinqFailureCause Cause { get; }
    /// <summary>Gets the specific failing operation.</summary>
    public DataLinqOperationKind Operation { get; }
    /// <summary>Gets the failing execution stage.</summary>
    public DataLinqFailureStage Stage { get; }
    /// <summary>Gets transaction-completion certainty, independently of later cleanup failure.</summary>
    public DataLinqCompletionOutcome CompletionOutcome { get; }
    /// <summary>Gets actions permitted when this snapshot was recorded. Each later operation validates its current state.</summary>
    public DataLinqRecoveryActions RecoveryActions { get; }
    /// <summary>Gets an ordered, defensively protected collection of original secondary exceptions.</summary>
    public IReadOnlyList<DataLinqSecondaryFailure> SecondaryFailures { get; }
    /// <summary>Gets the managed transaction correlation identifier, if applicable.</summary>
    public uint? TransactionId { get; }
    /// <summary>Gets the provider telemetry-instance identifier, if available.</summary>
    public string? ProviderInstanceId { get; }
    /// <summary>Gets the conflicting active operation when admission was rejected.</summary>
    public DataLinqOperationKind? ActiveOperation { get; }

    internal DataLinqFailureContext(ExecutionFailureContext source)
    {
        Cause = PublicFailureMapping.Cause(source.Cause);
        Operation = PublicFailureMapping.OperationKind(source.Operation);
        Stage = PublicFailureMapping.Stage(source.Stage, source.Operation);
        CompletionOutcome = PublicFailureMapping.Completion(source.Completion);
        RecoveryActions = PublicFailureMapping.Recovery(source.Recovery);
        SecondaryFailures = Array.AsReadOnly(source.SecondaryFailures.Select(value => new DataLinqSecondaryFailure(value)).ToArray());
        TransactionId = source.TransactionId;
        ProviderInstanceId = source.ProviderInstanceId;
        ActiveOperation = source.ActiveOperation is { } active ? PublicFailureMapping.OperationKind(active) : null;
    }
}

/// <summary>An immutable entry retaining an original secondary exception and its classification.</summary>
public sealed class DataLinqSecondaryFailure
{
    /// <summary>Gets the evidence-based failure cause.</summary>
    public DataLinqFailureCause Cause { get; }
    /// <summary>Gets the specific failing operation.</summary>
    public DataLinqOperationKind Operation { get; }
    /// <summary>Gets the failing execution stage.</summary>
    public DataLinqFailureStage Stage { get; }
    /// <summary>Gets the original exception. It is not flattened, replaced or sanitized.</summary>
    public Exception Exception { get; }

    internal DataLinqSecondaryFailure(ExecutionSecondaryFailure source)
    {
        Cause = PublicFailureMapping.Cause(source.Cause);
        Operation = PublicFailureMapping.OperationKind(source.Operation);
        Stage = PublicFailureMapping.Stage(source.Stage, source.Operation);
        Exception = source.Exception;
    }
}
