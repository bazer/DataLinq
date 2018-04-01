using System.Collections.Generic;

namespace Slim.Metadata
{
    public class Column
    {
        public string DbName { get; set; }
        public string DbType { get; set; }
        public bool ForeignKey { get; set; }
        public long? Length { get; set; }
        public bool Nullable { get; set; }
        public bool PrimaryKey { get; set; }
        public List<RelationPart> RelationParts { get; set; } = new List<RelationPart>();
        public List<Property> RelationProperties { get; set; } = new List<Property>();
        public Table Table { get; set; }
        public Property ValueProperty { get; set; }
    }
}