using System;
using System.Collections.Generic;
using System.Text;

namespace Slim.Attributes
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class NullableAttribute : Attribute
    {
        public NullableAttribute()
        {

        }
    }
}
