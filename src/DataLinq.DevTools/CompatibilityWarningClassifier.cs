using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.DevTools;

public static class CompatibilityWarningClassifier
{
    public static CompatibilityWarningSummary Summarize(
        CompatibilityTargetDefinition target,
        IReadOnlyList<DotnetDiagnostic> warnings)
    {
        var diagnostics = warnings
            .Select(warning => new CompatibilityWarningDiagnostic(
                Classify(target, warning),
                warning.Code,
                warning.Message,
                warning.Projects,
                warning.Count))
            .OrderBy(static x => x.Owner)
            .ThenBy(static x => x.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.Message, StringComparer.Ordinal)
            .ToArray();

        var owners = diagnostics
            .GroupBy(static x => x.Owner)
            .Select(static group => new CompatibilityWarningOwnerSummary(
                group.Key,
                group.Count(),
                group.Sum(static x => x.Count)))
            .OrderBy(static x => x.Owner)
            .ToArray();

        return new CompatibilityWarningSummary(
            diagnostics.Length,
            diagnostics.Sum(static x => x.Count),
            owners,
            diagnostics);
    }

    public static CompatibilityWarningOwner Classify(
        CompatibilityTargetDefinition target,
        DotnetDiagnostic warning)
    {
        var combined = string.Join(
            " ",
            warning.Code ?? string.Empty,
            warning.Message,
            string.Join(" ", warning.Projects));

        if (target.Kind == CompatibilityTargetKind.Wasm &&
            ContainsAny(combined, "no-aot", "interpreter", "RunAOTCompilation=false"))
        {
            return CompatibilityWarningOwner.UnsupportedNoAot;
        }

        if (ContainsAny(
            combined,
            ".nuget",
            "NuGetFallbackFolder",
            "Remotion.Linq",
            "SQLitePCLRaw",
            "Microsoft.CodeAnalysis",
            "System.CommandLine",
            "Spectre.Console"))
        {
            return CompatibilityWarningOwner.ThirdPartyDependency;
        }

        if (warning.Projects.Any(static project => IsDataLinqProjectPath(project)) ||
            ContainsAny(combined, "DataLinq."))
        {
            return CompatibilityWarningOwner.DataLinqOwned;
        }

        if (warning.Code?.StartsWith("WASM", StringComparison.OrdinalIgnoreCase) == true ||
            ContainsAny(
                combined,
                "WebAssembly",
                "wasm",
                "MarshalingPInvokeScanner",
                "Emscripten",
                "Microsoft.NET.Sdk.WebAssembly"))
        {
            return CompatibilityWarningOwner.SdkOrWebAssembly;
        }

        if (warning.Code?.StartsWith("NETSDK", StringComparison.OrdinalIgnoreCase) == true ||
            warning.Code?.StartsWith("IL", StringComparison.OrdinalIgnoreCase) == true ||
            ContainsAny(combined, "Microsoft.NET.Sdk", "ILLink"))
        {
            return CompatibilityWarningOwner.SdkOrWebAssembly;
        }

        if (target.IsWebAssembly)
            return CompatibilityWarningOwner.SdkOrWebAssembly;

        return CompatibilityWarningOwner.Other;
    }

    public static CompatibilityFailureClassification ClassifyFailure(
        CompatibilityTargetDefinition target,
        DotnetCommandResult result)
    {
        if (result.ProcessResult.ExitCode == 0)
            return CompatibilityFailureClassification.None;

        var combined = string.Join(
            Environment.NewLine,
            result.ProcessResult.StandardOutput,
            result.ProcessResult.StandardError,
            result.Analysis.FailureSummary ?? string.Empty);

        if (ContainsAny(
            combined,
            "wasm-tools",
            "WebAssembly workload",
            "MarshalingPInvokeScanner",
            "ResolveWasmOutputs",
            "Microsoft.NET.Sdk.BlazorWebAssembly",
            "Microsoft.NET.Sdk.WebAssembly",
            "workload is not installed",
            "Platform linker not found",
            "nativeaot-prerequisites",
            "Desktop Development for C++",
            "RunAOTCompilation"))
        {
            return CompatibilityFailureClassification.SdkOrWebAssemblyToolchain;
        }

        if (target.Kind == CompatibilityTargetKind.Wasm)
            return CompatibilityFailureClassification.UnsupportedNoAot;

        if (result.Analysis.FailureCategory == DotnetFailureCategory.TrimAnalysis &&
            ContainsAny(combined, "Remotion.Linq", @"remotion.linq\"))
        {
            return CompatibilityFailureClassification.RemotionDependency;
        }

        if (result.Analysis.FailureCategory is
            DotnetFailureCategory.Compiler or
            DotnetFailureCategory.TrimAnalysis or
            DotnetFailureCategory.MissingPackages or
            DotnetFailureCategory.NugetSourceAccess or
            DotnetFailureCategory.NugetConfigAccess or
            DotnetFailureCategory.SdkResolver)
        {
            return CompatibilityFailureClassification.Dotnet;
        }

        return CompatibilityFailureClassification.Unknown;
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static bool IsDataLinqProjectPath(string value)
    {
        var fileName = System.IO.Path.GetFileName(value);
        return fileName.StartsWith("DataLinq", StringComparison.OrdinalIgnoreCase) &&
               fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    }
}
