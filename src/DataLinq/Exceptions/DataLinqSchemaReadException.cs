using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.ErrorHandling;

namespace DataLinq.Exceptions;

/// <summary>A failed schema read whose failure tree cannot be represented losslessly by one original exception.</summary>
/// <remarks>This is an operational failure, not a completed schema comparison. Original exceptions and their execution evidence are retained.</remarks>
public sealed class DataLinqSchemaReadException : Exception
{
    internal DataLinqSchemaReadException(IDLOptionFailure failure, IReadOnlyList<Exception> exceptions)
        : base(failure.Message, exceptions.Count switch { 0 => null, 1 => exceptions[0], _ => new AggregateException(exceptions) })
    {
        Failure = failure;
        Exceptions = Array.AsReadOnly(exceptions.ToArray());
    }

    /// <summary>Gets the original metadata failure tree, including non-exception failures and source context.</summary>
    public IDLOptionFailure Failure { get; }
    /// <summary>Gets original exceptions in failure-tree encounter order, followed by each exception's ordered secondary evidence.</summary>
    public IReadOnlyList<Exception> Exceptions { get; }
}
