using System;
using System.Collections.Generic;

namespace DataLinq.DevTools;

public enum ApiCompatibilityFindingSeverity
{
    Information,
    Review,
    Error
}

public enum ApiCompatibilityChangeKind
{
    CompatibilityBreak,
    SourceSensitiveBreak,
    CurrentPackageFrameworkMismatch,
    CompatibleApiChange,
    NewPackageSurface
}

public enum ApiCompatibilityComparisonKind
{
    PackageBaseline,
    ToolAssemblyBaseline,
    CurrentFramework,
    NewPackage
}

public sealed record ApiCompatibilityReportOptions(
    string RepositoryRoot,
    string CandidatePackageDirectory,
    string CandidateVersion,
    string BaselinePackageDirectory,
    string BaselineVersion,
    string BaselineLockPath,
    string OutputDirectory,
    ToolingProfile Profile);

public sealed record ApiCompatibilityReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    ApiCompatibilityReportInvocation Invocation,
    string ReportDirectory,
    ApiCompatibilityBaselineLockReport? BaselineLock,
    ApiPackageSetInspection? BaselinePackages,
    ApiPackageSetInspection? CandidatePackages,
    string? BaselineAggregateIdentity,
    string? CandidateAggregateIdentity,
    string? ApiCompatToolVersion,
    ApiCompatibilityRunnerEvidence Runner,
    IReadOnlyList<ApiCompatibilityToolExecutionReport> ToolExecutions,
    IReadOnlyList<ApiCompatibilitySurfaceReport> Surfaces,
    IReadOnlyList<ApiCompatibilityComparisonReport> Comparisons,
    IReadOnlyList<ApiCompatibilityFinding> Findings,
    ApiCompatibilityReportSummary Summary);

public sealed record ApiCompatibilityReportInvocation(
    string RepositoryRoot,
    string CandidatePackageDirectory,
    string CandidateVersion,
    string BaselinePackageDirectory,
    string BaselineVersion,
    string BaselineLockPath,
    ToolingProfile Profile,
    IReadOnlyList<string> BaselinePackageIds,
    IReadOnlyList<string> CandidatePackageIds);

public sealed record ApiCompatibilityBaselineLockReport(
    string SchemaVersion,
    string BaselineVersion,
    string PackageSource,
    string RepositoryUrl,
    string RepositoryCommit,
    string RepositoryTag,
    string RepositoryTagObjectType,
    string ProvenanceNote,
    IReadOnlyDictionary<string, string> PackageSha256,
    string LockPath,
    string LockSha256,
    bool CanonicalTrackedPolicy);

public sealed record ApiCompatibilityRunnerEvidence(
    ApiCompatibilityRepositoryState Start,
    ApiCompatibilityRepositoryState End,
    ApiCompatibilityRunnerAssembly EntryAssembly,
    ApiCompatibilityRunnerAssembly DevToolsAssembly,
    bool StateChangedDuringRun,
    bool AssembliesMatchCheckout,
    bool AssembliesBuiltFromCleanState,
    bool CandidateMatchesCheckout,
    bool BaselineTagMatchesLock,
    bool BaselineLockMatchesCheckout,
    bool ValidForEvidence);

public sealed record ApiCompatibilityRepositoryState(
    string Commit,
    string Branch,
    bool Dirty,
    string StatusSha256,
    bool Captured);

public sealed record ApiCompatibilityRunnerAssembly(
    string Name,
    string InformationalVersion,
    string RepositoryCommit,
    bool RepositoryCommitCaptured,
    string RepositoryBuildState);

public sealed record ApiCompatibilityToolExecutionReport(
    string Name,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    int? ExitCode,
    double DurationSeconds,
    string StandardOutputPath,
    string StandardErrorPath,
    string? SuppressionPath,
    int DiagnosticCount,
    bool Succeeded,
    string? Failure);

public sealed record ApiCompatibilitySurfaceReport(
    string Side,
    string PackageId,
    string PackageVersion,
    string TargetFramework,
    string AssetPath,
    string AssemblyIdentity,
    Guid ModuleVersionId,
    string FileSha256,
    string ApiSha256,
    int ApiLineCount,
    string SnapshotPath);

public sealed record ApiCompatibilityComparisonReport(
    string PackageId,
    ApiCompatibilityComparisonKind Kind,
    string? TargetFramework,
    string? PrimaryExecution,
    string? SecondaryExecution,
    int ChangeCount,
    int HardFailureCount,
    int ReviewCount,
    bool Succeeded);

public sealed record ApiCompatibilityFinding(
    ApiCompatibilityFindingSeverity Severity,
    string Code,
    string PackageId,
    string? TargetFramework,
    string Message,
    ApiCompatibilityChangeKind? ChangeKind = null,
    string? DiagnosticId = null,
    string? Target = null,
    string? Left = null,
    string? Right = null,
    string? Fingerprint = null);

public sealed record ApiCompatibilityReportSummary(
    int BaselinePackageCount,
    int CandidatePackageCount,
    int SurfaceCount,
    int ComparisonCount,
    int FindingCount,
    int ReviewCount,
    int HardFailureCount,
    int CompatibilityBreakCount,
    int FrameworkMismatchCount,
    int CompatibleChangeCount,
    int NewPackageSurfaceCount,
    bool HasHardFailures,
    bool RequiresReview);
