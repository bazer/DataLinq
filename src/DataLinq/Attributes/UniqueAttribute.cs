using System;

namespace DataLinq.Attributes
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public sealed class UniqueAttribute : Attribute
    {
        public UniqueAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}