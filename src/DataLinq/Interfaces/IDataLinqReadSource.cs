using DataLinq.Metadata;

namespace DataLinq.Interfaces;

/// <summary>
/// Minimal source contract required by backend-neutral generated read models.
/// </summary>
public interface IDataLinqReadSource
{
    DatabaseDefinition Metadata { get; }
}
