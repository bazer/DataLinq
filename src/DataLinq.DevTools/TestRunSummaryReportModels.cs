using System;
using System.Collections.Generic;

namespace DataLinq.DevTools;

public enum TestRunSummaryOutcome
{
    Passed,
    Failed,
    Error,
    Incomplete
}

public sealed record TestRunSummaryTarget(
    string Id,
    string DisplayName,
    string Category,
    bool UsesPodman,
    int? HostPort);

public sealed record TestRunSummarySafeEnvironment(
    bool DatabaseHostOverridePresent,
    bool DatabaseHostOverrideValid,
    string? DatabaseHostOverride,
    string ProviderSetForTargetBatches,
    bool ClearsTargetAliasForTargetBatches);

public sealed record TestRunSummarySuite(
    string Name,
    string ProjectPath,
    bool UsesTargetBatches,
    bool IncludeSqliteTargets);

public sealed record TestRunSummaryInvocation(
    string Command,
    string RepositoryRoot,
    string? Alias,
    IReadOnlyList<TestRunSummaryTarget> SelectedTargets,
    IReadOnlyList<TestRunSummarySuite> ResolvedSuites,
    TestRunSummarySafeEnvironment SafeEnvironment,
    bool IncludesAllSuites,
    bool IncludesAllTargets,
    bool IsUnfiltered,
    string Suite,
    string? ProjectPath,
    string? Filter,
    string Configuration,
    bool BuildProject,
    int BatchSize,
    bool ParallelSuites,
    bool TearDown,
    string OutputMode,
    ToolingProfile Profile);

public sealed record TestRunSummaryRepositoryState(
    bool Captured,
    string Commit,
    string Branch,
    bool Dirty,
    string StatusSha256);

public sealed record TestRunSummaryRunnerAssembly(
    string Name,
    string InformationalVersion,
    string RepositoryCommit,
    bool RepositoryCommitCaptured,
    string RepositoryBuildState);

public sealed record TestRunSummaryRunnerEvidence(
    TestRunSummaryRepositoryState Start,
    TestRunSummaryRepositoryState End,
    TestRunSummaryRunnerAssembly EntryAssembly,
    TestRunSummaryRunnerAssembly DevToolsAssembly,
    bool StateChangedDuringRun,
    bool AssembliesMatchCheckout,
    bool AssembliesBuiltFromCleanState,
    bool ValidForEvidence);

public sealed record TestRunSummaryExpectedResult(
    string Suite,
    string ProjectPath,
    int? BatchIndex,
    IReadOnlyList<string> TargetIds);

public sealed record TestRunSummaryBuild(
    string ProjectPath,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    double DurationSeconds,
    int ExitCode,
    string LogPath);

public sealed record TestRunSummaryCommandEnvironment(
    bool UsesDatabaseHost,
    bool DatabaseHostCaptured,
    string? DatabaseHost,
    bool UsesExplicitTargetSet,
    bool TargetAliasCleared,
    IReadOnlyList<string> TargetIds);

public sealed record TestRunSummaryResult(
    string Suite,
    string ProjectPath,
    int? BatchIndex,
    string Targets,
    IReadOnlyList<string> TargetIds,
    TestRunSummaryOutcome Outcome,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TestRunSummaryCommandEnvironment Environment,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    double DurationSeconds,
    int ExitCode,
    int? Total,
    int? Passed,
    int? Failed,
    int? Skipped,
    IReadOnlyList<string> ArtifactPaths,
    string LogPath);

public sealed record TestRunSummaryFailure(
    string Stage,
    string ExceptionType,
    string Message);

public sealed record TestRunSummaryReportInput(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    TestRunSummaryInvocation Invocation,
    string ReportPath,
    TestRunSummaryRepositoryState RepositoryStart,
    TestRunSummaryRepositoryState RepositoryEnd,
    TestRunSummaryRunnerAssembly EntryAssembly,
    TestRunSummaryRunnerAssembly DevToolsAssembly,
    int OverallExitCode,
    int? Total,
    int? Passed,
    int? Failed,
    int? Skipped,
    IReadOnlyList<TestRunSummaryExpectedResult> ExpectedResults,
    IReadOnlyList<TestRunSummaryBuild> Builds,
    IReadOnlyList<TestRunSummaryResult> Results,
    TestRunSummaryFailure? Failure,
    TestRunSummaryFailure? TeardownFailure = null);

public sealed record TestRunSummaryReport(
    string SchemaVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    double DurationSeconds,
    TestRunSummaryInvocation Invocation,
    string ReportPath,
    TestRunSummaryOutcome Outcome,
    bool CountsComplete,
    bool IsCompleteForInvocation,
    bool ArtifactsComplete,
    bool IsFullMatrixInvocation,
    bool HasPerTargetProviderTotals,
    bool ValidForEvidence,
    int OverallExitCode,
    int? Total,
    int? Passed,
    int? Failed,
    int? Skipped,
    TestRunSummaryRunnerEvidence RunnerEvidence,
    IReadOnlyList<TestRunSummaryExpectedResult> ExpectedResults,
    IReadOnlyList<TestRunSummaryBuild> Builds,
    IReadOnlyList<TestRunSummaryResult> Results,
    IReadOnlyList<string> ArtifactPaths,
    TestRunSummaryFailure? Failure,
    TestRunSummaryFailure? TeardownFailure);
