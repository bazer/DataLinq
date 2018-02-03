using System;
using System.Collections.Generic;
using System.Text;

namespace Slim.Attributes
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class ForeignKeyAttribute : Attribute
    {
        public ForeignKeyAttribute(Type table = null, bool nullable = false)
        {
            Table = table;
            Nullable = nullable;
        }

        public Type Table { get; }
        public bool Nullable { get; }
    }

}
