using System;
using System.Reflection;

namespace Slim.Metadata
{
    public enum PropertyType
    {
        Value,
        Relation
    }

    public class Property
    {
        public object[] Attributes { get; set; }
        public Column Column { get; set; }
        public string CsName { get; set; }
        public bool CsNullable { get; set; }
        public Type CsType { get; set; }
        public string CsTypeName { get; set; }
        public Model Model { get; set; }
        public PropertyInfo PropertyInfo { get; set; }
        public PropertyType Type { get; set; }
        public RelationPart RelationPart { get; set; }
    }
}