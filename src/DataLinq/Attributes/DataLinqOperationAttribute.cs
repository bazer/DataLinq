using System;

namespace DataLinq.Attributes;

/// <summary>Generates owning and participating entry points for a private Core or CoreAsync service method.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DataLinqOperationAttribute : Attribute
{
    /// <summary>Explicitly selects native-result-aware owning completion. The default treats every return value as ordinary data.</summary>
    public bool ResultAware { get; set; }
}

/// <summary>Designates the stored unit-of-work factory used by a service with a traditional constructor.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class DataLinqFactoryAttribute : Attribute;
