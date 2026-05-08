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

public enum CompatibilityCommandStatus
{
    Succeeded,
    Failed,
    Skipped,
    NotApplicable
}

public enum CompatibilityFailureClassification
{
    None,
    UnsupportedNoAot,
    SdkOrWebAssemblyToolchain,
    Dotnet,
    Unknown
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
    IReadOnlyList<CompatibilityTargetKind> Targets,
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
    bool ContinueOnPublishFailure);

public sealed record CompatibilitySizeReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string RepositoryRoot,
    string TargetSet,
    string Configuration,
    string RuntimeIdentifier,
    string DotnetSdkVersion,
    string ReportDirectory,
    IReadOnlyList<CompatibilityTargetReport> Targets,
    CompatibilityReportSummary Summary);

public sealed record CompatibilityReportSummary(
    int TargetCount,
    int PublishFailureCount,
    int SmokeFailureCount,
    int BannedPayloadCount,
    int ThresholdWarningCount,
    int DistinctWarningCount,
    bool HasHardFailures);

public sealed record CompatibilityTargetReport(
    string Name,
    CompatibilityTargetKind Kind,
    string DisplayName,
    string ProjectPath,
    string PublishDirectory,
    CompatibilityCommandReport Publish,
    CompatibilityCommandReport Smoke,
    CompatibilityPayloadSizeSummary Payload,
    IReadOnlyList<CompatibilityBannedPayloadFinding> BannedPayloads,
    IReadOnlyList<CompatibilityThresholdFinding> ThresholdWarnings,
    CompatibilityWarningSummary WarningSummary,
    IReadOnlyList<CompatibilityLargestFile> LargestFiles,
    CompatibilityCompressedAssetSummary BrotliAssets,
    CompatibilityCompressedAssetSummary GzipAssets);

public sealed record CompatibilityCommandReport(
    CompatibilityCommandStatus Status,
    int? ExitCode,
    double? DurationSeconds,
    string? RawLogPath,
    CompatibilityFailureClassification FailureClassification,
    string? Summary);

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
