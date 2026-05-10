using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Metadata;

public class DatabaseDefinition : IDefinition
{
    private static readonly ConcurrentDictionary<Type, DatabaseDefinition> loadedDatabases = new();
    private MetadataCollection<Attribute> attributes = MetadataCollection<Attribute>.Empty;
    private MetadataCollection<TableModel> tableModels = MetadataCollection<TableModel>.Empty;
    private Dictionary<Type, TableModel>? tableModelsByModelType;
    private Dictionary<string, TableModel>? tableModelsByDbName;

    [Obsolete("Direct mutation of the global metadata registry is obsolete. Runtime code should use provider-owned metadata or internal registry helpers.")]
    public static ConcurrentDictionary<Type, DatabaseDefinition> LoadedDatabases => loadedDatabases;

    internal static IEnumerable<DatabaseDefinition> LoadedDatabaseValues => loadedDatabases.Values;

    internal static bool TryGetLoadedDatabase(Type databaseModelType, out DatabaseDefinition metadata)
    {
        if (databaseModelType is null)
            throw new ArgumentNullException(nameof(databaseModelType));

        var found = loadedDatabases.TryGetValue(databaseModelType, out var foundMetadata);
        metadata = foundMetadata!;
        return found;
    }

    internal static bool TryAddLoadedDatabase(Type databaseModelType, DatabaseDefinition metadata)
    {
        if (databaseModelType is null)
            throw new ArgumentNullException(nameof(databaseModelType));

        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        return loadedDatabases.TryAdd(databaseModelType, metadata);
    }

    internal static bool TryRemoveLoadedDatabase(Type databaseModelType, out DatabaseDefinition? metadata)
    {
        if (databaseModelType is null)
            throw new ArgumentNullException(nameof(databaseModelType));

        return loadedDatabases.TryRemove(databaseModelType, out metadata);
    }

    public DatabaseDefinition(string name, CsTypeDeclaration csType, string? dbName = null)
    {
        Name = name;
        DbName = dbName ?? Name;
        CsType = csType;
    }

    public string Name { get; private set; }
    public bool IsFrozen { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetName(string name)
    {
        SetNameCore(name);
    }

    internal void SetNameCore(string name)
    {
        ThrowIfFrozen();
        Name = name;
    }

    public string DbName { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetDbName(string dbName)
    {
        SetDbNameCore(dbName);
    }

    internal void SetDbNameCore(string dbName)
    {
        ThrowIfFrozen();
        DbName = dbName;
    }

    public CsTypeDeclaration CsType { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetCsType(CsTypeDeclaration csType)
    {
        SetCsTypeCore(csType);
    }

    internal void SetCsTypeCore(CsTypeDeclaration csType)
    {
        ThrowIfFrozen();
        CsType = csType;
    }

    public CsFileDeclaration? CsFile { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetCsFile(CsFileDeclaration csFile)
    {
        SetCsFileCore(csFile);
    }

    internal void SetCsFileCore(CsFileDeclaration csFile)
    {
        ThrowIfFrozen();
        CsFile = csFile;
    }

    public bool UseCache { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetCache(bool useCache)
    {
        SetCacheCore(useCache);
    }

    internal void SetCacheCore(bool useCache)
    {
        ThrowIfFrozen();
        UseCache = useCache;
    }

    public MetadataCollection<Attribute> Attributes => attributes;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetAttributes(IEnumerable<Attribute> attributes)
    {
        SetAttributesCore(attributes);
    }

    internal void SetAttributesCore(IEnumerable<Attribute> attributes)
    {
        ThrowIfFrozen();
        this.attributes = new MetadataCollection<Attribute>(attributes);
    }

    public SourceTextSpan? SourceSpan { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetSourceSpan(SourceTextSpan sourceSpan)
    {
        SetSourceSpanCore(sourceSpan);
    }

    internal void SetSourceSpanCore(SourceTextSpan sourceSpan)
    {
        ThrowIfFrozen();
        SourceSpan = sourceSpan;
    }

    private Dictionary<Attribute, SourceTextSpan>? attributeSourceSpans;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetAttributeSourceSpan(Attribute attribute, SourceTextSpan sourceSpan)
    {
        SetAttributeSourceSpanCore(attribute, sourceSpan);
    }

    internal void SetAttributeSourceSpanCore(Attribute attribute, SourceTextSpan sourceSpan)
    {
        ThrowIfFrozen();
        attributeSourceSpans ??= new Dictionary<Attribute, SourceTextSpan>(AttributeReferenceEqualityComparer.Instance);
        attributeSourceSpans[attribute] = sourceSpan;
    }

    public SourceLocation? GetSourceLocation()
    {
        if (!CsFile.HasValue)
            return null;

        return new SourceLocation(CsFile.Value, SourceSpan);
    }

    public SourceLocation? GetAttributeSourceLocation(Attribute attribute)
    {
        if (!CsFile.HasValue ||
            attributeSourceSpans is null ||
            !attributeSourceSpans.TryGetValue(attribute, out var sourceSpan))
            return null;

        return new SourceLocation(CsFile.Value, sourceSpan);
    }

    public MetadataCollection<TableModel> TableModels => tableModels;

    public TableModel GetTableModel(Type modelType)
    {
        if (TryGetTableModel(modelType, out var tableModel))
            return tableModel;

        throw new KeyNotFoundException($"No table model registered for model type '{modelType.FullName ?? modelType.Name}'.");
    }

    public bool TryGetTableModel(Type modelType, out TableModel tableModel)
    {
        if (modelType is null)
            throw new ArgumentNullException(nameof(modelType));

        if (tableModelsByModelType is not null &&
            tableModelsByModelType.TryGetValue(modelType, out var exactMatch))
        {
            tableModel = exactMatch;
            return true;
        }

        foreach (var candidate in tableModels)
        {
            if (candidate.Model.IsOfType(modelType))
            {
                tableModel = candidate;
                return true;
            }
        }

        tableModel = null!;
        return false;
    }

    public TableModel GetTableModel(string tableName)
    {
        if (TryGetTableModel(tableName, out var tableModel))
            return tableModel;

        throw new KeyNotFoundException($"No table model registered for database table '{tableName}'.");
    }

    public bool TryGetTableModel(string tableName, out TableModel tableModel)
    {
        if (tableName is null)
            throw new ArgumentNullException(nameof(tableName));

        if (tableModelsByDbName is not null &&
            tableModelsByDbName.TryGetValue(tableName, out tableModel!))
            return true;

        tableModel = null!;
        return false;
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetTableModels(IEnumerable<TableModel> tableModels)
    {
        SetTableModelsCore(tableModels);
    }

    internal void SetTableModelsCore(IEnumerable<TableModel> tableModels)
    {
        ThrowIfFrozen();
        this.tableModels = new MetadataCollection<TableModel>(tableModels);
        RebuildTableModelLookups();
    }

    public MetadataList<(CacheLimitType limitType, long amount)> CacheLimits { get; } = new();
    public MetadataList<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; } = new();
    public MetadataList<(CacheCleanupType cleanupType, long amount)> CacheCleanup { get; } = new();

    internal void Freeze()
    {
        if (IsFrozen)
            return;

        RebuildTableModelLookups();
        IsFrozen = true;

        foreach (var tableModel in tableModels)
            tableModel.Freeze();

        CacheLimits.Freeze();
        IndexCache.Freeze();
        CacheCleanup.Freeze();

        HashSet<RelationDefinition>? frozenRelations = null;
        foreach (var tableModel in tableModels)
        {
            foreach (var index in tableModel.Table.ColumnIndices)
            {
                if (index.RelationParts is null)
                    continue;

                foreach (var part in index.RelationParts)
                {
                    var relation = part.Relation;
                    if (relation is null)
                        continue;

                    frozenRelations ??= [];
                    if (frozenRelations.Add(relation))
                        relation.Freeze();
                }
            }
        }
    }

    private void RebuildTableModelLookups()
    {
        var byModelType = new Dictionary<Type, TableModel>();
        var byDbName = new Dictionary<string, TableModel>(StringComparer.Ordinal);

        foreach (var tableModel in tableModels)
        {
            if (tableModel is null)
                continue;

            var modelType = tableModel.Model.CsType.Type;
            if (modelType is not null && !byModelType.ContainsKey(modelType))
                byModelType.Add(modelType, tableModel);

            if (!byDbName.ContainsKey(tableModel.Table.DbName))
                byDbName.Add(tableModel.Table.DbName, tableModel);
        }

        tableModelsByModelType = byModelType;
        tableModelsByDbName = byDbName;
    }

    private void ThrowIfFrozen() => MetadataMutationGuard.ThrowIfFrozen(IsFrozen, this);
}
