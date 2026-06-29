using System;
using System.Collections.Generic;

namespace DataLinq.DevTools;

public enum PackageInspectionFindingKind
{
    MissingExpectedPackage,
    UnexpectedPackage,
    DuplicatePackage,
    MissingSymbolPackage,
    RuntimeRoslynDependency,
    RuntimeRoslynAsset,
    RuntimeRemotionDependency,
    RuntimeRemotionAsset,
    AnalyzerAssetLeak,
    MissingAnalyzerAsset
}

public sealed record PackageInspectionOptions(
    string RepositoryRoot,
    string PackageDirectory,
    IReadOnlySet<string> ExpectedPackageIds,
    IReadOnlySet<string> RuntimePackageIds,
    bool FailOnUnexpectedPackage,
    bool FailOnMissingSymbolPackage,
    bool FailOnRuntimeRoslyn,
    bool FailOnRuntimeRemotion,
    bool FailOnAnalyzerAssetLeak);

public sealed record PackageInspectionReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string RepositoryRoot,
    string PackageDirectory,
    string ReportDirectory,
    IReadOnlyList<PackageInspectionPackageReport> Packages,
    IReadOnlyList<PackageInspectionFinding> Findings,
    PackageInspectionSummary Summary);

public sealed record PackageInspectionSummary(
    int PackageCount,
    int ExpectedPackageCount,
    int RuntimePackageCount,
    int FindingCount,
    int HardFailureCount,
    bool HasHardFailures);

public sealed record PackageInspectionPackageReport(
    string Id,
    string Version,
    string PackagePath,
    string? SymbolPackagePath,
    bool IsRuntimePackage,
    bool IsExpectedPackage,
    bool IsDotnetTool,
    IReadOnlyList<PackageDependencyGroup> DependencyGroups,
    PackageAssetSummary Assets);

public sealed record PackageDependencyGroup(
    string TargetFramework,
    IReadOnlyList<PackageDependency> Dependencies);

public sealed record PackageDependency(
    string Id,
    string Version,
    string? Exclude);

public sealed record PackageAssetSummary(
    int LibFileCount,
    int AnalyzerFileCount,
    int ToolFileCount,
    int RuntimeFileCount,
    IReadOnlyList<string> LibFiles,
    IReadOnlyList<string> AnalyzerFiles,
    IReadOnlyList<string> ToolFiles,
    IReadOnlyList<string> RuntimeFiles,
    IReadOnlyList<string> SymbolFiles);

public sealed record PackageInspectionFinding(
    PackageInspectionFindingKind Kind,
    string PackageId,
    string? TargetFramework,
    string Message);
