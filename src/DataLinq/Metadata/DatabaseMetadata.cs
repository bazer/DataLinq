using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DataLinq.Attributes;

namespace DataLinq.Metadata;

public class DatabaseMetadata
{
    public static ConcurrentDictionary<Type, DatabaseMetadata> LoadedDatabases { get; } = new();

    public DatabaseMetadata(string name, Type? csType = null, string? csTypeName = null, string? dbName = null)
    {
        Name = name;
        CsType = csType;
        CsTypeName = csTypeName ?? CsType?.Name ?? name;
        DbName = dbName ?? name;
    }

    public string Name { get; internal set; }
    public string DbName { get; }
    public Type? CsType { get; }
    public string CsTypeName { get; }
    public Attribute[] Attributes { get; set; }
    public List<TableModelMetadata> TableModels { get; set; } = [];
    public List<(CacheLimitType limitType, long amount)> CacheLimits { get; internal set; } = [];
    public List<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; set; } = [];
    public List<(CacheCleanupType cleanupType, long amount)> CacheCleanup { get; internal set; } = [];
    public bool UseCache { get; internal set; }
}