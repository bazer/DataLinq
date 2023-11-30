using System;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = true, AllowMultiple = false)]
public sealed class ViewAttribute : Attribute
{
    public ViewAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
}