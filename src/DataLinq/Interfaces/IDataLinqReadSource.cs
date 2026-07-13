using DataLinq.Metadata;
using DataLinq.Instances;
using DataLinq.Linq.Planning;

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

/// <summary>
/// Read-service capability for sources whose cache-cold primary-key rows can be loaded through the
/// neutral source-row contract. Sources implement this only when a real loader is available.
/// </summary>
internal interface IDataLinqSourceRowServices : IDataLinqReadServices
{
    ISourceRowLoader RowLoader { get; }
}

/// <summary>
/// Optional execution capability implemented by read sources that can execute neutral query plans.
/// </summary>
internal interface IDataLinqQueryPlanServices : IDataLinqReadServices
{
    IQueryPlanBackend QueryPlanBackend { get; }
}
