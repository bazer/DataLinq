using System;
using System.Collections.Generic;

namespace DataLinq.DevTools;

public enum CompatibilityTargetKind
{
    NativeAot,
    Trimmed,
    Wasm,
    WasmAot
}

public enum CompatibilityRuntimeGraph
{
    SQLite,
    Memory
}

public enum CompatibilityCommandStatus
{
    Succeeded,
    Failed,
    Skipped,
    NotApplicable,
    Unsupported
}

public enum CompatibilityFailureClassification
{
    None,
    UnsupportedNoAot,
    SdkOrWebAssemblyToolchain,
    BrowserTelemetryContract,
    PayloadInspection,
    PackageProvenance,
    ProductRegression,
    RemotionDependency,
    Dotnet,
    Unknown
}

public enum CompatibilityFailureDisposition
{
    None,
    Product,
    Environment,
    Unsupported
}

public enum CompatibilityWarningOwner
{
    DataLinqOwned,
    ThirdPartyDependency,
    SdkOrWebAssembly,
    UnsupportedNoAot,
    Other
}

public sealed record CompatibilityTargetDefinition(
    string Name,
    CompatibilityTargetKind Kind,
    CompatibilityRuntimeGraph RuntimeGraph,
    string DisplayName,
    string ProjectRelativePath,
    string TargetFramework,
    bool RequiresRuntimeIdentifier,
    bool IsWebAssembly,
    string ExecutableName,
    IReadOnlyList<string> PublishProperties);

public sealed record CompatibilityReportOptions(
    string RepositoryRoot,
    ToolingProfile Profile,
    string TargetSet,
    string? TargetSelectors,
    string Configuration,
    string RuntimeIdentifier,
    int LargestFileCount,
    bool NoRestore,
    bool SkipSmoke,
    long? TotalSizeWarningBytes,
    long? SymbolExcludedSizeWarningBytes,
    int? FileCountWarning,
    bool FailOnBannedPayload,
    bool FailOnThresholdWarnings,
    bool ContinueOnPublishFailure,
    bool CleanIntermediateOutputs,
    bool UseReleaseThresholds)
{
    public string? PackageDirectory { get; init; }

    public string? PackageVersion { get; init; }
}

public sealed record CompatibilityReportInvocation(
    ToolingProfile Profile,
    bool NoRestore,
    bool SkipSmoke,
    bool CleanIntermediateOutputs,
    bool UseReleaseThresholds,
    bool FailOnBannedPayload,
    bool FailOnThresholdWarnings,
    bool ContinueOnPublishFailure,
    int LargestFileCount,
    long? TotalSizeWarningBytes,
    long? SymbolExcludedSizeWarningBytes,
    int? FileCountWarning);

public sealed record CompatibilitySizeReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string RepositoryRoot,
    string TargetSet,
    IReadOnlyList<string> SelectedTargetIds,
    int ExpectedTargetCount,
    bool IsFullTargetSet,
    string Configuration,
    string RuntimeIdentifier,
    string DotnetSdkVersion,
    string ReportDirectory,
    IReadOnlyList<CompatibilityTargetReport> Targets,
    CompatibilityReportSummary Summary)
{
    public CompatibilityDependencySource DependencySource { get; init; } =
        CompatibilityDependencySource.ProjectReferences;

    public CompatibilityReportInvocation? Invocation { get; init; }

    public CompatibilityPackageInput? PackageInput { get; init; }

    public string? PackageNugetConfigPath { get; init; }

    public string? PackageCacheDirectory { get; init; }

    public string RunnerStartRepositoryCommit { get; init; } = "unknown";

    public bool RunnerStartWorkingTreeDirty { get; init; }

    public string RunnerStartStatusSha256 { get; init; } = "unknown";

    public string RunnerRepositoryCommit { get; init; } = "unknown";

    public bool RunnerWorkingTreeDirty { get; init; }

    public string RunnerStatusSha256 { get; init; } = "unknown";

    public bool RunnerStateChangedDuringRun { get; init; }

    public bool RunnerStateValidForEvidence { get; init; }
}

public sealed record CompatibilityReportSummary(
    int TargetCount,
    int ProductPublishFailureCount,
    int ProductSmokeFailureCount,
    int ProductInspectionFailureCount,
    int EnvironmentFailureCount,
    int UnsupportedCount,
    int BannedPayloadCount,
    int ThresholdWarningCount,
    int DistinctWarningCount,
    bool HasHardFailures)
{
    public int RunnerStateFailureCount { get; init; }
}

public sealed record CompatibilityTargetReport(
    string Name,
    CompatibilityTargetKind Kind,
    CompatibilityRuntimeGraph RuntimeGraph,
    string DisplayName,
    string ProjectPath,
    string PublishDirectory,
    string BuildScratchDirectory,
    CompatibilityCommandReport Publish,
    CompatibilityCommandReport Smoke,
    CompatibilityCommandReport Inspection,
    CompatibilityPayloadSizeSummary Payload,
    IReadOnlyList<CompatibilityBannedPayloadFinding> BannedPayloads,
    IReadOnlyList<CompatibilityThresholdFinding> ThresholdWarnings,
    CompatibilityWarningSummary WarningSummary,
    IReadOnlyList<CompatibilityLargestFile> LargestFiles,
    CompatibilityCompressedAssetSummary BrotliAssets,
    CompatibilityCompressedAssetSummary GzipAssets)
{
    public CompatibilityPackageResolutionReport? PackageResolution { get; init; }
}

public sealed record CompatibilityCommandReport(
    CompatibilityCommandStatus Status,
    int? ExitCode,
    double? DurationSeconds,
    string? RawLogPath,
    CompatibilityFailureDisposition FailureDisposition,
    CompatibilityFailureClassification FailureClassification,
    string? Summary)
{
    public CompatibilityBrowserSmokeDetails? Browser { get; init; }
}

public sealed record CompatibilityBrowserSmokeDetails(
    bool ContractPresent,
    string FinalStatus,
    string FinalStage,
    IReadOnlyList<string> WindowConsole,
    IReadOnlyList<string> PlaywrightConsole,
    IReadOnlyList<string> PageErrors);

public sealed record CompatibilityPayloadSizeSummary(
    long TotalBytes,
    long SymbolExcludedBytes,
    int FileCount);

public sealed record CompatibilityLargestFile(
    string RelativePath,
    long SizeBytes);

public sealed record CompatibilityCompressedAssetSummary(
    string Extension,
    int FileCount,
    long TotalBytes);

public sealed record CompatibilityBannedPayloadFinding(
    string Rule,
    string RelativePath,
    long SizeBytes);

public sealed record CompatibilityThresholdFinding(
    string Metric,
    long Actual,
    long Limit,
    string Severity,
    string Message);

public sealed record CompatibilityWarningSummary(
    int DistinctWarningCount,
    int TotalWarningCount,
    IReadOnlyList<CompatibilityWarningOwnerSummary> Owners,
    IReadOnlyList<CompatibilityWarningDiagnostic> Diagnostics);

public sealed record CompatibilityWarningOwnerSummary(
    CompatibilityWarningOwner Owner,
    int DistinctWarningCount,
    int TotalWarningCount);

public sealed record CompatibilityWarningDiagnostic(
    CompatibilityWarningOwner Owner,
    string? Code,
    string Message,
    IReadOnlyList<string> Projects,
    int Count);
