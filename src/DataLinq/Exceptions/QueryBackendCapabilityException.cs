using System;

namespace DataLinq.Exceptions;

/// <summary>
/// Reports that a normalized query uses a feature unsupported by the selected backend.
/// </summary>
public sealed class QueryBackendCapabilityException : QueryTranslationException
{
    internal QueryBackendCapabilityException(
        string backendName,
        string feature,
        string location,
        string? sourceId = null,
        string? columnName = null)
        : base(BuildMessage(backendName, feature, location, sourceId, columnName))
    {
        BackendName = backendName;
        Feature = feature;
        Location = location;
        SourceId = sourceId;
        ColumnName = columnName;
    }

    /// <summary>Gets the backend that rejected the query plan.</summary>
    public string BackendName { get; }

    /// <summary>Gets the unsupported normalized plan feature.</summary>
    public string Feature { get; }

    /// <summary>Gets the normalized plan location that required the feature.</summary>
    public string Location { get; }

    /// <summary>Gets the source identifier when the failure is source-specific.</summary>
    public string? SourceId { get; }

    /// <summary>Gets the column name when the failure is column-specific.</summary>
    public string? ColumnName { get; }

    private static string BuildMessage(
        string backendName,
        string feature,
        string location,
        string? sourceId,
        string? columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendName);
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        var context = sourceId is null
            ? string.Empty
            : columnName is null
                ? $" Source: {sourceId}."
                : $" Source: {sourceId}; column: {columnName}.";

        return $"Backend '{backendName}' cannot execute query plan feature '{feature}'. Location: {location}.{context}";
    }
}
