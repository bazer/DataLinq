using System;
using DataLinq.Exceptions;

namespace DataLinq.Linq.Planning;

internal static class QueryPlanCapabilityValidator
{
    public static QueryPlanRequirements Validate(
        QueryPlanInvocation invocation,
        QueryBackendCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(capabilities);
        var requirements = QueryPlanRequirements.Extract(invocation);
        Validate(requirements, capabilities);
        return requirements;
    }

    public static void Validate(
        QueryPlanRequirements requirements,
        QueryBackendCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(capabilities);

        foreach (var requirement in requirements.Structural)
            Validate(requirement, capabilities);

        foreach (var requirement in requirements.Invocation)
            Validate(requirement, capabilities);
    }

    private static void Validate(
        QueryPlanRequirement requirement,
        QueryBackendCapabilities capabilities)
    {
        if (capabilities.GetDisposition(requirement.Feature) == QueryBackendCapabilityDisposition.Supported)
            return;

        throw new QueryBackendCapabilityException(
            capabilities.BackendName,
            requirement.Feature.Token,
            requirement.Location,
            requirement.SourceId,
            requirement.ColumnName);
    }
}
