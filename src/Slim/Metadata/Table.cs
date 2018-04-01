using System.Collections.Generic;

namespace Slim.Metadata
{
    public enum TableType
    {
        Table,
        View
    }

    public class Table
    {
        public List<Column> Columns { get; set; }
        public Database Database { get; set; }
        public string DbName { get; set; }
        public Model Model { get; set; }
        public TableType Type { get; set; }
    }
}