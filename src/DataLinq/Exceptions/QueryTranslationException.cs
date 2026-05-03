using System;

namespace DataLinq.Exceptions;

public class QueryTranslationException : Exception
{
    public QueryTranslationException(string message)
        : base("The LINQ query cannot be translated: " + message)
    {
    }

    public QueryTranslationException(string message, Exception innerException)
        : base("The LINQ query cannot be translated: " + message, innerException)
    {
    }
}
