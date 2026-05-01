using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DataLinq.Extensions.Helpers;

internal static class StringExtensions
{
    // This regular expression splits provider identifiers into C# identifier words.
    private static readonly Regex WordBoundaryRegex = new Regex(@"[^\p{L}\p{Nd}]+");
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach",
        "goto", "if", "implicit", "in", "int", "interface", "internal",
        "is", "lock", "long", "namespace", "new", "null", "object",
        "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void",
        "volatile", "while"
    };

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
            return EnsureCSharpIdentifier(input.ToLowerInvariant());

        // First, convert the string to PascalCase to correctly handle all delimiters and acronyms.
        string pascalCase = input.ToPascalCase();

        // Now, simply make the first character lowercase.
        return EnsureCSharpIdentifier(char.ToLowerInvariant(pascalCase[0]) + pascalCase.Substring(1));
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

    internal static string ToCSharpIdentifier(this string input, bool pascalCase)
    {
        if (string.IsNullOrEmpty(input))
            return "_";

        var hasWordBoundary = WordBoundaryRegex.IsMatch(input);
        var candidate = pascalCase && (!input.IsFirstCharUpper() || hasWordBoundary)
            ? input.ToPascalCase()
            : input;

        return EnsureCSharpIdentifier(candidate);
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

    private static string EnsureCSharpIdentifier(string input)
    {
        var builder = new StringBuilder(input.Length);
        var lastWasUnderscore = false;

        foreach (var character in input)
        {
            if (IsCSharpIdentifierPart(character))
            {
                builder.Append(character);
                lastWasUnderscore = character == '_';
                continue;
            }

            if (!lastWasUnderscore)
            {
                builder.Append('_');
                lastWasUnderscore = true;
            }
        }

        var candidate = builder.ToString().Trim('_');
        if (candidate.Length == 0)
            candidate = "_";

        if (!IsCSharpIdentifierStart(candidate[0]))
            candidate = "_" + candidate;

        return CSharpKeywords.Contains(candidate)
            ? "_" + candidate
            : candidate;
    }

    private static bool IsCSharpIdentifierStart(char character) =>
        character == '_' || char.IsLetter(character);

    private static bool IsCSharpIdentifierPart(char character) =>
        character == '_' || char.IsLetterOrDigit(character);
}
