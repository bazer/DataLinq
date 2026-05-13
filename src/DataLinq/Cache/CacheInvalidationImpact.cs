using System.Collections.Generic;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq.Cache;

internal readonly record struct RelationCacheKey(ColumnIndex Index, DataLinqKey ProviderKey);

internal sealed class CacheInvalidationImpact
{
    public static CacheInvalidationImpact TableWide { get; } = new(
        clearTable: true,
        changedPrimaryKeys: new HashSet<DataLinqKey>(),
        changedRelationKeys: new HashSet<RelationCacheKey>());

    public CacheInvalidationImpact(
        bool clearTable,
        IReadOnlySet<DataLinqKey> changedPrimaryKeys,
        IReadOnlySet<RelationCacheKey> changedRelationKeys)
    {
        ClearTable = clearTable;
        ChangedPrimaryKeys = changedPrimaryKeys;
        ChangedRelationKeys = changedRelationKeys;
    }

    public bool ClearTable { get; }
    public IReadOnlySet<DataLinqKey> ChangedPrimaryKeys { get; }
    public IReadOnlySet<RelationCacheKey> ChangedRelationKeys { get; }

    public bool IsEmpty =>
        !ClearTable &&
        ChangedPrimaryKeys.Count == 0 &&
        ChangedRelationKeys.Count == 0;
}

internal sealed class CacheInvalidationImpactBuilder
{
    private bool clearTable;
    private HashSet<DataLinqKey>? changedPrimaryKeys;
    private HashSet<RelationCacheKey>? changedRelationKeys;

    public void ClearTable() => clearTable = true;

    public void AddPrimaryKey(DataLinqKey primaryKey)
    {
        if (primaryKey.IsNull)
            return;

        (changedPrimaryKeys ??= []).Add(primaryKey);
    }

    public void AddRelationKey(ColumnIndex index, DataLinqKey providerKey)
    {
        if (providerKey.IsNull)
            return;

        (changedRelationKeys ??= []).Add(new RelationCacheKey(index, providerKey));
    }

    public CacheInvalidationImpact Build() => new(
        clearTable,
        changedPrimaryKeys ?? new HashSet<DataLinqKey>(),
        changedRelationKeys ?? new HashSet<RelationCacheKey>());
}
