using System;

namespace DataLinq.Query;

/// <summary>Quotes a single SQL identifier component, never a SQL expression.</summary>
public static class SqlIdentifier
{
    public static string Quote(string name, string delimiter)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrEmpty(delimiter);
        return delimiter + name.Replace(delimiter, delimiter + delimiter, StringComparison.Ordinal) + delimiter;
    }

    public static void Append(Sql sql, string name, string delimiter)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrEmpty(delimiter);
        sql.AddText(delimiter);
        sql.AddText(name.Replace(delimiter, delimiter + delimiter, StringComparison.Ordinal));
        sql.AddText(delimiter);
    }

    internal static string Unquote(string name, string delimiter) =>
        name.Length >= 2 * delimiter.Length &&
        name.StartsWith(delimiter, StringComparison.Ordinal) &&
        name.EndsWith(delimiter, StringComparison.Ordinal)
            ? name.Substring(delimiter.Length, name.Length - 2 * delimiter.Length)
                .Replace(delimiter + delimiter, delimiter, StringComparison.Ordinal)
            : name;
}
