using System;
using System.Collections.Generic;
using System.Text;

namespace Slim.Extensions
{
    public static class LinqExtensions
    {
        public static string ToJoinedString<T>(this IEnumerable<T> source, string separator = "\n") =>
            string.Join(separator, source);
    }
}
