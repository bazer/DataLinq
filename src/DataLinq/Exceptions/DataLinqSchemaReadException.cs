using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;

namespace DataLinq.Exceptions;

/// <summary>A failed schema read whose failure tree cannot be represented losslessly by one original exception.</summary>
/// <remarks>This is an operational failure, not a completed schema comparison. Original exceptions and their execution evidence are retained.</remarks>
public sealed class DataLinqSchemaReadException : Exception
{
    internal DataLinqSchemaReadException(IDLOptionFailure failure, IReadOnlyList<Exception> exceptions)
        : base(string.Join("; ", Summaries(failure)), exceptions.Count switch { 0 => null, 1 => exceptions[0], _ => new AggregateException(exceptions) })
    {
        Failure = failure;
        Exceptions = Array.AsReadOnly(exceptions.ToArray());
    }

    /// <summary>Gets the original metadata failure tree, including non-exception failures and source context.</summary>
    public IDLOptionFailure Failure { get; }
    /// <summary>Gets original exceptions in failure-tree encounter order, followed by each exception's ordered secondary evidence.</summary>
    public IReadOnlyList<Exception> Exceptions { get; }

    private static IEnumerable<string> Summaries(IDLOptionFailure failure)
    {
        // Exception.Message is original diagnostic text without duplicating its
        // stack in both this summary and the ordinary InnerException rendering.
        var message = ValueSummary(failure.FailureValue) ?? failure.OwnMessage;
        if (failure.SourceLocation is { } location)
            message += $" at {SourceLocationFormatter.Format(location)}";
        if (!string.IsNullOrEmpty(message)) yield return $"[{failure.FailureType}] {message}";
        foreach (var inner in failure.InnerFailures)
            foreach (var messagePart in Summaries(inner)) yield return messagePart;
    }

    private static string? ValueSummary(object? value)
    {
        if (value is Exception exception) return exception.Message;
        if (value is IFailureWithDefinition definition)
        {
            var message = ValueSummary(definition.FailureValue) ?? "";
            var originalContext = new FailureWithDefinition<string>(message, definition.Definition).ToString();
            var path = DataLinqDiagnosticObjectPath.GetObjectPath(definition.Definition);
            return string.IsNullOrEmpty(path) ? originalContext : $"{originalContext} ({path})";
        }
        return value?.ToString();
    }
}
