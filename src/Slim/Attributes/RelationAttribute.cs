using System;

namespace Slim.Attributes
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public sealed class RelationAttribute : Attribute
    {
        public RelationAttribute(string table, string column)
        {
            Column = column;
            Table = table;
        }

        public string Column { get; }
        public string Table { get; }
    }
}