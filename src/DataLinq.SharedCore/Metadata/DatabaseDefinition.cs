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
    private Attribute[] attributes = [];
    private TableModel[] tableModels = [];

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

    public Attribute[] Attributes => attributes.ToArray();

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetAttributes(IEnumerable<Attribute> attributes)
    {
        SetAttributesCore(attributes);
    }

    internal void SetAttributesCore(IEnumerable<Attribute> attributes)
    {
        ThrowIfFrozen();
        this.attributes = attributes.ToArray();
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

    private readonly Dictionary<Attribute, SourceTextSpan> attributeSourceSpans = new(AttributeReferenceEqualityComparer.Instance);

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetAttributeSourceSpan(Attribute attribute, SourceTextSpan sourceSpan)
    {
        SetAttributeSourceSpanCore(attribute, sourceSpan);
    }

    internal void SetAttributeSourceSpanCore(Attribute attribute, SourceTextSpan sourceSpan)
    {
        ThrowIfFrozen();
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
        if (!CsFile.HasValue || !attributeSourceSpans.TryGetValue(attribute, out var sourceSpan))
            return null;

        return new SourceLocation(CsFile.Value, sourceSpan);
    }

    public TableModel[] TableModels => tableModels.ToArray();

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetTableModels(IEnumerable<TableModel> tableModels)
    {
        SetTableModelsCore(tableModels);
    }

    internal void SetTableModelsCore(IEnumerable<TableModel> tableModels)
    {
        ThrowIfFrozen();
        this.tableModels = tableModels.ToArray();
    }

    public MetadataList<(CacheLimitType limitType, long amount)> CacheLimits { get; } = new();
    public MetadataList<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; } = new();
    public MetadataList<(CacheCleanupType cleanupType, long amount)> CacheCleanup { get; } = new();

    internal void Freeze()
    {
        if (IsFrozen)
            return;

        IsFrozen = true;

        foreach (var tableModel in tableModels)
            tableModel.Freeze();

        CacheLimits.Freeze();
        IndexCache.Freeze();
        CacheCleanup.Freeze();

        foreach (var relation in tableModels
            .SelectMany(tableModel => tableModel.Table.ColumnIndices)
            .SelectMany(index => index.RelationParts ?? Enumerable.Empty<RelationPart>())
            .Select(part => part.Relation)
            .Where(relation => relation is not null)
            .Distinct())
        {
            relation.Freeze();
        }
    }

    private void ThrowIfFrozen() => MetadataMutationGuard.ThrowIfFrozen(IsFrozen, this);
}
