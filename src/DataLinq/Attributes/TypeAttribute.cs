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

        public TypeAttribute(string name, bool unsigned)
        {
            Name = name;
            Unsigned = unsigned;
        }

        public TypeAttribute(string name, long length, bool unsigned)
        {
            Name = name;
            Length = length;
            Unsigned = unsigned;
        }

        public long? Length { get; }
        public string Name { get; }
        public bool Unsigned { get; }
    }
}