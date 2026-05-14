using System;

namespace DataLinq.CLI;

internal static class ConsoleDiagnosticWriter
{
    private const string ErrorPrefix = "Error:";
    private const string WarningPrefix = "Warning:";
    private const ConsoleColor ErrorColor = ConsoleColor.Red;
    private const ConsoleColor WarningColor = ConsoleColor.Yellow;

    public static void WriteFailure(object? failure) =>
        WritePrefixedLines(ErrorPrefix, ErrorColor, failure?.ToString() ?? "");

    internal static string FormatFailureText(object? failure) =>
        FormatPrefixedLines(ErrorPrefix, failure?.ToString() ?? "");

    public static void WriteLogLine(string message)
    {
        if (TryGetDiagnosticPrefix(message, out var prefix, out var color))
        {
            WriteColoredPrefix(prefix, color);
            Console.WriteLine(message[prefix.Length..]);
            return;
        }

        Console.WriteLine(message);
    }

    internal static bool TryGetDiagnosticPrefix(string message, out string prefix, out ConsoleColor color)
    {
        if (message.StartsWith(WarningPrefix, StringComparison.OrdinalIgnoreCase))
        {
            prefix = WarningPrefix;
            color = WarningColor;
            return true;
        }

        if (message.StartsWith(ErrorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            prefix = ErrorPrefix;
            color = ErrorColor;
            return true;
        }

        prefix = "";
        color = default;
        return false;
    }

    private static void WritePrefixedLines(string prefix, ConsoleColor color, string message)
    {
        var lines = message.ReplaceLineEndings("\n").Split('\n');
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                Console.WriteLine();
                continue;
            }

            WriteColoredPrefix(prefix, color);
            Console.WriteLine($" {line}");
        }
    }

    private static string FormatPrefixedLines(string prefix, string message)
    {
        var lines = message.ReplaceLineEndings("\n").Split('\n');
        return string.Join(
            Environment.NewLine,
            Array.ConvertAll(lines, line => line.Length == 0 ? "" : $"{prefix} {line}")) + Environment.NewLine;
    }

    private static void WriteColoredPrefix(string prefix, ConsoleColor color)
    {
        if (Console.IsOutputRedirected)
        {
            Console.Write(prefix);
            return;
        }

        var previousColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Console.Write(prefix);
        }
        finally
        {
            Console.ForegroundColor = previousColor;
        }
    }
}
