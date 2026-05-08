using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DataLinq.DevTools;

public static class CompatibilityTargetCatalog
{
    private static readonly CompatibilityTargetDefinition[] Phase8CTargets =
    [
        new(
            Name: "native-aot",
            Kind: CompatibilityTargetKind.NativeAot,
            DisplayName: "Native AOT smoke",
            ProjectRelativePath: @"src\DataLinq.AotSmoke\DataLinq.AotSmoke.csproj",
            TargetFramework: "net10.0",
            RequiresRuntimeIdentifier: true,
            IsWebAssembly: false,
            ExecutableName: "DataLinq.AotSmoke",
            PublishProperties: ["PublishAot=true", "SelfContained=true"]),
        new(
            Name: "trimmed",
            Kind: CompatibilityTargetKind.Trimmed,
            DisplayName: "Trimmed smoke",
            ProjectRelativePath: @"src\DataLinq.TrimSmoke\DataLinq.TrimSmoke.csproj",
            TargetFramework: "net10.0",
            RequiresRuntimeIdentifier: true,
            IsWebAssembly: false,
            ExecutableName: "DataLinq.TrimSmoke",
            PublishProperties: ["PublishTrimmed=true", "SelfContained=true"]),
        new(
            Name: "wasm",
            Kind: CompatibilityTargetKind.Wasm,
            DisplayName: "Blazor WebAssembly no-AOT smoke",
            ProjectRelativePath: @"src\DataLinq.BlazorWasm\DataLinq.BlazorWasm.csproj",
            TargetFramework: "net10.0",
            RequiresRuntimeIdentifier: false,
            IsWebAssembly: true,
            ExecutableName: "DataLinq.BlazorWasm",
            PublishProperties: ["RunAOTCompilation=false"]),
        new(
            Name: "wasm-aot",
            Kind: CompatibilityTargetKind.WasmAot,
            DisplayName: "Blazor WebAssembly AOT smoke",
            ProjectRelativePath: @"src\DataLinq.BlazorWasm\DataLinq.BlazorWasm.csproj",
            TargetFramework: "net10.0",
            RequiresRuntimeIdentifier: false,
            IsWebAssembly: true,
            ExecutableName: "DataLinq.BlazorWasm",
            PublishProperties: ["RunAOTCompilation=true"])
    ];

    public static IReadOnlyList<CompatibilityTargetDefinition> GetTargets(
        string targetSet,
        IReadOnlyList<CompatibilityTargetKind> targetKinds)
    {
        if (!string.Equals(targetSet, "phase8c", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported compatibility report target '{targetSet}'. Use phase8c.");

        var requested = targetKinds.Count == 0
            ? Phase8CTargets.Select(static x => x.Kind).ToHashSet()
            : targetKinds.ToHashSet();

        return Phase8CTargets.Where(target => requested.Contains(target.Kind)).ToArray();
    }

    public static IReadOnlyList<CompatibilityTargetKind> ParseTargetKinds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value.Trim(), "all", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value.Trim(), "phase8c", StringComparison.OrdinalIgnoreCase))
        {
            return Phase8CTargets.Select(static x => x.Kind).ToArray();
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseTargetKind)
            .Distinct()
            .ToArray();
    }

    public static string DefaultRuntimeIdentifier()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => "x64"
        };

        if (OperatingSystem.IsWindows())
            return $"win-{architecture}";

        if (OperatingSystem.IsMacOS())
            return $"osx-{architecture}";

        return $"linux-{architecture}";
    }

    private static CompatibilityTargetKind ParseTargetKind(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "aot" or "native-aot" or "nativeaot" => CompatibilityTargetKind.NativeAot,
            "trim" or "trimmed" => CompatibilityTargetKind.Trimmed,
            "wasm" or "no-aot-wasm" or "wasm-no-aot" => CompatibilityTargetKind.Wasm,
            "wasm-aot" or "aot-wasm" or "blazor-wasm-aot" => CompatibilityTargetKind.WasmAot,
            _ => throw new InvalidOperationException(
                $"Unsupported compatibility report publish target '{value}'. Use aot, trim, wasm, wasm-aot, all, or phase8c.")
        };
}
