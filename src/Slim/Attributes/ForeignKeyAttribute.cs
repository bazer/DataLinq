using System;
using System.Collections.Generic;
using System.Text;

namespace Slim.Attributes
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public sealed class ForeignKeyAttribute : Attribute
    {
        public ForeignKeyAttribute(string table, string column, string name)
        {
            Table = table;
            Column = column;
            Name = name;
        }

        public string Table { get; }
        public string Column { get; }
        public string Name { get; }
    }

}
