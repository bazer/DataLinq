using System;

namespace Slim.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class NameAttribute : Attribute
    {
        public NameAttribute(string name)
        {
            Name = name;
        }
        
        public string Name { get; }
    }
}