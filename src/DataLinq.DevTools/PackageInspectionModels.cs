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
    InvalidManagedAssembly,
    PackageArchiveChanged,
    InspectionError
}

public enum PackageInspectionOutcome
{
    Passed,
    Failed,
    Incomplete,
    Error
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
    bool FailOnAnalyzerAssetLeak)
{
    public string? ExpectedVersion { get; init; }

    public string? OutputDirectory { get; init; }

    public string OutputFormat { get; init; } = "summary";
}

public sealed record PackageInspectionInvocation(
    string Command,
    string RepositoryRoot,
    string PackageDirectory,
    string ReportDirectory,
    string? ExpectedVersion,
    string OutputFormat,
    IReadOnlyList<string> ExpectedPackageIds,
    IReadOnlyList<string> RuntimePackageIds,
    bool FailOnUnexpectedPackage,
    bool FailOnMissingSymbolPackage,
    bool FailOnRuntimeRoslyn,
    bool FailOnRuntimeRemotion,
    bool FailOnAnalyzerAssetLeak);

public sealed record PackageInspectionArtifacts(
    string JsonPath,
    string MarkdownPath);

public sealed record PackageInspectionCandidateIdentity(
    string AggregateSha256,
    string? Version,
    bool VersionConsistent,
    string? RepositoryCommit,
    bool RepositoryCommitConsistent,
    bool ArchivesStable);

public sealed record PackageInspectionRunnerEvidence(
    TestRunSummaryRepositoryState Start,
    TestRunSummaryRepositoryState End,
    TestRunSummaryRunnerAssembly EntryAssembly,
    TestRunSummaryRunnerAssembly DevToolsAssembly,
    bool StateChangedDuringRun,
    bool AssembliesMatchCheckout,
    bool AssembliesBuiltFromCleanState,
    bool CandidateMatchesCheckout,
    bool ValidForEvidence);

public sealed record PackageInspectionFailure(
    string Stage,
    string ExceptionType,
    string Message);

public sealed record PackageInspectionReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string RepositoryRoot,
    string PackageDirectory,
    string ReportDirectory,
    IReadOnlyList<PackageInspectionPackageReport> Packages,
    IReadOnlyList<PackageInspectionSymbolPackageReport> SymbolPackages,
    IReadOnlyList<PackageInspectionFinding> Findings,
    PackageInspectionSummary Summary)
{
    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public double DurationSeconds { get; init; }

    public PackageInspectionInvocation Invocation { get; init; } = null!;

    public PackageInspectionOutcome Outcome { get; init; } = PackageInspectionOutcome.Incomplete;

    public bool InspectionComplete { get; init; }

    public bool ArtifactsComplete { get; init; }

    public bool IsCanonicalReleasePolicy { get; init; }

    public bool PackageDirectoryIsRepositoryArtifact { get; init; }

    public bool ValidForEvidence { get; init; }

    public PackageInspectionArtifacts Artifacts { get; init; } = null!;

    public PackageInspectionCandidateIdentity Candidate { get; init; } = null!;

    public PackageInspectionRunnerEvidence Runner { get; init; } = null!;

    public PackageInspectionFailure? Failure { get; init; }
}

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
    IReadOnlyList<PackageBinaryPayloadMatch> BinaryPayloadMatches)
{
    public long SizeBytes { get; init; }

    public string Sha256 { get; init; } = "unknown";
}

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
    IReadOnlyList<PackageManagedAssemblyInspection> ManagedAssemblies)
{
    public long SizeBytes { get; init; }

    public string Sha256 { get; init; } = "unknown";
}

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
    string Message)
{
    public bool IsHardFailure { get; init; }
}
