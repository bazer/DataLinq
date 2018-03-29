using System;
using System.Collections.Generic;
using System.Reflection;

namespace Slim.Metadata
{
    public class Column
    {
        public List<Constraint> Constraints { get; set; } = new List<Constraint>();
        public string CsName { get; set; }
        public bool CsNullable { get; set; }
        public Type CsType { get; set; }
        public string CsTypeName { get; set; }
        public string DbName { get; set; }
        public string DbType { get; set; }
        public string Default { get; set; }
        public long? Length { get; set; }
        public bool Nullable { get; set; }
        public bool PrimaryKey { get; set; }
        public PropertyInfo Property { get; set; }
        public Table Table { get; set; }
    }
}