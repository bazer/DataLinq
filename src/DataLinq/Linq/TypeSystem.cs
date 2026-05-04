using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace DataLinq.Linq;

/// <summary>
/// Provides utility methods for working with types in a LINQ-to-SQL context,
/// particularly for determining the element types of sequences.
/// </summary>
internal static class TypeSystem
{
    /// <summary>
    /// Determines the element type of a sequence given a <see cref="Type"/> that represents the sequence.
    /// </summary>
    /// <param name="seqType">The type of the sequence.</param>
    /// <returns>The element type of the sequence. If the sequence type is not an <see cref="IEnumerable"/>,
    /// or if it is a string, returns the sequence type itself.</returns>
    internal static Type GetElementType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
        Type seqType)
    {
        return FindIEnumerableElementType(seqType) ?? seqType;
    }

    /// <summary>
    /// Finds the <see cref="IEnumerable{T}"/> interface for a given sequence type.
    /// </summary>
    /// <param name="seqType">The type of the sequence.</param>
    /// <returns>The <see cref="IEnumerable{T}"/> interface type if found; otherwise, null.</returns>
    /// <remarks>
    /// This method checks if the provided type is a string or null, an array, a generic type,
    /// or implements any interfaces that are assignable from <see cref="IEnumerable{T}"/>,
    /// and recursively checks the base type if necessary.
    /// </remarks>
    private static Type? FindIEnumerableElementType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
        Type seqType)
    {
        if (seqType == null || seqType == typeof(string))
            return null;

        if (seqType.IsArray)
            return seqType.GetElementType();

        if (IsGenericEnumerable(seqType))
            return seqType.GetGenericArguments()[0];

        Type[] ifaces = seqType.GetInterfaces();

        if (ifaces != null && ifaces.Length > 0)
        {
            foreach (Type iface in ifaces)
            {
                if (IsGenericEnumerable(iface))
                    return iface.GetGenericArguments()[0];
            }
        }

        // Recursively check the base type if it's not the root object type.
        if (seqType.BaseType != null && seqType.BaseType != typeof(object))
        {
            return FindIEnumerableElementType(seqType.BaseType);
        }

        return null;
    }

    private static bool IsGenericEnumerable(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>);
}
