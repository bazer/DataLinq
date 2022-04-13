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

        public TypeAttribute(string name, bool signed)
        {
            Name = name;
            Signed = signed;
        }

        public TypeAttribute(string name, long length, bool signed)
        {
            Name = name;
            Length = length;
            Signed = signed;
        }

        public long? Length { get; }
        public string Name { get; }
        public bool? Signed { get; }
    }
}