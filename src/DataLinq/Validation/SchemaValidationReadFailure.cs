using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using DataLinq.ErrorHandling;
using DataLinq.Exceptions;
using DataLinq.Execution;

namespace DataLinq.Validation;

internal static class SchemaValidationReadFailure
{
    [DoesNotReturn]
    internal static void Throw(IDLOptionFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        // The coordinator's plain exception leaf adds no independent metadata
        // evidence. Every other shape keeps its original tree in the wrapper.
        if (failure is DLOptionFailure<Exception> { HasInnerFailures: false, SourceLocation: null, FailureType: DLFailureType.Exception } leaf &&
            ExecutionFailureContexts.Get(leaf.Failure)?.SecondaryFailures.Count is null or 0)
            ExceptionDispatchInfo.Capture(leaf.Failure).Throw();
        var exceptions = new List<Exception>();
        Collect(failure, exceptions);
        throw new DataLinqSchemaReadException(failure, exceptions);
    }

    private static void Collect(IDLOptionFailure failure, List<Exception> exceptions)
    {
        var value = failure.FailureValue;
        while (value is IFailureWithDefinition definition) value = definition.FailureValue;
        if (value is Exception exception)
        {
            exceptions.Add(exception);
            if (ExecutionFailureContexts.Get(exception) is { } context)
                foreach (var secondary in context.SecondaryFailures) exceptions.Add(secondary.Exception);
        }
        foreach (var inner in failure.InnerFailures) Collect(inner, exceptions);
    }
}
