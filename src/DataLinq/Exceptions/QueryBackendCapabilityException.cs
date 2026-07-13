using System;

namespace DataLinq.Exceptions;

internal sealed class QueryBackendCapabilityException : QueryTranslationException
{
    public QueryBackendCapabilityException(
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

    public string BackendName { get; }

    public string Feature { get; }

    public string Location { get; }

    public string? SourceId { get; }

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
