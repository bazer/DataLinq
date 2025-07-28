using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DataLinq.Extensions.Helpers;

internal static class StringExtensions
{
    // This regular expression splits the string by underscores or hyphens.
    private static readonly Regex WordBoundaryRegex = new Regex("[-_ ]");

    /// <summary>
    /// Converts a snake_case, kebab-case, or PascalCase string to camelCase.
    /// </summary>
    internal static string ToCamelCase(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // First, check for the special case of an all-uppercase string (like an acronym).
        // This ensures "ID" becomes "id" and "SKU" becomes "sku".
        if (input.ToUpperInvariant() == input)
            return input.ToLowerInvariant();

        // First, convert the string to PascalCase to correctly handle all delimiters and acronyms.
        string pascalCase = input.ToPascalCase();

        // Now, simply make the first character lowercase.
        return char.ToLowerInvariant(pascalCase[0]) + pascalCase.Substring(1);
    }

    /// <summary>
    /// Converts a snake_case or kebab-case string to PascalCase.
    /// </summary>
    internal static string ToPascalCase(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Split the string by word boundaries and capitalize the first letter of each part.
        var parts = WordBoundaryRegex.Split(input);

        return string.Concat(parts.Select(part =>
            FirstCharToUpper(part)));
    }

    //https://stackoverflow.com/a/4405876
    /// <summary>
    /// Converts the first character of a string to uppercase using invariant culture.
    /// </summary>
    internal static string FirstCharToUpper(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return string.Concat(char.ToUpperInvariant(input[0]), input.Substring(1));
    }

    internal static bool IsFirstCharUpper(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        // Check if the first character is uppercase using invariant culture
        return char.IsUpper(input[0]);
    }
}
