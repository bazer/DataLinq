using System;

namespace DataLinq.Exceptions;

public class InvalidMutationObjectException : System.Exception
{
    public InvalidMutationObjectException(string message) : base(message)
    {
    }

    public InvalidMutationObjectException() : base()
    {
    }

    public InvalidMutationObjectException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public override string Message =>
        string.IsNullOrWhiteSpace(base.Message)
            ? "The client query is invalid."
            : "The client query is invalid: " + base.Message;
}
