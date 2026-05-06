using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Metadata;

public class DatabaseDefinition : IDefinition
{
    public static ConcurrentDictionary<Type, DatabaseDefinition> LoadedDatabases { get; } = new();
    private Attribute[] attributes = [];
    private TableModel[] tableModels = [];

    public DatabaseDefinition(string name, CsTypeDeclaration csType, string? dbName = null)
    {
        Name = name;
        DbName = dbName ?? Name;
        CsType = csType;
    }

    public string Name { get; private set; }
    public bool IsFrozen { get; private set; }

    public void SetName(string name)
    {
        ThrowIfFrozen();
        Name = name;
    }

    public string DbName { get; private set; }

    public void SetDbName(string dbName)
    {
        ThrowIfFrozen();
        DbName = dbName;
    }

    public CsTypeDeclaration CsType { get; private set; }

    public void SetCsType(CsTypeDeclaration csType)
    {
        ThrowIfFrozen();
        CsType = csType;
    }

    public CsFileDeclaration? CsFile { get; private set; }

    public void SetCsFile(CsFileDeclaration csFile)
    {
        ThrowIfFrozen();
        CsFile = csFile;
    }

    public bool UseCache { get; private set; }

    public void SetCache(bool useCache)
    {
        ThrowIfFrozen();
        UseCache = useCache;
    }

    public Attribute[] Attributes => attributes.ToArray();

    public void SetAttributes(IEnumerable<Attribute> attributes)
    {
        ThrowIfFrozen();
        this.attributes = attributes.ToArray();
    }

    public SourceTextSpan? SourceSpan { get; private set; }

    public void SetSourceSpan(SourceTextSpan sourceSpan)
    {
        ThrowIfFrozen();
        SourceSpan = sourceSpan;
    }

    private readonly Dictionary<Attribute, SourceTextSpan> attributeSourceSpans = new(AttributeReferenceEqualityComparer.Instance);

    public void SetAttributeSourceSpan(Attribute attribute, SourceTextSpan sourceSpan)
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

    public void SetTableModels(IEnumerable<TableModel> tableModels)
    {
        ThrowIfFrozen();
        this.tableModels = tableModels.ToArray();
    }

    public List<(CacheLimitType limitType, long amount)> CacheLimits { get; private set; } = [];
    public List<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; private set; } = [];
    public List<(CacheCleanupType cleanupType, long amount)> CacheCleanup { get; private set; } = [];

    internal void Freeze()
    {
        if (IsFrozen)
            return;

        IsFrozen = true;

        foreach (var tableModel in tableModels)
            tableModel.Freeze();

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
