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

        public List<Constraint> Constraints { get; set; }
        public string Name { get; set; }
        public Type SystemType { get; set; }
        public List<Table> Tables { get; set; }
    }
}