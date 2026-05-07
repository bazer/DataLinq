using System;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class EnumAttribute : Attribute
{
    private readonly string[] values;

    public EnumAttribute(params string[] values)
    {
        this.values = values is null ? [] : [.. values];
    }

    public string[] Values => [.. values];
}
