using System;
using DataLinq.Validation;

namespace DataLinq.Exceptions;

/// <summary>A completed schema comparison failed the configured validation policy.</summary>
/// <remarks>Connection failures, incomplete metadata reads and cancellation are operational failures,
/// not instances of this result-based exception.</remarks>
public sealed class DataLinqSchemaValidationException : Exception
{
    /// <summary>Creates an exception retaining the exact supplied comparison result.</summary>
    public DataLinqSchemaValidationException(DataLinqSchemaValidationResult result)
        : base(CreateMessage(result)) => Result = result;

    /// <summary>Gets the complete comparison result, including findings below the failure threshold.</summary>
    public DataLinqSchemaValidationResult Result { get; }

    private static string CreateMessage(DataLinqSchemaValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return $"Schema validation failed for {result.DatabaseType} database '{result.DatabaseName}': "
            + $"{result.Differences.Count} difference(s), {result.Issues.Count} metadata issue(s).";
    }
}
