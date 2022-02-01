using DataLinq.Attributes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DataLinq.Metadata
{
    public class DatabaseMetadata
    {
        public static ConcurrentDictionary<Type, DatabaseMetadata> LoadedDatabases { get; } = new();

        public string NameOrAlias => string.IsNullOrEmpty(Alias) ? Name : Alias;

        public DatabaseMetadata(string name, string alias = null)
        {
            Name = name;
            Alias = alias;
        }

        public List<Relation> Relations { get; set; }
        public string Name { get; set; }
        public string Alias { get; set; }
        public Type CsType { get; set; }
        public List<TableMetadata> Tables { get; set; }
        public List<Model> Models { get; set; }
        public List<(CacheLimitType limitType, long amount)> CacheLimits { get; set; } = new();
        public bool UseCache { get; set; }
    }
}