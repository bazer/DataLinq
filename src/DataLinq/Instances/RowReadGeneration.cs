namespace DataLinq.Instances;

/// <summary>
/// Opaque read/publication generation. Only in-flight loads carry it; published
/// model rows retain no token, source, or cache reference through this metadata.
/// </summary>
internal sealed class RowReadGeneration;
