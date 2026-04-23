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

    public DatabaseDefinition(string name, CsTypeDeclaration csType, string? dbName = null)
    {
        Name = name;
        DbName = dbName ?? Name;
        CsType = csType;
    }

    public string Name { get; private set; }
    public void SetName(string name) => Name = name;
    public string DbName { get; private set; }
    public void SetDbName(string dbName) => DbName = dbName;
    public CsTypeDeclaration CsType { get; private set; }
    public void SetCsType(CsTypeDeclaration csType) => CsType = csType;
    public CsFileDeclaration? CsFile { get; private set; }
    public void SetCsFile(CsFileDeclaration csFile) => CsFile = csFile;
    public bool UseCache { get; private set; }
    public void SetCache(bool useCache) => UseCache = useCache;
    public Attribute[] Attributes { get; private set; } = [];
    public void SetAttributes(IEnumerable<Attribute> attributes) => Attributes = attributes.ToArray();
    public SourceTextSpan? SourceSpan { get; private set; }
    public void SetSourceSpan(SourceTextSpan sourceSpan) => SourceSpan = sourceSpan;
    private readonly Dictionary<Attribute, SourceTextSpan> attributeSourceSpans = new(AttributeReferenceEqualityComparer.Instance);
    public void SetAttributeSourceSpan(Attribute attribute, SourceTextSpan sourceSpan) => attributeSourceSpans[attribute] = sourceSpan;

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

    public TableModel[] TableModels { get; private set; } = [];
    public void SetTableModels(IEnumerable<TableModel> tableModels) => TableModels = tableModels.ToArray();
    public List<(CacheLimitType limitType, long amount)> CacheLimits { get; private set; } = [];
    public List<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; private set; } = [];
    public List<(CacheCleanupType cleanupType, long amount)> CacheCleanup { get; private set; } = [];
}
