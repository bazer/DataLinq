using DataLinq.Metadata;
using DataLinq.Instances;

namespace DataLinq.Interfaces;

/// <summary>
/// Minimal source contract required by backend-neutral generated read models.
/// </summary>
public interface IDataLinqReadSource
{
    DatabaseDefinition Metadata { get; }
}

/// <summary>
/// Internal runtime services carried by an actual read source. The public source identity remains
/// metadata-only so generated consumer code does not inherit cache, loader, or SQL contracts.
/// </summary>
internal interface IDataLinqReadServices : IDataLinqReadSource
{
    IModelMaterializationServices MaterializationServices { get; }
}
