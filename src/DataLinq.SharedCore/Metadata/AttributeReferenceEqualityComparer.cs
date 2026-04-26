using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DataLinq.Metadata;

internal sealed class AttributeReferenceEqualityComparer : IEqualityComparer<Attribute>
{
    public static AttributeReferenceEqualityComparer Instance { get; } = new();

    public bool Equals(Attribute? x, Attribute? y) => ReferenceEquals(x, y);

    public int GetHashCode(Attribute obj) => RuntimeHelpers.GetHashCode(obj);
}
