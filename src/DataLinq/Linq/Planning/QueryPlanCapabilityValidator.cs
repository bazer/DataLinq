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

        Validate(
            requirements,
            requirements.StructuralFeatures,
            capabilities,
            structural: true);
        Validate(
            requirements,
            requirements.InvocationFeatures,
            capabilities,
            structural: false);
    }

    internal static void ValidateStructural(
        QueryPlanTemplate template,
        QueryBackendCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(capabilities);

        var features = template.StructuralRequirementFeatures;
        for (var index = 0; index < features.Length; index++)
        {
            if (capabilities.GetDisposition(features[index]) == QueryBackendCapabilityDisposition.Supported)
                continue;

            var requirement = QueryPlanRequirements.ExtractStructuralDiagnostics(template)[index];
            throw new QueryBackendCapabilityException(
                capabilities.BackendName,
                requirement.Feature.Token,
                requirement.Location,
                requirement.SourceId,
                requirement.ColumnName);
        }
    }

    private static void Validate(
        QueryPlanRequirements requirements,
        ReadOnlySpan<QueryPlanFeature> features,
        QueryBackendCapabilities capabilities,
        bool structural)
    {
        for (var index = 0; index < features.Length; index++)
        {
            if (capabilities.GetDisposition(features[index]) == QueryBackendCapabilityDisposition.Supported)
                continue;

            var requirement = structural
                ? requirements.Structural[index]
                : requirements.Invocation[index];

            throw new QueryBackendCapabilityException(
                capabilities.BackendName,
                requirement.Feature.Token,
                requirement.Location,
                requirement.SourceId,
                requirement.ColumnName);
        }
    }
}
