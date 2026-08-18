using System;
using System.Collections.Generic;

namespace DataLinq.Extensions.Helpers;

internal static class LinqExtensions
{
    internal static string ToJoinedString<T>(this IEnumerable<T> source, string separator = "\n") =>
        string.Join(separator, source);

    internal static IEnumerable<T> Yield<T>(this T item)
    {
#pragma warning disable RCS1165 // Unconstrained type parameter checked for null.
        if (item != null)
#pragma warning restore RCS1165 // Unconstrained type parameter checked for null.
            yield return item;
    }
}
