using System;

namespace DataLinq.Exceptions;

/// <summary>An explicit business rollback request prevents new execution or commit.</summary>
/// <remarks>The reason is distinct from operational failure diagnostics. Manual owners must still roll back and dispose.</remarks>
public sealed class DataLinqRollbackRequestedException : InvalidOperationException
{
    /// <summary>Gets the original non-null reason, without formatting it into the exception message.</summary>
    public object Reason { get; }

    internal DataLinqRollbackRequestedException(object reason)
        : base("Rollback was requested for this transaction; new execution and commit are not permitted.") => Reason = reason;
}
