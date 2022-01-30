using DataLinq.Cache;
using DataLinq.Mutation;
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

            //if (!LoadedDatabases.ContainsKey(NameOrAlias) && !LoadedDatabases.TryAdd(NameOrAlias, this))
            //    throw new Exception($"Failed while adding database with name {NameOrAlias} to global dictionary.");
        }

        //public DatabaseProvider DatabaseProvider { get; set; }
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