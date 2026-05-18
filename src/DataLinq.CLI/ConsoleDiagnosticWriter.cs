using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;

namespace DataLinq.CLI;

internal static class ConsoleDiagnosticWriter
{
    private const string ErrorSeverity = "error";
    private const string WarningSeverity = "warning";
    private const string LegacyErrorPrefix = "Error:";
    private const string LegacyWarningPrefix = "Warning:";
    private const ConsoleColor ErrorColor = ConsoleColor.Red;
    private const ConsoleColor WarningColor = ConsoleColor.Yellow;

    public static SecretRedactor Redactor { get; set; } = new();

    public static void WriteError(string message) =>
        WriteDiagnosticLines(DataLinqDiagnosticSeverity.Error, null, message);

    public static void WriteError(string? code, string message) =>
        WriteDiagnosticLines(DataLinqDiagnosticSeverity.Error, code, message);

    public static void WriteWarning(string message) =>
        WriteDiagnosticLines(DataLinqDiagnosticSeverity.Warning, null, message);

    public static void WriteWarning(string? code, string message) =>
        WriteDiagnosticLines(DataLinqDiagnosticSeverity.Warning, code, message);

    public static void WriteFailure(object? failure)
    {
        if (failure is IDLOptionFailure optionFailure)
        {
            WriteIssues(DataLinqDiagnosticIssue.FromFailure(optionFailure));
            return;
        }

        WriteError(failure?.ToString() ?? "");
    }

    public static void WriteIssues(IEnumerable<DataLinqDiagnosticIssue> issues)
    {
        var issueList = issues.ToList();
        if (issueList.Count == 0)
        {
            WriteError("Unknown DataLinq failure.");
            return;
        }

        foreach (var issue in issueList)
            WriteIssue(issue, TryReadSourceText);
    }

    internal static string FormatFailureText(object? failure)
    {
        if (failure is IDLOptionFailure optionFailure)
            return FormatIssuesText(DataLinqDiagnosticIssue.FromFailure(optionFailure));

        return FormatDiagnosticLines(DataLinqDiagnosticSeverity.Error, null, failure?.ToString() ?? "");
    }

    internal static string FormatIssuesText(
        IEnumerable<DataLinqDiagnosticIssue> issues,
        Func<SourceLocation, string?>? sourceTextProvider = null)
    {
        var issueList = issues.ToList();
        if (issueList.Count == 0)
            return FormatDiagnosticLines(DataLinqDiagnosticSeverity.Error, null, "Unknown DataLinq failure.");

        return string.Concat(issueList.Select(issue =>
            string.Join(Environment.NewLine, FormatIssueLines(issue, sourceTextProvider)) + Environment.NewLine));
    }

    public static void WriteLogLine(string message)
    {
        message = Redactor.Redact(message);
        if (TryGetDiagnosticPrefix(message, out var prefix, out _))
        {
            var diagnosticMessage = message[prefix.Length..].TrimStart();
            if (prefix.Equals(LegacyWarningPrefix, StringComparison.OrdinalIgnoreCase))
                WriteWarning(diagnosticMessage);
            else
                WriteError(diagnosticMessage);

            return;
        }

        Console.WriteLine(message);
    }

    internal static bool TryGetDiagnosticPrefix(string message, out string prefix, out ConsoleColor color)
    {
        if (message.StartsWith(LegacyWarningPrefix, StringComparison.OrdinalIgnoreCase))
        {
            prefix = LegacyWarningPrefix;
            color = WarningColor;
            return true;
        }

        if (message.StartsWith(LegacyErrorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            prefix = LegacyErrorPrefix;
            color = ErrorColor;
            return true;
        }

        prefix = "";
        color = default;
        return false;
    }

    private static void WriteDiagnosticLines(
        DataLinqDiagnosticSeverity severity,
        string? code,
        string message)
    {
        var lines = Redactor.Redact(message).ReplaceLineEndings("\n").Split('\n');
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                Console.Error.WriteLine();
                continue;
            }

            WriteDiagnosticLine(severity, code, line);
        }
    }

    private static string FormatDiagnosticLines(
        DataLinqDiagnosticSeverity severity,
        string? code,
        string message)
    {
        var lines = Redactor.Redact(message).ReplaceLineEndings("\n").Split('\n');
        return string.Join(
            Environment.NewLine,
            Array.ConvertAll(lines, line => line.Length == 0 ? "" : FormatDiagnosticLine(severity, code, line))) +
            Environment.NewLine;
    }

    private static void WriteIssue(
        DataLinqDiagnosticIssue issue,
        Func<SourceLocation, string?>? sourceTextProvider)
    {
        WriteDiagnosticLine(
            issue.Severity,
            CodeFor(issue.FailureType),
            Redactor.Redact(issue.Message),
            FormatIssueLocation(issue, sourceTextProvider));

        foreach (var contextMessage in issue.ContextMessages.Where(static message => !string.IsNullOrWhiteSpace(message)))
            Console.Error.WriteLine($"  context: {Redactor.Redact(contextMessage)}");
    }

    private static IReadOnlyList<string> FormatIssueLines(
        DataLinqDiagnosticIssue issue,
        Func<SourceLocation, string?>? sourceTextProvider)
    {
        var lines = new List<string>
        {
            FormatDiagnosticLine(
                issue.Severity,
                CodeFor(issue.FailureType),
                Redactor.Redact(issue.Message),
                FormatIssueLocation(issue, sourceTextProvider))
        };

        lines.AddRange(issue.ContextMessages
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Select(message => $"  context: {Redactor.Redact(message)}"));

        return lines;
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

    private static void WriteDiagnosticLine(
        DataLinqDiagnosticSeverity severity,
        string? code,
        string message,
        string? location = null)
    {
        if (!string.IsNullOrWhiteSpace(location))
            Console.Error.Write($"{Redactor.Redact(location)}: ");

        WriteColoredSeverity(SeverityText(severity), ColorFor(severity));

        var normalizedCode = NormalizeCode(code);
        Console.Error.WriteLine(string.IsNullOrWhiteSpace(normalizedCode)
            ? $": {Redactor.Redact(message)}"
            : $" {normalizedCode}: {Redactor.Redact(message)}");
    }

    private static string FormatDiagnosticLine(
        DataLinqDiagnosticSeverity severity,
        string? code,
        string message,
        string? location = null)
    {
        var diagnosticText = FormatCodeMessage(SeverityText(severity), NormalizeCode(code), Redactor.Redact(message));
        return string.IsNullOrWhiteSpace(location)
            ? diagnosticText
            : $"{Redactor.Redact(location)}: {diagnosticText}";
    }

    private static string SeverityText(DataLinqDiagnosticSeverity severity) =>
        severity == DataLinqDiagnosticSeverity.Warning
            ? WarningSeverity
            : ErrorSeverity;

    private static ConsoleColor ColorFor(DataLinqDiagnosticSeverity severity) =>
        severity == DataLinqDiagnosticSeverity.Warning
            ? WarningColor
            : ErrorColor;

    private static string? CodeFor(DLFailureType failureType) =>
        failureType == DLFailureType.Unspecified
            ? null
            : failureType.ToString();

    private static string? NormalizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) || code.Equals(DLFailureType.Unspecified.ToString(), StringComparison.Ordinal)
            ? null
            : code;

    private static string FormatCodeMessage(string severity, string? code, string message) =>
        string.IsNullOrWhiteSpace(code)
            ? $"{severity}: {message}"
            : $"{severity} {code}: {message}";

    private static void WriteColoredSeverity(string severity, ConsoleColor color)
    {
        if (Console.IsErrorRedirected)
        {
            Console.Error.Write(severity);
            return;
        }

        var previousColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Console.Error.Write(severity);
        }
        finally
        {
            Console.ForegroundColor = previousColor;
        }
    }
}
