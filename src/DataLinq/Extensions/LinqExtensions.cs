using System;
using System.Collections.Generic;

namespace DataLinq.Extensions
{
    public static class LinqExtensions
    {
        public static string ToJoinedString<T>(this IEnumerable<T> source, string separator = "\n") =>
            string.Join(separator, source);

        //https://stackoverflow.com/a/11463800
        public static IEnumerable<List<T>> SplitList<T>(this List<T> locations, int nSize = 30)
        {
            for (int i = 0; i < locations.Count; i += nSize)
            {
                yield return locations.GetRange(i, Math.Min(nSize, locations.Count - i));
            }
        }

        public static IEnumerable<T> Yield<T>(this T item)
        {
#pragma warning disable RCS1165 // Unconstrained type parameter checked for null.
            if (item != null)
#pragma warning restore RCS1165 // Unconstrained type parameter checked for null.
                yield return item;
        }
    }
}
