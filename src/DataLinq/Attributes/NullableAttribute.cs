using System;

namespace DataLinq.Attributes
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class NullableAttribute : Attribute
    {
        public NullableAttribute()
        {

        }
    }
}
