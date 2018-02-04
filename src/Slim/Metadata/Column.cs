using System.Collections.Generic;

namespace Slim.Metadata
{
    public class Column
    {
        public List<Constraint> Constraints { get; set; } = new List<Constraint>();
        public bool CsNullable { get; set; }
        public string CsType { get; set; }
        public string DbType { get; set; }
        public string Default { get; set; }
        public long? Length { get; set; }
        public string Name { get; set; }
        public bool Nullable { get; set; }
        public bool PrimaryKey { get; set; }
        public Table Table { get; set; }
    }
}