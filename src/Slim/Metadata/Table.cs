using System.Collections.Generic;
using System.Linq;
using Slim.Cache;

namespace Slim.Metadata
{
    public enum TableType
    {
        Table,
        View
    }

    public class Table
    {
        private List<Column> primaryKeyColumns;

        public TableCache Cache { get; set; }
        public List<Column> Columns { get; set; }
        public Database Database { get; set; }
        public string DbName { get; set; }
        public Model Model { get; set; }

        public List<Column> PrimaryKeyColumns =>
            primaryKeyColumns ?? (primaryKeyColumns = Columns.Where(x => x.PrimaryKey).ToList());

        public TableType Type { get; set; }
    }
}