using System.Collections.Generic;

namespace Slim.Metadata
{
    public class Database
    {
        public Database(string name)
        {
            Name = name;
        }

        public string Name { get; set; }
        public List<Table> Tables { get; set; }
    }
}