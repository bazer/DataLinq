using System;
using System.Collections.Generic;
using DataLinq.DevTools;

namespace DataLinq.Benchmark.CLI;

internal static class BenchmarkEvidenceSchemas
{
    public const int HistoryVersion = 3;
    public const int ComparisonVersion = 3;
    public const string HistoryId = "v0.9.benchmark-history.v3";
    public const string ComparisonId = "v0.9.benchmark-comparison.v3";
    public const string RowAggregateFormat = "v0.9.benchmark-row-aggregate.v1";
}

internal static class BenchmarkEvidenceOutcomes
{
    public const string Passed = "Passed";
    public const string ReviewRequired = "ReviewRequired";
    public const string Incomplete = "Incomplete";
    public const string Error = "Error";
}

internal sealed record BenchmarkRunMetadata(
    string? Repository,
    string? Branch,
    string? Commit,
    string? Workflow,
    string? RunId,
    string? RunNumber,
    string? EventName,
    string? RunnerOs,
    string? RunnerArchitecture,
    string Profile,
    string Filter)
{
    public string? RuntimeDescription { get; init; }
    public int ProcessorCount { get; init; }
    public string? ProcessorIdentifier { get; init; }
    public string? BenchmarkDotNetVersion { get; init; }
}

internal sealed record BenchmarkInvocation(
    string Command,
    string RepositoryRoot,
    string BenchmarkProjectPath,
    string BenchmarkAssemblyPath,
    string RunArtifactsDirectory,
    string Profile,
    string ExpectedJob,
    string Filter,
    string? SelectedCategory,
    IReadOnlyList<string> ConfiguredProviderIds,
    bool NoBuild,
    bool KeepFiles,
    bool Verbose,
    IReadOnlyList<string> AdditionalArguments,
    bool ArgumentsRedacted,
    string? HistoryJsonPath,
    string? BaselinePath,
    string? ComparisonJsonPath,
    double WarningThresholdPercent,
    bool ReleaseEvidenceIntent);

internal sealed record BenchmarkCommandEnvironment(
    string? Profile,
    string? BenchmarkRunId,
    string? ArtifactsDirectory,
    string? ResultsDirectory,
    IReadOnlyList<string> ProviderIds);

internal sealed record BenchmarkCommandRecord(
    string Stage,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    double DurationSeconds,
    int ExitCode,
    string LogPath,
    BenchmarkCommandEnvironment Environment);

internal sealed record BenchmarkArtifactReference(
    string Kind,
    string Path,
    string RepositoryRelativePath,
    long SizeBytes,
    string Sha256);

internal sealed record BenchmarkArtifactPaths(
    string? HistoryJsonPath,
    string? ComparisonJsonPath,
    IReadOnlyList<BenchmarkArtifactReference> Files);

internal sealed record BenchmarkFailure(
    string Stage,
    string Type,
    string Message);

internal sealed record BenchmarkWarning(
    string Kind,
    string Message);

internal sealed record BenchmarkTarget(
    string Method,
    string ProviderName,
    string Category)
{
    public string Id => $"{Category}|{ProviderName}|{Method}";
}

internal sealed record BenchmarkHistorySummary(
    int ExpectedTargetCount,
    int ObservedTargetCount,
    int MeasuredRowCount,
    int InvalidRowCount,
    int TelemetryRowCount,
    int WarningCount,
    bool ExpectedScopeKnown,
    bool ExactTargetSet,
    bool RowsComplete);

internal sealed record BenchmarkAssemblyEvidence(
    string Path,
    string Sha256,
    TestRunSummaryRunnerAssembly Identity);

internal sealed record BenchmarkRunnerEvidence(
    TestRunSummaryRepositoryState Start,
    TestRunSummaryRepositoryState End,
    TestRunSummaryRunnerAssembly EntryAssembly,
    TestRunSummaryRunnerAssembly DevToolsAssembly,
    BenchmarkAssemblyEvidence BenchmarkAssembly,
    bool StateChangedDuringRun,
    bool AssembliesMatchCheckout,
    bool AssembliesBuiltFromCleanState,
    bool ValidForEvidence);

internal sealed record BenchmarkTelemetryDeltaArtifact(
    string Method,
    string ProviderName,
    int OperationsPerInvoke,
    double EntityQueriesPerOperation,
    double ScalarQueriesPerOperation,
    double TransactionStartsPerOperation,
    double TransactionCommitsPerOperation,
    double TransactionRollbacksPerOperation,
    double MutationInsertsPerOperation,
    double MutationUpdatesPerOperation,
    double MutationDeletesPerOperation,
    double MutationAffectedRowsPerOperation,
    double RowCacheHitsPerOperation,
    double RowCacheMissesPerOperation,
    double RowCacheStoresPerOperation,
    double DatabaseRowsPerOperation,
    double MaterializationsPerOperation,
    double RelationHitsPerOperation,
    double RelationLoadsPerOperation,
    double CacheInvalidationOperationsPerOperation,
    double CacheInvalidationRowsRemovedPerOperation,
    double CacheInvalidationTablesClearedPerOperation,
    double CacheInvalidationProviderKeysPerOperation,
    double CacheInvalidationApproximateWorkPerOperation,
    double CacheInvalidationPreciseOperationsPerOperation,
    double CacheInvalidationConservativeFallbackOperationsPerOperation,
    double MemoryDatabasesConstructedPerOperation = 0d,
    double MemoryRowsSeededPerOperation = 0d,
    double MemoryPrimaryKeyRequestsPerOperation = 0d,
    double MemoryPrimaryKeyProbesPerOperation = 0d,
    double MemoryScanRowsVisitedPerOperation = 0d,
    double MemoryPredicateEvaluationsPerOperation = 0d,
    double MemoryPredicateRejectionsPerOperation = 0d,
    double MemoryCacheLookupsPerOperation = 0d,
    double MemoryCacheHitsPerOperation = 0d,
    double MemoryCacheMissesPerOperation = 0d,
    double MemoryMaterializationsPerOperation = 0d,
    double MemoryCacheInsertionsPerOperation = 0d);

internal sealed record BenchmarkHistoryArtifactRow(
    string Method,
    string ProviderName,
    string Category,
    double? MeanMicroseconds,
    double? ErrorMicroseconds,
    double? MedianMicroseconds,
    double? StdDevMicroseconds,
    double? MinMicroseconds,
    double? MaxMicroseconds,
    double? AllocatedBytes,
    double? NoisePercent,
    double? UncertaintyPercent,
    double? StdDevPercent,
    int? OperationsPerInvoke,
    string? TrackingGroup,
    BenchmarkTelemetryDeltaArtifact? TelemetryDelta)
{
    public string? Job { get; init; }
    public string? Runtime { get; init; }
    public string? Jit { get; init; }
    public string? Platform { get; init; }
    public string? Toolchain { get; init; }
}

internal sealed record BenchmarkHistoryArtifact
{
    public int SchemaVersion { get; init; }
    public string? SchemaId { get; init; }
    public string RunId { get; init; } = string.Empty;
    public DateTime GeneratedAtUtc { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public double? DurationSeconds { get; init; }
    public BenchmarkRunMetadata Metadata { get; init; } = new(
        null, null, null, null, null, null, null, null, null, "default", "*");
    public BenchmarkInvocation? Invocation { get; init; }
    public string? Outcome { get; init; }
    public int OverallExitCode { get; init; }
    public bool IsCompleteForInvocation { get; init; }
    public bool ArtifactsComplete { get; init; }
    public bool ValidForEvidence { get; init; }
    public bool ReviewRequired { get; init; }
    public BenchmarkHistorySummary? Summary { get; init; }
    public IReadOnlyList<BenchmarkTarget> ExpectedTargets { get; init; } = [];
    public IReadOnlyList<BenchmarkTarget> ObservedTargets { get; init; } = [];
    public IReadOnlyList<BenchmarkCommandRecord> Commands { get; init; } = [];
    public IReadOnlyList<BenchmarkWarning> Warnings { get; init; } = [];
    public BenchmarkFailure? Failure { get; init; }
    public BenchmarkArtifactPaths? Artifacts { get; init; }
    public BenchmarkRunnerEvidence? RunnerEvidence { get; init; }
    public string? RowAggregateSha256 { get; init; }
    public IReadOnlyList<BenchmarkHistoryArtifactRow> Rows { get; init; } = [];
}

internal sealed record BenchmarkHistoryCreationInput(
    string RunId,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    BenchmarkRunMetadata Metadata,
    BenchmarkInvocation Invocation,
    IReadOnlyList<BenchmarkHistoryArtifactRow> Rows,
    IReadOnlyList<BenchmarkCommandRecord> Commands,
    IReadOnlyList<BenchmarkWarning> Warnings,
    BenchmarkFailure? Failure,
    BenchmarkArtifactPaths Artifacts,
    BenchmarkRunnerEvidence RunnerEvidence);

internal sealed record BenchmarkHistoryReference(
    string Path,
    string Sha256,
    long SizeBytes,
    int SchemaVersion,
    string? SchemaId,
    string RunId,
    DateTime GeneratedAtUtc,
    string? Commit,
    string Profile,
    string Filter,
    int RowCount,
    string RowAggregateSha256,
    bool LegacySchema,
    bool SourceValidForEvidence)
{
    public string? SelectedCategory { get; init; }
    public string? ExpectedJob { get; init; }
    public IReadOnlyList<string> ConfiguredProviderIds { get; init; } = [];
    public IReadOnlyList<string> ExpectedTargetIds { get; init; } = [];
    public string? RunnerOs { get; init; }
    public string? RunnerArchitecture { get; init; }
    public string? RuntimeDescription { get; init; }
    public int ProcessorCount { get; init; }
    public string? ProcessorIdentifier { get; init; }
    public string? BenchmarkDotNetVersion { get; init; }
    public string? Outcome { get; init; }
    public bool IsCompleteForInvocation { get; init; }
    public bool ArtifactsComplete { get; init; }
    public bool ReviewRequired { get; init; }
}

internal sealed record BenchmarkHistoryReadResult(
    BenchmarkHistoryArtifact Artifact,
    BenchmarkHistoryReference Reference);

internal sealed record BenchmarkEvidencePaths(
    string? HistoryJsonPath,
    string? BaselinePath,
    string? ComparisonJsonPath);

internal sealed record BenchmarkComparisonInvocation(
    string BaselinePath,
    string CandidatePath,
    string? ComparisonJsonPath,
    double WarningThresholdPercent,
    bool ReleaseEvidenceIntent);

internal sealed record BenchmarkComparisonStatusCounts(
    int Total,
    int Stable,
    int Improved,
    int Warning,
    int Noisy,
    int MissingBaseline,
    int MissingCandidate,
    int ProfileMismatch,
    int ScopeMismatch,
    int Invalid,
    int LatencyWarnings,
    int AllocationWarnings,
    int TelemetryChanges);

internal sealed record BenchmarkComparisonArtifactRow(
    string Method,
    string ProviderName,
    string Category,
    double? BaselineMeanMicroseconds,
    double? CandidateMeanMicroseconds,
    double? MeanDeltaPercent,
    double? BaselineAllocatedBytes,
    double? CandidateAllocatedBytes,
    double? AllocatedDeltaPercent,
    double MaxNoisePercent,
    string? TrackingGroup,
    string Status)
{
    public string LatencyStatus { get; init; } = "invalid";
    public string AllocationStatus { get; init; } = "invalid";
    public string TelemetryStatus { get; init; } = "invalid";
    public int? BaselineOperationsPerInvoke { get; init; }
    public int? CandidateOperationsPerInvoke { get; init; }
    public BenchmarkTelemetryDeltaArtifact? BaselineTelemetry { get; init; }
    public BenchmarkTelemetryDeltaArtifact? CandidateTelemetry { get; init; }
    public string? BaselineJob { get; init; }
    public string? CandidateJob { get; init; }
    public string? BaselineRuntime { get; init; }
    public string? CandidateRuntime { get; init; }
    public string? BaselineJit { get; init; }
    public string? CandidateJit { get; init; }
    public string? BaselinePlatform { get; init; }
    public string? CandidatePlatform { get; init; }
    public string? BaselineToolchain { get; init; }
    public string? CandidateToolchain { get; init; }
}

internal sealed record BenchmarkComparisonArtifact
{
    public int SchemaVersion { get; init; }
    public string? SchemaId { get; init; }
    public DateTime GeneratedAtUtc { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public double WarningThresholdPercent { get; init; }
    public int WarningCount { get; init; }
    public BenchmarkRunMetadata Baseline { get; init; } = new(
        null, null, null, null, null, null, null, null, null, "default", "*");
    public BenchmarkRunMetadata Candidate { get; init; } = new(
        null, null, null, null, null, null, null, null, null, "default", "*");
    public string? BaselineRunId { get; init; }
    public string? CandidateRunId { get; init; }
    public BenchmarkComparisonInvocation? Invocation { get; init; }
    public BenchmarkHistoryReference? BaselineArtifact { get; init; }
    public BenchmarkHistoryReference? CandidateArtifact { get; init; }
    public string? Outcome { get; init; }
    public int OverallExitCode { get; init; }
    public bool IsComplete { get; init; }
    public bool ArtifactsComplete { get; init; }
    public bool Comparable { get; init; }
    public bool ReviewRequired { get; init; }
    public bool ValidForEvidence { get; init; }
    public BenchmarkComparisonStatusCounts? StatusCounts { get; init; }
    public BenchmarkFailure? Failure { get; init; }
    public IReadOnlyList<BenchmarkComparisonArtifactRow> Rows { get; init; } = [];
}
