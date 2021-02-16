using Slim.Cache;
using Slim.Mutation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Slim.Metadata
{
    public class DatabaseMetadata
    {
        //public static ConcurrentDictionary<string, DatabaseMetadata> LoadedDatabases { get; } = new ConcurrentDictionary<string, DatabaseMetadata>();

        public string NameOrAlias => string.IsNullOrEmpty(Alias) ? Name : Alias;

        public DatabaseMetadata(string name, string alias = null)
        {
            Name = name;
            Alias = alias;

            //if (!LoadedDatabases.ContainsKey(NameOrAlias) && !LoadedDatabases.TryAdd(NameOrAlias, this))
            //    throw new Exception($"Failed while adding database with name {NameOrAlias} to global dictionary.");
        }

        public DatabaseProvider DatabaseProvider { get; set; }
        public List<Relation> Relations { get; set; }
        public string Name { get; set; }
        public string Alias { get; set; }
        public Type CsType { get; set; }
        public List<Table> Tables { get; set; }
        public List<Model> Models { get; set; }
    }
}