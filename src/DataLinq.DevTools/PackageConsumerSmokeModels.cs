using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.DevTools;

public enum PackageConsumerSmokeFindingSeverity
{
    Warning,
    Error
}

public enum PackageConsumerSmokeOutcome
{
    Passed,
    Failed
}

public sealed record PackageConsumerSmokeOptions(
    string RepositoryRoot,
    string PackageDirectory,
    string OutputDirectory,
    string Version,
    ToolingProfile Profile);

public sealed record PackageConsumerSmokeReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string RepositoryRoot,
    string PackageDirectory,
    string Version,
    ToolingProfile Profile,
    string FixtureDirectory,
    string WorkspaceDirectory,
    string ReportDirectory,
    string NugetConfigPath,
    string PackagesCacheDirectory,
    IReadOnlyList<PackageConsumerCandidatePackage> CandidatePackages,
    IReadOnlyList<PackageConsumerCommandReport> Commands,
    IReadOnlyList<PackageConsumerResolvedPackage> ResolvedPackages,
    PackageConsumerExecutionReport? Execution,
    PackageConsumerGeneratedSourceReport GeneratedSource,
    IReadOnlyList<PackageConsumerSmokeFinding> Findings,
    PackageConsumerSmokeSummary Summary)
{
    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public double DurationSeconds { get; init; }

    public PackageConsumerSmokeOutcome Outcome { get; init; } = PackageConsumerSmokeOutcome.Failed;

    public bool IsCompleteForInvocation { get; init; }

    public int OverallExitCode { get; init; } = 1;

    public IReadOnlyList<string> ArtifactPaths { get; init; } = [];
}

public sealed record PackageConsumerCandidatePackage(
    string Id,
    string Version,
    string PackagePath,
    long SizeBytes,
    string Sha256,
    bool NuspecIdentityMatches);

public sealed record PackageConsumerCommandReport(
    string Name,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    int? ExitCode,
    double? DurationSeconds,
    string? RawLogPath,
    bool Succeeded,
    string? FailureSummary);

public sealed record PackageConsumerResolvedPackage(
    string Id,
    string Version,
    string AssetsLibraryKey,
    string PackageCacheDirectory,
    string MetadataPath,
    string? Source,
    string CachedPackagePath,
    string CandidatePackagePath,
    string CandidateSha256,
    string? CachedSha256,
    bool ExactVersion,
    bool SourceMatchesCandidateDirectory,
    bool HashMatchesCandidate);

public sealed record PackageConsumerExecutionReport(
    string SchemaVersion,
    string TargetFramework,
    PackageConsumerMemoryExecutionReport Memory,
    PackageConsumerSQLiteExecutionReport Sqlite,
    bool MySqlCompilationProbe,
    bool Passed,
    bool ContractValidated,
    string RawJson);

public sealed record PackageConsumerMemoryExecutionReport(
    bool Passed,
    int? FoundId,
    bool Missing,
    IReadOnlyList<int> QueryIds);

public sealed record PackageConsumerSQLiteExecutionReport(
    bool Passed,
    IReadOnlyList<int> RowIds);

public sealed record PackageConsumerGeneratedSourceReport(
    bool MutableModelFound,
    bool DatabaseFound,
    IReadOnlyList<string> MatchingFiles)
{
    public bool Passed => MutableModelFound && DatabaseFound &&
                          TargetFrameworks.Count == 3 &&
                          TargetFrameworks.All(static target => target.Passed);

    public IReadOnlyList<PackageConsumerGeneratedSourceTargetReport> TargetFrameworks { get; init; } = [];
}

public sealed record PackageConsumerGeneratedSourceTargetReport(
    string TargetFramework,
    bool MutableModelFound,
    bool DatabaseFound,
    IReadOnlyList<string> MatchingFiles)
{
    public bool Passed => MutableModelFound && DatabaseFound;
}

public sealed record PackageConsumerSmokeFinding(
    PackageConsumerSmokeFindingSeverity Severity,
    string Code,
    string Message);

public sealed record PackageConsumerSmokeSummary(
    int RequiredPackageCount,
    int CandidatePackageCount,
    int ResolvedPackageCount,
    int BuildCount,
    int SuccessfulBuildCount,
    bool RestoreSucceeded,
    bool ExecutionSucceeded,
    bool GeneratedSourceVerified,
    int FindingCount,
    int HardFailureCount,
    bool HasHardFailures);
