using System;

namespace DataLinq.Attributes
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class EnumAttribute : Attribute
    {
        public EnumAttribute(params string[] values)
        {
            Values = values;
        }

        public string[] Values { get; }
    }
}