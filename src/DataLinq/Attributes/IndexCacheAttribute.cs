using System;

namespace DataLinq.Attributes;

public enum IndexCacheType
{
    None,
    All,
    MaxAmountRows,
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public sealed class IndexCacheAttribute : Attribute
{
    public IndexCacheAttribute(IndexCacheType type, int? amount = null)
    {
        Type = type;
        Amount = amount;
    }

    public IndexCacheType Type { get; }
    public int? Amount { get; }
}