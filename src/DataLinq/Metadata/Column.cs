using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Cache;
using DataLinq.Instances;

namespace DataLinq.Metadata
{
    public class Column
    {
        public string DbName { get; set; }
        //public string CsName { get; set; }
        public string DbType { get; set; }
        public int Index { get; set; }
        public bool ForeignKey { get; set; }
        public long? Length { get; set; }
        public bool Nullable { get; set; }
        public bool PrimaryKey { get; set; }
        public bool Unique { get; set; }
        public bool AutoIncrement { get; set; }
        public bool? Signed { get; set; }
        public List<RelationPart> RelationParts { get; set; } = new List<RelationPart>();
        public IEnumerable<ColumnIndex> ColumnIndices => Table.ColumnIndices.Where(x => x.Columns.Contains(this));
        public List<Property> RelationProperties { get; set; } = new List<Property>();
        public TableMetadata Table { get; set; }
        public Property ValueProperty { get; set; }
        //public ConcurrentDictionary<object, PrimaryKeys[]> Index { get; } = new ConcurrentDictionary<object, PrimaryKeys[]>();

        public override string ToString()
        {
            return $"{Table.DbName}.{DbName} ({DbType})";
        }
    }
}