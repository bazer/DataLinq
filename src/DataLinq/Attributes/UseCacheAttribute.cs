using System;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class UseCacheAttribute : Attribute
{
    public UseCacheAttribute(bool useCache = true)
    {
        UseCache = useCache;
    }

    public bool UseCache { get; }
}