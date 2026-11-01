using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.ErrorHandling;

namespace DataLinq.Validation;

/// <summary>A completed comparison and its captured failure policy, without live provider resources.</summary>
/// <remarks>Collections and issue context messages are snapshots. Difference references to finalized
/// metadata are retained; metadata graphs are not deep-cloned. Operational read failures do not produce a result.</remarks>
public sealed class DataLinqSchemaValidationResult
{
    internal DataLinqSchemaValidationResult(string databaseName, DatabaseType databaseType,
        int modelTableCount, int databaseTableCount, IEnumerable<SchemaDifference> differences,
        IEnumerable<DataLinqDiagnosticIssue> issues, CapturedSchemaValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(databaseName);
        ArgumentNullException.ThrowIfNull(differences);
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(modelTableCount);
        ArgumentOutOfRangeException.ThrowIfNegative(databaseTableCount);
        DatabaseName = databaseName;
        DatabaseType = databaseType;
        ModelTableCount = modelTableCount;
        DatabaseTableCount = databaseTableCount;
        Differences = Array.AsReadOnly(differences.ToArray());
        Issues = Array.AsReadOnly(issues.Select(issue => new DataLinqDiagnosticIssue(
            issue.Severity, issue.FailureType, issue.Message, issue.SourceLocation, issue.ObjectPath,
            Array.AsReadOnly(issue.ContextMessages.ToArray()))).ToArray());
        HasDifferences = Differences.Count != 0;
        // Issue severity uses a different enum ordering and is intentionally
        // independent of the difference threshold, including Warning issues.
        HasFailures = Differences.Any(difference => difference.Severity >= options.FailOnSeverity)
            || options.TreatValidationIssuesAsFailures && Issues.Count != 0;
    }

    /// <summary>Gets the effective database name reported by the provider.</summary>
    public string DatabaseName { get; }
    /// <summary>Gets the provider database type.</summary>
    public DatabaseType DatabaseType { get; }
    /// <summary>Gets the number of model tables/views inside the comparison scope.</summary>
    public int ModelTableCount { get; }
    /// <summary>Gets the number of live tables/views inside the comparison scope.</summary>
    public int DatabaseTableCount { get; }
    /// <summary>Gets every comparison difference, including informational findings.</summary>
    public IReadOnlyList<SchemaDifference> Differences { get; }
    /// <summary>Gets every typed metadata issue inside the comparison scope, regardless of failure policy.</summary>
    public IReadOnlyList<DataLinqDiagnosticIssue> Issues { get; }
    /// <summary>Gets whether any difference was found, independently of failure policy.</summary>
    public bool HasDifferences { get; }
    /// <summary>Gets whether differences or issues fail the policy captured for this invocation.</summary>
    public bool HasFailures { get; }
}
