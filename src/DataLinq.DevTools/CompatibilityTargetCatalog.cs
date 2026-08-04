using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DataLinq.DevTools;

public static class CompatibilityTargetCatalog
{
    public const string HistoricalTargetSet = "phase8c";
    public const string CurrentTargetSet = "v0.9";

    private static readonly CompatibilityTargetDefinition[] Phase8CTargets =
    [
        CreateSqliteNativeAot("native-aot", "Native AOT smoke"),
        CreateSqliteTrimmed("trimmed", "Trimmed smoke"),
        CreateSqliteWasm("wasm", "Blazor WebAssembly no-AOT smoke", aot: false),
        CreateSqliteWasm("wasm-aot", "Blazor WebAssembly AOT smoke", aot: true)
    ];

    private static readonly CompatibilityTargetDefinition[] Version09Targets =
    [
        CreateSqliteNativeAot("sqlite-native-aot", "SQLite Native AOT smoke"),
        CreateSqliteTrimmed("sqlite-trimmed", "SQLite trimmed smoke"),
        CreateSqliteWasm("sqlite-wasm-no-aot", "SQLite WebAssembly no-AOT smoke", aot: false),
        CreateSqliteWasm("sqlite-wasm-aot", "SQLite WebAssembly AOT smoke", aot: true),
        CreateMemoryNativeAot(),
        CreateMemoryTrimmed(),
        CreateMemoryWasm(aot: false),
        CreateMemoryWasm(aot: true)
    ];

    public static IReadOnlyList<CompatibilityTargetDefinition> GetTargets(
        string targetSet,
        string? targetSelectors = null)
    {
        var targets = GetTargetSet(targetSet);
        if (string.IsNullOrWhiteSpace(targetSelectors))
            return targets;

        var requestedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectAll = false;
        foreach (var selector in targetSelectors.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IsAllSelector(selector, targetSet))
            {
                selectAll = true;
                continue;
            }

            if (TryParseKind(selector, out var kind))
            {
                var matches = targets.Where(target => target.Kind == kind).ToArray();
                if (matches.Length == 0)
                    throw new InvalidOperationException(CreateUnsupportedSelectorMessage(selector, targetSet, targets));

                requestedNames.UnionWith(matches.Select(static target => target.Name));
                continue;
            }

            if (TryParseRuntimeGraph(selector, out var runtimeGraph))
            {
                var matches = targets.Where(target => target.RuntimeGraph == runtimeGraph).ToArray();
                if (matches.Length == 0)
                    throw new InvalidOperationException(CreateUnsupportedSelectorMessage(selector, targetSet, targets));

                requestedNames.UnionWith(matches.Select(static target => target.Name));
                continue;
            }

            var exactTarget = targets.FirstOrDefault(
                target => string.Equals(target.Name, selector, StringComparison.OrdinalIgnoreCase));
            if (exactTarget is not null)
            {
                requestedNames.Add(exactTarget.Name);
                continue;
            }

            throw new InvalidOperationException(CreateUnsupportedSelectorMessage(selector, targetSet, targets));
        }

        if (selectAll)
            return targets;

        if (requestedNames.Count == 0)
            throw new InvalidOperationException("--targets must contain at least one target id or mode alias.");

        return targets.Where(target => requestedNames.Contains(target.Name)).ToArray();
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

    private static CompatibilityTargetDefinition[] GetTargetSet(string targetSet)
    {
        if (string.Equals(targetSet, HistoricalTargetSet, StringComparison.OrdinalIgnoreCase))
            return Phase8CTargets;

        if (string.Equals(targetSet, CurrentTargetSet, StringComparison.OrdinalIgnoreCase))
            return Version09Targets;

        throw new InvalidOperationException(
            $"Unsupported compatibility report target set '{targetSet}'. Use {HistoricalTargetSet} or {CurrentTargetSet}.");
    }

    private static bool IsAllSelector(string selector, string targetSet) =>
        string.Equals(selector, "all", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(selector, targetSet, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseKind(string value, out CompatibilityTargetKind kind)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "aot":
            case "native-aot":
            case "nativeaot":
                kind = CompatibilityTargetKind.NativeAot;
                return true;
            case "trim":
            case "trimmed":
                kind = CompatibilityTargetKind.Trimmed;
                return true;
            case "wasm":
            case "no-aot-wasm":
            case "wasm-no-aot":
                kind = CompatibilityTargetKind.Wasm;
                return true;
            case "wasm-aot":
            case "aot-wasm":
            case "blazor-wasm-aot":
                kind = CompatibilityTargetKind.WasmAot;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool TryParseRuntimeGraph(string value, out CompatibilityRuntimeGraph runtimeGraph)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "sqlite":
                runtimeGraph = CompatibilityRuntimeGraph.SQLite;
                return true;
            case "memory":
                runtimeGraph = CompatibilityRuntimeGraph.Memory;
                return true;
            default:
                runtimeGraph = default;
                return false;
        }
    }

    private static string CreateUnsupportedSelectorMessage(
        string selector,
        string targetSet,
        IReadOnlyList<CompatibilityTargetDefinition> targets)
    {
        var targetIds = string.Join(", ", targets.Select(static target => target.Name));
        return
            $"Unsupported compatibility report selector '{selector}' for target set '{targetSet}'. " +
            $"Use an exact target id ({targetIds}), aot, trim, wasm, wasm-aot, sqlite, memory, all, or {targetSet}.";
    }

    private static CompatibilityTargetDefinition CreateSqliteNativeAot(string name, string displayName) =>
        new(
            Name: name,
            Kind: CompatibilityTargetKind.NativeAot,
            RuntimeGraph: CompatibilityRuntimeGraph.SQLite,
            DisplayName: displayName,
            ProjectRelativePath: @"src\DataLinq.AotSmoke\DataLinq.AotSmoke.csproj",
            TargetFramework: "net10.0",
            RequiresRuntimeIdentifier: true,
            IsWebAssembly: false,
            ExecutableName: "DataLinq.AotSmoke",
            PublishProperties: []);

    private static CompatibilityTargetDefinition CreateSqliteTrimmed(string name, string displayName) =>
        new(
            Name: name,
            Kind: CompatibilityTargetKind.Trimmed,
            RuntimeGraph: CompatibilityRuntimeGraph.SQLite,
            DisplayName: displayName,
            ProjectRelativePath: @"src\DataLinq.TrimSmoke\DataLinq.TrimSmoke.csproj",
            TargetFramework: "net10.0",
            RequiresRuntimeIdentifier: true,
            IsWebAssembly: false,
            ExecutableName: "DataLinq.TrimSmoke",
            PublishProperties: []);

    private static CompatibilityTargetDefinition CreateSqliteWasm(
        string name,
        string displayName,
        bool aot) =>
        new(
            Name: name,
            Kind: aot ? CompatibilityTargetKind.WasmAot : CompatibilityTargetKind.Wasm,
            RuntimeGraph: CompatibilityRuntimeGraph.SQLite,
            DisplayName: displayName,
            ProjectRelativePath: @"src\DataLinq.BlazorWasm\DataLinq.BlazorWasm.csproj",
            TargetFramework: "net10.0",
            RequiresRuntimeIdentifier: false,
            IsWebAssembly: true,
            ExecutableName: "DataLinq.BlazorWasm",
            PublishProperties: [$"RunAOTCompilation={aot.ToString().ToLowerInvariant()}"]);

    private static CompatibilityTargetDefinition CreateMemoryNativeAot() =>
        new(
            Name: "memory-native-aot",
            Kind: CompatibilityTargetKind.NativeAot,
            RuntimeGraph: CompatibilityRuntimeGraph.Memory,
            DisplayName: "Memory Native AOT smoke",
            ProjectRelativePath: @"src\DataLinq.Memory.AotSmoke\DataLinq.Memory.AotSmoke.csproj",
            TargetFramework: "net10.0",
            RequiresRuntimeIdentifier: true,
            IsWebAssembly: false,
            ExecutableName: "DataLinq.Memory.AotSmoke",
            PublishProperties: []);

    private static CompatibilityTargetDefinition CreateMemoryTrimmed() =>
        new(
            Name: "memory-trimmed",
            Kind: CompatibilityTargetKind.Trimmed,
            RuntimeGraph: CompatibilityRuntimeGraph.Memory,
            DisplayName: "Memory trimmed smoke",
            ProjectRelativePath: @"src\DataLinq.Memory.TrimSmoke\DataLinq.Memory.TrimSmoke.csproj",
            TargetFramework: "net10.0",
            RequiresRuntimeIdentifier: true,
            IsWebAssembly: false,
            ExecutableName: "DataLinq.Memory.TrimSmoke",
            PublishProperties: []);

    private static CompatibilityTargetDefinition CreateMemoryWasm(bool aot) =>
        new(
            Name: aot ? "memory-wasm-aot" : "memory-wasm-no-aot",
            Kind: aot ? CompatibilityTargetKind.WasmAot : CompatibilityTargetKind.Wasm,
            RuntimeGraph: CompatibilityRuntimeGraph.Memory,
            DisplayName: aot ? "Memory WebAssembly AOT smoke" : "Memory WebAssembly no-AOT smoke",
            ProjectRelativePath: @"src\DataLinq.Memory.BlazorWasm\DataLinq.Memory.BlazorWasm.csproj",
            TargetFramework: "net10.0",
            RequiresRuntimeIdentifier: false,
            IsWebAssembly: true,
            ExecutableName: "DataLinq.Memory.BlazorWasm",
            PublishProperties: [$"RunAOTCompilation={aot.ToString().ToLowerInvariant()}"]);
}
