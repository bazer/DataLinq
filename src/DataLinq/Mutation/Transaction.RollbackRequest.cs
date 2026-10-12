using System;
using System.Diagnostics.CodeAnalysis;

namespace DataLinq.Mutation;

public partial class Transaction
{
    /// <summary>Gets whether an explicit business veto was recorded. False does not establish that commit is otherwise legal.</summary>
    public bool IsRollbackRequested => ExecutionGate.IsRollbackRequested;

    /// <summary>Records an irreversible business veto without provider I/O, interrupting active work, or completing the transaction.</summary>
    /// <remarks>
    /// Normal return guarantees acceptance. Active commands/readers may finish, but new execution and commit are rejected.
    /// Repeated requests preserve the first diagnostic reason; the returned payload contains this call's reason.
    /// Completion admission, terminal state, disposal and helper callback closure reject requests, including repeated requests.
    /// </remarks>
    public Rollback<TFailure> RequestRollback<TFailure>(TFailure reason) where TFailure : notnull
    {
        ArgumentNullException.ThrowIfNull(reason);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ExecutionGate.RequestRollback(reason);
        return new(reason);
    }

    /// <summary>Gets the first accepted reason if it is assignable to the requested type, without changing transaction state.</summary>
    public bool TryGetRollbackReason<TFailure>([MaybeNullWhen(false)] out TFailure reason) where TFailure : notnull
        => ExecutionGate.TryGetRollbackReason(out reason);
}
