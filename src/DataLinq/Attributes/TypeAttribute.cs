using System;

namespace DataLinq.Attributes
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class TypeAttribute : Attribute
    {
        public TypeAttribute(string name)
        {
            Name = name;
        }

        public TypeAttribute(string name, long length)
        {
            Name = name;
            Length = length;
        }

        public long? Length { get; }
        public string Name { get; }
    }
}