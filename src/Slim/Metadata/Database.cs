using Slim.Cache;
using System;
using System.Collections.Generic;

namespace Slim.Metadata
{
    public class Database
    {
        public Database(string name)
        {
            Name = name;
        }

        public DatabaseProvider DatabaseProvider { get; set; }
        public List<Relation> Relations { get; set; }
        public string Name { get; set; }
        public Type CsType { get; set; }
        public List<Table> Tables { get; set; }
        public List<Model> Models { get; set; }
        public DatabaseCache Cache { get; set; }
    }
}