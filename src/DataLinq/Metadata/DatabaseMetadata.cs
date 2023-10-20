using DataLinq.Attributes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DataLinq.Metadata
{
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

        public string Name { get; set; }
        public string DbName { get; set; }
        public Type CsType { get; set; }
        public string CsTypeName { get; set; }
        public Attribute[] Attributes { get; set; }
        public List<TableModelMetadata> TableModels { get; set; } = new();
        public List<(CacheLimitType limitType, long amount)> CacheLimits { get; set; } = new();
        public List<(CacheCleanupType limitType, long amount)> CacheCleanup { get; set; } = new();
        public bool UseCache { get; set; }
    }
}