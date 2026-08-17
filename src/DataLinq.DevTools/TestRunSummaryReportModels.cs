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
    bool IncludeSqliteTargets,
    string? Filter = null);

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
    ToolingProfile Profile,
    string? Plan = null,
    int? MaximumParallelTests = null);

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
    IReadOnlyList<string> TargetIds,
    string? ProviderAffinityRole = null);

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

public sealed record TestRunSummarySlowTest(
    string Name,
    string? ClassName,
    string Outcome,
    double DurationSeconds);

public sealed record TestRunSummarySlowClass(
    string ClassName,
    int TestCount,
    double TotalDurationSeconds,
    double AverageDurationSeconds,
    double MaximumDurationSeconds);

public sealed record TestRunSummaryPerformance(
    bool Captured,
    string? CaptureError,
    int TestCount,
    double TotalTestDurationSeconds,
    double? P50DurationSeconds,
    double? P95DurationSeconds,
    double? P99DurationSeconds,
    double? MaximumDurationSeconds,
    double? EffectiveConcurrency,
    int? ConfiguredMaximumParallelTests,
    string ConfiguredParallelismSource,
    IReadOnlyList<TestRunSummarySlowTest> SlowestTests,
    IReadOnlyList<TestRunSummarySlowClass> SlowestClasses);

public sealed record TestRunSummaryTimingBreakdown(
    double BuildProcessSeconds,
    double InfrastructureSetupSeconds,
    double TestHostProcessSeconds,
    double TestBodySeconds,
    double TeardownSeconds);

public sealed record TestRunSummaryRuntimeEnvironment(
    string OperatingSystem,
    string ProcessArchitecture,
    string FrameworkDescription,
    int ProcessorCount);

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
    string LogPath,
    string HtmlReportPath,
    string TrxReportPath,
    double InfrastructureSetupDurationSeconds,
    TestRunSummaryPerformance Performance,
    string? ProviderAffinityRole = null);

public sealed record TestRunSummaryFailure(
    string Stage,
    string ExceptionType,
    string Message);

public sealed record TestRunSummaryReportInput(
    string RunId,
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
    TestRunSummaryFailure? TeardownFailure = null,
    double TeardownDurationSeconds = 0);

public sealed record TestRunSummaryReport(
    string SchemaVersion,
    string RunId,
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
    TestRunSummaryTimingBreakdown Timings,
    TestRunSummaryRuntimeEnvironment RuntimeEnvironment,
    TestRunSummaryRunnerEvidence RunnerEvidence,
    IReadOnlyList<TestRunSummaryExpectedResult> ExpectedResults,
    IReadOnlyList<TestRunSummaryBuild> Builds,
    IReadOnlyList<TestRunSummaryResult> Results,
    IReadOnlyList<string> ArtifactPaths,
    TestRunSummaryFailure? Failure,
    TestRunSummaryFailure? TeardownFailure);
