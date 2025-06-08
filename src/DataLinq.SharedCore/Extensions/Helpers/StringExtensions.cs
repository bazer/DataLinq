using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DataLinq.Extensions.Helpers;

internal static class StringExtensions
{
    // This regular expression splits the string by underscores or hyphens.
    private static readonly Regex WordBoundaryRegex = new Regex("[-_]");

    /// <summary>
    /// Converts a snake_case or kebab-case string to PascalCase.
    /// </summary>
    internal static string ToPascalCase(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Split the string by word boundaries and capitalize the first letter of each part.
        var parts = WordBoundaryRegex.Split(input.ToLowerInvariant());

        return string.Concat(parts.Select(part =>
            FirstCharToUpper(part)));
    }
     
    //https://stackoverflow.com/a/4405876
    /// <summary>
    /// Converts the first character of a string to uppercase using invariant culture.
    /// </summary>
    internal static string FirstCharToUpper(this string input) =>
        input switch
        {
            null => throw new ArgumentNullException(nameof(input)),
            "" => throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input)),
            _ => string.Concat(char.ToUpperInvariant(input[0]), input.Substring(1))
        };
}
