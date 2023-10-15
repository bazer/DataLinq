using DataLinq.Extensions;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace DataLinq.Metadata
{
    public struct DatabaseColumnType
    {
        public DatabaseType DatabaseType { get; set; }
        public string Name { get; set; }
        public long? Length { get; set; }
        public int? Decimals { get; set; }
        public bool? Signed { get; set; }

        public override string ToString()
        {
            return $"{Name} ({Length}) [{DatabaseType}]";
        }
    }

    public class Column
    {
        public string DbName { get; set; }
        public List<DatabaseColumnType> DbTypes { get; set; } = new List<DatabaseColumnType>();
        public int Index { get; set; }
        public bool ForeignKey { get; set; }
        public bool PrimaryKey { get; set; }
        public bool Unique => ColumnIndices.Any(x => x.Characteristic == Attributes.IndexCharacteristic.Unique);
        public bool AutoIncrement { get; set; }
        public bool Nullable { get; set; }
        public List<RelationPart> RelationParts { get; set; } = new List<RelationPart>();
        public IEnumerable<ColumnIndex> ColumnIndices => Table.ColumnIndices.Where(x => x.Columns.Contains(this));
        public TableMetadata Table { get; set; }
        public ValueProperty ValueProperty { get; set; }

        public override string ToString()
        {
            return $"{Table.DbName}.{DbName} ({DbTypes.ToJoinedString(", ")})";
        }
    }
}