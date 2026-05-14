using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;

namespace DataLinq.CLI;

internal static class ConsoleDiagnosticWriter
{
    private const string ErrorPrefix = "Error:";
    private const string WarningPrefix = "Warning:";
    private const ConsoleColor ErrorColor = ConsoleColor.Red;
    private const ConsoleColor WarningColor = ConsoleColor.Yellow;

    public static void WriteFailure(object? failure)
    {
        if (failure is IDLOptionFailure optionFailure)
        {
            WriteIssues(DataLinqDiagnosticIssue.FromFailure(optionFailure));
            return;
        }

        WritePrefixedLines(ErrorPrefix, ErrorColor, failure?.ToString() ?? "");
    }

    public static void WriteIssues(IEnumerable<DataLinqDiagnosticIssue> issues) =>
        WritePrefixedLines(ErrorPrefix, ErrorColor, FormatIssuesMessage(issues, TryReadSourceText));

    internal static string FormatFailureText(object? failure)
    {
        if (failure is IDLOptionFailure optionFailure)
            return FormatIssuesText(DataLinqDiagnosticIssue.FromFailure(optionFailure));

        return FormatPrefixedLines(ErrorPrefix, failure?.ToString() ?? "");
    }

    internal static string FormatIssuesText(
        IEnumerable<DataLinqDiagnosticIssue> issues,
        Func<SourceLocation, string?>? sourceTextProvider = null) =>
        FormatPrefixedLines(ErrorPrefix, FormatIssuesMessage(issues, sourceTextProvider));

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

    private static string FormatIssuesMessage(
        IEnumerable<DataLinqDiagnosticIssue> issues,
        Func<SourceLocation, string?>? sourceTextProvider)
    {
        var issueList = issues.ToList();
        if (issueList.Count == 0)
            return "Unknown DataLinq failure.";

        var lines = new List<string>();
        foreach (var issue in issueList)
        {
            var location = FormatIssueLocation(issue, sourceTextProvider);
            var issueText = string.IsNullOrWhiteSpace(location)
                ? $"[{issue.FailureType}] {issue.Message}"
                : $"{location}: [{issue.FailureType}] {issue.Message}";

            lines.Add(issueText);

            foreach (var contextMessage in issue.ContextMessages.Where(static message => !string.IsNullOrWhiteSpace(message)))
                lines.Add($"  context: {contextMessage}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatIssueLocation(
        DataLinqDiagnosticIssue issue,
        Func<SourceLocation, string?>? sourceTextProvider)
    {
        if (!issue.SourceLocation.HasValue)
            return issue.ObjectPath ?? "";

        var sourceLocation = issue.SourceLocation.Value;
        var sourceText = sourceTextProvider?.Invoke(sourceLocation);
        return SourceLocationFormatter.Format(sourceLocation, sourceText);
    }

    private static string? TryReadSourceText(SourceLocation sourceLocation)
    {
        try
        {
            return File.Exists(sourceLocation.File.FullPath)
                ? File.ReadAllText(sourceLocation.File.FullPath)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
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
