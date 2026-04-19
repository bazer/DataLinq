using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DataLinq.DevTools;

public static partial class DotnetOutputAnalyzer
{
    [GeneratedRegex(
        @"^(?<location>.+?): (?<kind>error|warning)( (?<code>[A-Z]{1,4}\d+))?: (?<message>.+?)( \[(?<project>.+?)\])?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticPattern();

    public static DotnetCommandAnalysis Analyze(DotnetCommandType commandType, ExternalCommandResult result)
    {
        var lines = string.Concat(result.StandardOutput, Environment.NewLine, result.StandardError)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var diagnostics = new Dictionary<(DotnetDiagnosticKind Kind, string? Code, string Message), DiagnosticAccumulator>();

        foreach (var line in lines)
        {
            var match = DiagnosticPattern().Match(line);
            if (!match.Success)
                continue;

            var kind = string.Equals(match.Groups["kind"].Value, "warning", StringComparison.OrdinalIgnoreCase)
                ? DotnetDiagnosticKind.Warning
                : DotnetDiagnosticKind.Error;
            var code = GetOptionalValue(match.Groups["code"].Value);
            var message = NormalizeMessage(match.Groups["message"].Value);
            var project = GetOptionalValue(match.Groups["project"].Value);
            var key = (kind, code, message);

            if (!diagnostics.TryGetValue(key, out var accumulator))
            {
                accumulator = new DiagnosticAccumulator(kind, code, message);
                diagnostics[key] = accumulator;
            }

            accumulator.Count++;
            if (!string.IsNullOrWhiteSpace(project))
                accumulator.Projects.Add(project!);
        }

        var errors = diagnostics.Values
            .Where(static x => x.Kind == DotnetDiagnosticKind.Error)
            .OrderBy(static x => x.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.Message, StringComparer.Ordinal)
            .Select(static x => x.ToDiagnostic())
            .ToArray();
        var warnings = diagnostics.Values
            .Where(static x => x.Kind == DotnetDiagnosticKind.Warning)
            .OrderBy(static x => x.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.Message, StringComparer.Ordinal)
            .Select(static x => x.ToDiagnostic())
            .ToArray();
        var failureDetails = commandType == DotnetCommandType.Test
            ? lines
                .Where(static x =>
                    x.StartsWith("failed ", StringComparison.Ordinal) ||
                    x.Contains("[Test Failure]", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : [];

        var failureCategory = result.ExitCode == 0 && errors.Length == 0
            ? DotnetFailureCategory.None
            : ClassifyFailure(commandType, lines, errors);

        return new DotnetCommandAnalysis(
            DistinctErrorCount: errors.Length,
            DistinctWarningCount: warnings.Length,
            Errors: errors,
            Warnings: warnings,
            FailureDetails: failureDetails,
            FailureCategory: failureCategory,
            FailureSummary: CreateFailureSummary(failureCategory));
    }

    private static DotnetFailureCategory ClassifyFailure(
        DotnetCommandType commandType,
        IReadOnlyList<string> lines,
        IReadOnlyList<DotnetDiagnostic> errors)
    {
        var combined = string.Join(Environment.NewLine, lines);

        if (combined.Contains("WorkloadAutoImportPropsLocator", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("default SDK resolver failed to resolve SDK", StringComparison.OrdinalIgnoreCase))
        {
            return DotnetFailureCategory.SdkResolver;
        }

        if (combined.Contains("Failed to read NuGet.Config due to unauthorized access", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("NuGet.Config", StringComparison.OrdinalIgnoreCase) &&
            combined.Contains("Access to the path", StringComparison.OrdinalIgnoreCase))
        {
            return DotnetFailureCategory.NugetConfigAccess;
        }

        if (errors.Any(static x => string.Equals(x.Code, "NU1101", StringComparison.OrdinalIgnoreCase)) ||
            combined.Contains("Unable to find package", StringComparison.OrdinalIgnoreCase))
        {
            return DotnetFailureCategory.MissingPackages;
        }

        if (errors.Any(static x => string.Equals(x.Code, "NU1301", StringComparison.OrdinalIgnoreCase)) ||
            combined.Contains("Unable to load the service index", StringComparison.OrdinalIgnoreCase))
        {
            return DotnetFailureCategory.NugetSourceAccess;
        }

        if (commandType == DotnetCommandType.Test &&
            lines.Any(static x => x.StartsWith("failed ", StringComparison.Ordinal)))
        {
            return DotnetFailureCategory.TestFailures;
        }

        if (errors.Any(static x => x.Code?.StartsWith("CS", StringComparison.OrdinalIgnoreCase) == true))
            return DotnetFailureCategory.Compiler;

        return DotnetFailureCategory.Unknown;
    }

    private static string? CreateFailureSummary(DotnetFailureCategory category) =>
        category switch
        {
            DotnetFailureCategory.None => null,
            DotnetFailureCategory.SdkResolver => "The installed .NET SDK host looks broken or incomplete.",
            DotnetFailureCategory.NugetConfigAccess => "NuGet configuration resolution is pointing at an inaccessible user-profile path.",
            DotnetFailureCategory.NugetSourceAccess => "Package source access failed, usually because the network is blocked or the source is unavailable.",
            DotnetFailureCategory.MissingPackages => "The required packages are not available in the selected cache/source set.",
            DotnetFailureCategory.Compiler => "Compilation failed with source diagnostics.",
            DotnetFailureCategory.TestFailures => "The test run completed but one or more tests failed.",
            _ => "The command failed, but the wrapper could not classify the root cause more specifically."
        };

    private static string NormalizeMessage(string message) =>
        Regex.Replace(message.Trim(), @"\s+", " ");

    private static string? GetOptionalValue(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private sealed class DiagnosticAccumulator
    {
        public DiagnosticAccumulator(DotnetDiagnosticKind kind, string? code, string message)
        {
            Kind = kind;
            Code = code;
            Message = message;
        }

        public DotnetDiagnosticKind Kind { get; }
        public string? Code { get; }
        public string Message { get; }
        public HashSet<string> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Count { get; set; }

        public DotnetDiagnostic ToDiagnostic() =>
            new(
                Kind,
                Code,
                Message,
                Projects.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                Count);
    }
}
