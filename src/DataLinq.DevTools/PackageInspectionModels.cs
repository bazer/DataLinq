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
    MissingAnalyzerAsset,
    PackageVersionMismatch,
    PackageIdentityMismatch,
    MissingPackageMetadata,
    InvalidPackageMetadata,
    MissingRequiredPackageAsset,
    UnexpectedPackageAsset,
    MissingDependencyGroup,
    UnexpectedDependencyGroup,
    MissingRequiredPackageDependency,
    UnexpectedPackageDependency,
    PackageDependencyVersionMismatch,
    PackageDependencyExclusionMismatch,
    BannedRuntimeDependency,
    BannedRuntimeAsset,
    OrphanSymbolPackage,
    DuplicateSymbolPackage,
    UnexpectedSymbolPackageAsset,
    BannedSymbolPackageAsset,
    InvalidManagedAssembly
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
    IReadOnlyList<PackageInspectionSymbolPackageReport> SymbolPackages,
    IReadOnlyList<PackageInspectionFinding> Findings,
    PackageInspectionSummary Summary);

public sealed record PackageInspectionSummary(
    int PackageCount,
    int ExpectedPackageCount,
    int RuntimePackageCount,
    int FindingCount,
    int HardFailureCount,
    bool HasHardFailures);

public sealed record PackageInspectionSymbolPackageReport(
    string Id,
    string Version,
    string PackagePath,
    PackageMetadata Metadata,
    IReadOnlyList<string> PdbFiles,
    IReadOnlyList<string> AllFiles,
    IReadOnlyList<PackageBinaryPayloadMatch> BinaryPayloadMatches);

public sealed record PackageInspectionPackageReport(
    string Id,
    string Version,
    string PackagePath,
    string? SymbolPackagePath,
    bool IsRuntimePackage,
    bool IsExpectedPackage,
    bool IsDotnetTool,
    PackageMetadata Metadata,
    string? SymbolPackageId,
    string? SymbolPackageVersion,
    IReadOnlyList<PackageDependencyGroup> DependencyGroups,
    PackageAssetSummary Assets,
    IReadOnlyList<PackagePayloadTokenMatch> PayloadTokenMatches,
    IReadOnlyList<PackageBinaryPayloadMatch> BinaryPayloadMatches,
    IReadOnlyList<PackageManagedAssemblyInspection> ManagedAssemblies);

public sealed record PackageMetadata(
    string? Id,
    string? Version,
    string? Description,
    string? LicenseType,
    string? License,
    string? Readme,
    string? RepositoryType,
    string? RepositoryUrl,
    string? RepositoryBranch,
    string? RepositoryCommit);

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
    IReadOnlyList<string> SymbolFiles,
    IReadOnlyList<string> AllFiles);

public sealed record PackagePayloadTokenMatch(
    string Asset,
    string Token);

public sealed record PackageBinaryPayloadMatch(
    string Asset,
    string Signature);

public sealed record PackageManagedAssemblyInspection(
    string Asset,
    string? AssemblyName,
    string? Error);

public sealed record PackageInspectionFinding(
    PackageInspectionFindingKind Kind,
    string PackageId,
    string? TargetFramework,
    string Message);
