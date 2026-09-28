using System;
using DataLinq.Execution;

namespace DataLinq;

/// <summary>Execution settings captured by a provider when it is constructed.</summary>
public sealed class DataLinqExecutionOptions
{
    /// <summary>Gets the independent budget for automatic recovery rollback. The default is 30 seconds.</summary>
    /// <remarks>
    /// Must be between 1 and 4,294,967,294 milliseconds inclusive. This budget starts when
    /// recovery rollback begins; it is neither a request timeout nor a resource-cleanup deadline.
    /// </remarks>
    public TimeSpan RecoveryRollbackTimeout { get; init; } = TimeSpan.FromSeconds(30);

    internal static DataLinqExecutionOptions Capture(DataLinqExecutionOptions executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        var settings = new RecoveryRollbackSettings(executionOptions.RecoveryRollbackTimeout);
        return new() { RecoveryRollbackTimeout = settings.RecoveryRollbackTimeout };
    }
}
