using System;
using System.Linq;
using DataLinq.DevTools;
using Spectre.Console;

namespace DataLinq.Dev.CLI;

internal static class CommandRenderer
{
    public static void Render(DotnetCommandResult result, DotnetOutputMode outputMode, bool printBinaryLogOnSuccess)
    {
        if (outputMode is DotnetOutputMode.Raw or DotnetOutputMode.Diagnostic)
        {
            WriteRawOutput(result);
            WriteArtifacts(result, includeBinaryLogOnSuccess: true);
            return;
        }

        if (result.ProcessResult.ExitCode == 0)
        {
            var message = $"OK {result.CommandType.ToString().ToLowerInvariant()} {Markup.Escape(result.DisplayTarget)} " +
                          $"({result.Analysis.DistinctWarningCount} warnings, {result.ProcessResult.Duration.TotalSeconds:0.0}s)";

            AnsiConsole.MarkupLine($"[green]{message}[/]");

            if (outputMode == DotnetOutputMode.Summary || printBinaryLogOnSuccess)
                WriteArtifacts(result, includeBinaryLogOnSuccess: printBinaryLogOnSuccess);

            return;
        }

        AnsiConsole.MarkupLine(
            $"[red]FAIL[/] {result.CommandType.ToString().ToLowerInvariant()} {Markup.Escape(result.DisplayTarget)} " +
            $"({result.ProcessResult.Duration.TotalSeconds:0.0}s)");

        if (!string.IsNullOrWhiteSpace(result.Analysis.FailureSummary))
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(result.Analysis.FailureSummary!)}[/]");

        if (result.Analysis.Errors.Count > 0)
            WriteDiagnostics("Errors", result.Analysis.Errors);

        if ((outputMode == DotnetOutputMode.Summary || outputMode == DotnetOutputMode.Failures) &&
            result.Analysis.FailureDetails.Count > 0)
        {
            WriteFailureDetails(result.Analysis.FailureDetails);
        }

        if (outputMode == DotnetOutputMode.Failures &&
            result.Analysis.Errors.Count == 0 &&
            result.Analysis.FailureDetails.Count == 0)
        {
            WriteRawOutput(result);
        }

        if (outputMode == DotnetOutputMode.Summary && result.Analysis.Warnings.Count > 0)
            WriteDiagnostics("Warnings", result.Analysis.Warnings);

        if (outputMode == DotnetOutputMode.Quiet &&
            result.Analysis.FailureCategory == DotnetFailureCategory.TestFailures &&
            result.Analysis.FailureDetails.Count > 0)
        {
            WriteFailureDetails(result.Analysis.FailureDetails);
        }

        WriteArtifacts(result, includeBinaryLogOnSuccess: true);
    }

    public static void WriteRawOutput(DotnetCommandResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ProcessResult.StandardOutput))
            Console.WriteLine(result.ProcessResult.StandardOutput.TrimEnd());

        if (!string.IsNullOrWhiteSpace(result.ProcessResult.StandardError))
            Console.Error.WriteLine(result.ProcessResult.StandardError.TrimEnd());
    }

    private static void WriteDiagnostics(string title, System.Collections.Generic.IReadOnlyList<DotnetDiagnostic> diagnostics)
    {
        Console.WriteLine();
        AnsiConsole.Write(new Rule($"[yellow]{title}[/]"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Code")
            .AddColumn("Message")
            .AddColumn("Projects")
            .AddColumn("Count");

        foreach (var diagnostic in diagnostics)
        {
            var projects = diagnostic.Projects.Count switch
            {
                0 => "-",
                <= 2 => string.Join(", ", diagnostic.Projects.Select(PathTrimmer.TrimProjectName)),
                _ => $"{string.Join(", ", diagnostic.Projects.Take(2).Select(PathTrimmer.TrimProjectName))}, +{diagnostic.Projects.Count - 2} more"
            };

            table.AddRow(
                diagnostic.Code ?? "-",
                diagnostic.Message,
                projects,
                diagnostic.Count.ToString());
        }

        AnsiConsole.Write(table);
    }

    private static void WriteFailureDetails(System.Collections.Generic.IReadOnlyList<string> failureDetails)
    {
        Console.WriteLine();
        AnsiConsole.Write(new Rule("[red]Failures[/]"));

        foreach (var line in failureDetails)
            Console.WriteLine(line);
    }

    private static void WriteArtifacts(DotnetCommandResult result, bool includeBinaryLogOnSuccess)
    {
        Console.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Raw log:[/] {Markup.Escape(result.RawLogPath)}");

        if (!string.IsNullOrWhiteSpace(result.BinaryLogPath) &&
            (result.ProcessResult.ExitCode != 0 || includeBinaryLogOnSuccess))
        {
            AnsiConsole.MarkupLine($"[grey]Binary log:[/] {Markup.Escape(result.BinaryLogPath!)}");
        }
    }

    private static class PathTrimmer
    {
        public static string TrimProjectName(string value)
        {
            try
            {
                return System.IO.Path.GetFileName(value);
            }
            catch
            {
                return value;
            }
        }
    }
}
