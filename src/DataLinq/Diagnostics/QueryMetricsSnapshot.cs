using System.Collections.Generic;

namespace DataLinq.Diagnostics;

/// <summary>
/// Query execution metrics for a provider instance or the full DataLinq runtime.
/// </summary>
/// <param name="EntityExecutions">Total number of entity queries executed.</param>
/// <param name="ScalarExecutions">Total number of scalar queries executed.</param>
public readonly record struct QueryMetricsSnapshot(
    long EntityExecutions,
    long ScalarExecutions)
{
    internal static QueryMetricsSnapshot Sum(IEnumerable<QueryMetricsSnapshot> snapshots)
    {
        long entityExecutions = 0;
        long scalarExecutions = 0;

        foreach (var snapshot in snapshots)
        {
            entityExecutions += snapshot.EntityExecutions;
            scalarExecutions += snapshot.ScalarExecutions;
        }

        return new QueryMetricsSnapshot(
            EntityExecutions: entityExecutions,
            ScalarExecutions: scalarExecutions);
    }
}
