using System;
using DataLinq.ErrorHandling;

namespace DataLinq.Exceptions;

public class DLOptionFailureException(IDLOptionFailure failure) : Exception
{
    public IDLOptionFailure Failure { get; } = failure;

    public override string Message => Failure.ToString();
    public override string ToString()
    {
        // Start with the core failure message (which now comes from Failure.ToString())
        string message = Message; // Uses the overridden Message property
        string exceptionTypeName = GetType().FullName ?? "DLOptionFailureException"; // Get specific exception type name

        // Add Exception Type Info Clearly
        string result = $"{exceptionTypeName}: {message}";

        // Append InnerException details if present
        if (InnerException != null)
        {
            result = $"{result} ---> {InnerException}\n   --- End of inner exception stack trace ---";
        }

        // Append StackTrace if available
        if (StackTrace != null)
        {
            result = $"{result}\n{StackTrace}";
        }

        return result;
    }
}

public class DLOptionFailureException<T>(DLOptionFailure<T> failure) : DLOptionFailureException(failure)
{
    public new DLOptionFailure<T> Failure => (DLOptionFailure<T>)base.Failure;
}
