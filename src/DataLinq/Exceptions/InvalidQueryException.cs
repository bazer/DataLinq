using System;

namespace DataLinq.Exceptions;

public class InvalidQueryException : Exception
{
    private readonly string message;

    public InvalidQueryException(string message)
    {
        this.message = message + " ";
    }

    public InvalidQueryException(string message, Exception innerException) : base(message, innerException)
    {
        this.message = message;
    }

    public override string Message => "The client query is invalid: " + message;
}
