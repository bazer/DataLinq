using Slim.Cache;
using Slim.Mutation;
using System;
using System.Collections.Generic;

namespace Slim.Metadata
{
    public class DatabaseMetadata
    {
        public DatabaseMetadata(string name)
        {
            Name = name;
        }

        public DatabaseProvider DatabaseProvider { get; set; }
        public List<Relation> Relations { get; set; }
        public string Name { get; set; }
        public Type CsType { get; set; }
        public List<Table> Tables { get; set; }
        public List<Model> Models { get; set; }
    }
}