using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Metadata;

public class DatabaseDefinition
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
    public bool UseCache { get; private set; }
    public void SetCache(bool useCache) => UseCache = useCache;
    public Attribute[] Attributes { get; private set; } = [];
    public void SetAttributes(IEnumerable<Attribute> attributes) => Attributes = attributes.ToArray();
    public TableModel[] TableModels { get; private set; } = [];
    public void SetTableModels(IEnumerable<TableModel> tableModels) => TableModels = tableModels.ToArray();
    public List<(CacheLimitType limitType, long amount)> CacheLimits { get; private set; } = [];
    public List<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; private set; } = [];
    public List<(CacheCleanupType cleanupType, long amount)> CacheCleanup { get; private set; } = [];
}