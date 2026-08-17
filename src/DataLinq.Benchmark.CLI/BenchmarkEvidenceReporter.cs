using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataLinq.DevTools;

namespace DataLinq.Benchmark.CLI;

internal static class BenchmarkEvidenceReporter
{
    private const long MaximumHistoryBytes = 16L * 1024L * 1024L;
    private const int MaximumHistoryRows = 10_000;
    private const double LatencyNoiseThresholdPercent = 20d;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static readonly IReadOnlyDictionary<string, string[]> ReleaseLaneMethods =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [BenchmarkHarnessRunner.Phase2WatchCategory] =
            [
                "Provider initialization",
                "Startup primary-key fetch",
                "Warm primary-key fetch"
            ],
            [BenchmarkHarnessRunner.Phase3QueryHotPathCategory] =
            [
                "Repeated non-PK equality fetch",
                "Repeated IN predicate fetch",
                "Repeated scalar Any"
            ],
            [BenchmarkHarnessRunner.V09QueryBackendCategory] =
            [
                "Expression parse/structural template",
                "Expression parse/template/initial bind",
                "Template freeze/validation",
                "Invocation bind scalar/local sequence",
                "SQL request/capability preparation",
                "SQL adapter scalar Any"
            ],
            [BenchmarkHarnessRunner.V09MemoryReadCategory] =
            [
                "Memory database construction",
                "Memory construct and seed",
                "Memory primary-key hit",
                "Memory primary-key miss",
                "Memory scalar scan",
                "Memory filter order page",
                "Memory repeated entity identity",
                "Memory direct-Guid equality count",
                "Memory typed-ID equality count"
            ],
            [BenchmarkHarnessRunner.AllocationRegressionCategory] =
            [
                "Provider initialization",
                "Startup primary-key fetch",
                "CRUD workflow small",
                "CRUD workflow batch",
                "Update employees",
                "Cold primary-key fetch",
                "Warm primary-key fetch",
                "Cold relation traversal",
                "Warm relation traversal"
            ],
            [BenchmarkHarnessRunner.AllocationStagesCategory] =
            [
                "Canonical provider-row decoding",
                "Provider-row model materialization",
                "Provider-row decode/materialization pipeline",
                "Composite key reconstruction baseline",
                "Scalar canonical-key propagation",
                "Composite canonical-key propagation",
                "Typed-ID canonical-key propagation",
                "Converter-backed canonical-key propagation",
                "Binary canonical-key propagation",
                "Mutation state-change capture",
                "Mutation execution preflight"
            ]
        };

    private static readonly IReadOnlyDictionary<string, int> ReleaseOperationCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Provider initialization"] = 1,
            ["Startup primary-key fetch"] = 1,
            ["Warm primary-key fetch"] = 1000,
            ["CRUD workflow small"] = 50,
            ["CRUD workflow batch"] = 300,
            ["Update employees"] = 1000,
            ["Cold primary-key fetch"] = 1000,
            ["Cold relation traversal"] = 1000,
            ["Warm relation traversal"] = 1000,
            ["Repeated non-PK equality fetch"] = 1000,
            ["Repeated IN predicate fetch"] = 1000,
            ["Repeated scalar Any"] = 1000,
            ["Expression parse/structural template"] = 1000,
            ["Expression parse/template/initial bind"] = 1000,
            ["Template freeze/validation"] = 1000,
            ["Invocation bind scalar/local sequence"] = 1000,
            ["SQL request/capability preparation"] = 1000,
            ["SQL adapter scalar Any"] = 3000,
            ["Memory database construction"] = 1,
            ["Memory construct and seed"] = 1,
            ["Memory primary-key hit"] = 1,
            ["Memory primary-key miss"] = 1,
            ["Memory scalar scan"] = 1,
            ["Memory filter order page"] = 1,
            ["Memory repeated entity identity"] = 1,
            ["Memory direct-Guid equality count"] = 1,
            ["Memory typed-ID equality count"] = 1,
            ["Canonical provider-row decoding"] = 1000,
            ["Provider-row model materialization"] = 1000,
            ["Provider-row decode/materialization pipeline"] = 1000,
            ["Composite key reconstruction baseline"] = 1000,
            ["Scalar canonical-key propagation"] = 1000,
            ["Composite canonical-key propagation"] = 1000,
            ["Typed-ID canonical-key propagation"] = 1000,
            ["Converter-backed canonical-key propagation"] = 1000,
            ["Binary canonical-key propagation"] = 1000,
            ["Mutation state-change capture"] = 1000,
            ["Mutation execution preflight"] = 1000
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static BenchmarkEvidencePaths NormalizePaths(
        string repositoryRoot,
        string? historyJsonPath,
        string? baselinePath,
        string? comparisonJsonPath,
        bool releaseEvidenceIntent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var normalizedRoot = Path.GetFullPath(repositoryRoot);
        var history = NormalizeOptionalArtifactPath(normalizedRoot, historyJsonPath, "history");
        var baseline = NormalizeOptionalArtifactPath(normalizedRoot, baselinePath, "baseline");
        var comparison = NormalizeOptionalArtifactPath(normalizedRoot, comparisonJsonPath, "comparison");
        _ = releaseEvidenceIntent;

        var namedPaths = new[]
        {
            (Name: "history", Path: history),
            (Name: "baseline", Path: baseline),
            (Name: "comparison", Path: comparison)
        }.Where(static item => item.Path is not null).ToArray();
        for (var left = 0; left < namedPaths.Length; left++)
        {
            for (var right = left + 1; right < namedPaths.Length; right++)
            {
                if (PathComparer.Equals(namedPaths[left].Path, namedPaths[right].Path))
                {
                    throw new InvalidDataException(
                        $"Benchmark {namedPaths[left].Name} and {namedPaths[right].Name} paths must be distinct.");
                }
            }
        }

        return new BenchmarkEvidencePaths(history, baseline, comparison);
    }

    public static void ValidatePathDependencies(
        BenchmarkEvidencePaths paths,
        bool releaseEvidenceIntent)
    {
        if (paths.ComparisonJsonPath is not null && paths.BaselinePath is null)
            throw new InvalidDataException("--comparison-json requires --baseline.");
        if (paths.BaselinePath is not null && paths.HistoryJsonPath is null)
            throw new InvalidDataException("--baseline requires --history-json so the candidate run has a persisted identity.");
        if (releaseEvidenceIntent && paths.HistoryJsonPath is null)
            throw new InvalidDataException("--release-evidence requires --history-json.");
        if (releaseEvidenceIntent && paths.BaselinePath is not null && paths.ComparisonJsonPath is null)
            throw new InvalidDataException("A release-evidence comparison requires --comparison-json.");
    }

    public static void InvalidateRequestedOutputs(string repositoryRoot, BenchmarkEvidencePaths paths)
    {
        if (paths.HistoryJsonPath is not null)
            InvalidateOutput(repositoryRoot, paths.HistoryJsonPath);
        if (paths.ComparisonJsonPath is not null)
            InvalidateOutput(repositoryRoot, paths.ComparisonJsonPath);
    }

    public static void ValidateBaselinePath(string repositoryRoot, string baselinePath)
    {
        var artifactRoot = GetArtifactRoot(repositoryRoot);
        var fullPath = Path.GetFullPath(baselinePath);
        if (!IsArtifactFile(fullPath, artifactRoot))
        {
            throw new InvalidDataException(
                "The benchmark baseline must be an existing regular file beneath repository artifacts without reparse-point traversal.");
        }
    }

    public static string PrepareRunDirectory(string repositoryRoot, string runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("The benchmark run id is invalid.");

        var runDirectory = Path.Combine(repositoryRoot, "artifacts", "benchmarks", "runs", runId);
        var artifactRoot = GetArtifactRoot(repositoryRoot);
        if (!IsArtifactOutputPath(runDirectory, artifactRoot))
            throw new InvalidDataException("The benchmark run directory is outside the safe artifact tree.");
        if (Directory.Exists(runDirectory) || File.Exists(runDirectory))
            throw new InvalidDataException($"Benchmark run directory '{runDirectory}' already exists.");

        Directory.CreateDirectory(runDirectory);
        if (!IsSafeDirectory(runDirectory, artifactRoot))
            throw new InvalidDataException("The benchmark run directory became unsafe while it was created.");
        return Path.GetFullPath(runDirectory);
    }

    public static IReadOnlyList<string> ResolveConfiguredProviderIds(string? selectedCategory, string? configured)
    {
        if (string.Equals(selectedCategory, BenchmarkHarnessRunner.V09MemoryReadCategory, StringComparison.Ordinal))
            return ["memory"];

        if (string.Equals(selectedCategory, BenchmarkHarnessRunner.AllocationRegressionCategory, StringComparison.Ordinal))
            return ["sqlite-memory"];

        var providers = string.IsNullOrWhiteSpace(configured)
            ? new[] { "sqlite-file", "sqlite-memory" }
            : configured.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        if (providers.Length == 0)
            throw new InvalidDataException("The benchmark provider selection is empty.");

        var supported = new HashSet<string>(["sqlite-file", "sqlite-memory"], StringComparer.OrdinalIgnoreCase);
        var unsupported = providers.FirstOrDefault(provider => !supported.Contains(provider));
        if (unsupported is not null)
            throw new InvalidDataException($"Unsupported benchmark provider '{unsupported}'.");

        return providers
            .Select(static provider => provider.ToLowerInvariant())
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static string ResolveExpectedJob(string profile) => profile.Trim().ToLowerInvariant() switch
    {
        "default" => "ShortRun",
        "heavy" => "MediumRun",
        "smoke" => "Dry",
        _ => throw new InvalidDataException("The benchmark profile must be 'default', 'heavy', or 'smoke'.")
    };

    public static IReadOnlyList<BenchmarkTarget> ResolveExpectedTargets(BenchmarkInvocation invocation)
    {
        if (invocation.SelectedCategory is null ||
            !ReleaseLaneMethods.TryGetValue(invocation.SelectedCategory, out var methods))
        {
            return [];
        }

        var providers = string.Equals(
                invocation.SelectedCategory,
                BenchmarkHarnessRunner.V09MemoryReadCategory,
                StringComparison.Ordinal)
            ? new[] { "memory" }
            : invocation.ConfiguredProviderIds.ToArray();

        return methods
            .SelectMany(method => providers.Select(provider => new BenchmarkTarget(
                method,
                provider,
                BenchmarkHarnessRunner.GetScenarioCategory(method))))
            .OrderBy(static target => target.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static BenchmarkArtifactReference CreateArtifactReference(
        string repositoryRoot,
        string kind,
        string path)
    {
        var artifactRoot = GetArtifactRoot(repositoryRoot);
        var fullPath = Path.GetFullPath(path);
        if (!IsArtifactFile(fullPath, artifactRoot))
            throw new InvalidDataException($"Benchmark artifact '{fullPath}' is missing or unsafe.");

        var file = new FileInfo(fullPath);
        return new BenchmarkArtifactReference(
            kind,
            fullPath,
            Path.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/'),
            file.Length,
            ComputeFileSha256(fullPath));
    }

    public static BenchmarkHistoryArtifact CreateHistory(BenchmarkHistoryCreationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rows = input.Rows.ToArray();
        var expectedTargets = ResolveExpectedTargets(input.Invocation);
        var observedTargets = rows
            .Select(static row => new BenchmarkTarget(row.Method, row.ProviderName, row.Category))
            .OrderBy(static target => target.Id, StringComparer.Ordinal)
            .ToArray();
        var duplicateTargets = observedTargets
            .GroupBy(static target => target.Id, StringComparer.Ordinal)
            .Any(static group => group.Count() != 1);
        var expectedIds = expectedTargets.Select(static target => target.Id).ToArray();
        var observedIds = observedTargets.Select(static target => target.Id).ToArray();
        var exactTargetSet = expectedTargets.Count > 0 &&
                             expectedIds.SequenceEqual(observedIds, StringComparer.Ordinal);
        var validRows = rows.Count(row => IsCompleteRow(row, input.Invocation));
        var invalidRows = rows.Length - validRows;
        var telemetryRows = rows.Count(static row => row.TelemetryDelta is not null);
        var rowsComplete = rows.Length > 0 && invalidRows == 0 && !duplicateTargets;
        var commandsComplete = CommandsAreComplete(input.Invocation, input.Commands, input.RunId);
        var artifactFilesComplete = ValidateArtifactReferences(
            input.Invocation.RepositoryRoot,
            input.Artifacts.Files,
            input.Invocation.RunArtifactsDirectory);
        var requiredArtifactsPresent = RequiredArtifactsPresent(
            input.Artifacts.Files,
            input.Commands,
            rows.Length);
        var historyPathSafe = input.Artifacts.HistoryJsonPath is not null &&
                              IsArtifactOutputPath(
                                  input.Artifacts.HistoryJsonPath,
                                  GetArtifactRoot(input.Invocation.RepositoryRoot));
        var artifactPathsMatch = PathsEqual(
                                     input.Artifacts.HistoryJsonPath,
                                     input.Invocation.HistoryJsonPath) &&
                                 PathsEqual(
                                     input.Artifacts.ComparisonJsonPath,
                                     input.Invocation.ComparisonJsonPath);
        var artifactsComplete = artifactFilesComplete && requiredArtifactsPresent && historyPathSafe &&
                                artifactPathsMatch &&
                                CommandLogsAreReferenced(input.Commands, input.Artifacts.Files);
        var scopeComplete = expectedTargets.Count == 0 || exactTargetSet;
        var complete = input.Failure is null && rowsComplete && scopeComplete && commandsComplete;
        var warnings = BuildHistoryWarnings(rows, input.Invocation, input.Warnings);
        var reviewRequired = warnings.Count > 0;
        var canonicalInvocation = IsCanonicalEvidenceInvocation(input.Invocation, expectedTargets);
        var runnerRecomputed = EvaluateRunnerEvidence(
            input.RunnerEvidence.Start,
            input.RunnerEvidence.End,
            input.RunnerEvidence.EntryAssembly,
            input.RunnerEvidence.DevToolsAssembly,
            input.RunnerEvidence.BenchmarkAssembly,
            input.RunnerEvidence.BenchmarkTargetStart,
            input.RunnerEvidence.BenchmarkTargetEnd);
        var validForEvidence = complete && artifactsComplete && exactTargetSet &&
                               canonicalInvocation &&
                               MetadataIsComplete(input.Metadata, input.Invocation, runnerRecomputed) &&
                               RunnerEvidenceMatches(input.RunnerEvidence, runnerRecomputed) &&
                               runnerRecomputed.ValidForEvidence &&
                               InvocationPathsAreComplete(
                                   input.Invocation.RepositoryRoot,
                                   input.Invocation,
                                   input.RunId,
                                   input.RunnerEvidence.BenchmarkAssembly,
                                   requireCurrentBenchmarkAssembly: true) &&
                               ValidateBenchmarkAssemblyEvidence(
                                   input.Invocation.RepositoryRoot,
                                   input.RunnerEvidence.BenchmarkAssembly,
                                   verifyCurrentFile: true);
        var outcome = input.Failure is not null
            ? BenchmarkEvidenceOutcomes.Error
            : !complete
                ? BenchmarkEvidenceOutcomes.Incomplete
                : reviewRequired
                    ? BenchmarkEvidenceOutcomes.ReviewRequired
                    : BenchmarkEvidenceOutcomes.Passed;

        return new BenchmarkHistoryArtifact
        {
            SchemaVersion = BenchmarkEvidenceSchemas.HistoryVersion,
            SchemaId = BenchmarkEvidenceSchemas.HistoryId,
            RunId = input.RunId,
            GeneratedAtUtc = input.CompletedAtUtc,
            StartedAtUtc = input.StartedAtUtc,
            CompletedAtUtc = input.CompletedAtUtc,
            DurationSeconds = Math.Max(0d, (input.CompletedAtUtc - input.StartedAtUtc).TotalSeconds),
            Metadata = input.Metadata,
            Invocation = input.Invocation,
            Outcome = outcome,
            OverallExitCode = outcome is BenchmarkEvidenceOutcomes.Error or BenchmarkEvidenceOutcomes.Incomplete ||
                              input.Invocation.ReleaseEvidenceIntent && !validForEvidence
                ? 1
                : 0,
            IsCompleteForInvocation = complete,
            ArtifactsComplete = artifactsComplete,
            ValidForEvidence = validForEvidence,
            ReviewRequired = reviewRequired,
            Summary = new BenchmarkHistorySummary(
                expectedTargets.Count,
                observedTargets.Length,
                validRows,
                invalidRows,
                telemetryRows,
                warnings.Count,
                expectedTargets.Count > 0,
                exactTargetSet,
                rowsComplete),
            ExpectedTargets = expectedTargets,
            ObservedTargets = observedTargets,
            Commands = input.Commands.ToArray(),
            Warnings = warnings,
            Failure = input.Failure,
            Artifacts = input.Artifacts,
            RunnerEvidence = input.RunnerEvidence,
            RowAggregateSha256 = ComputeRowAggregate(rows),
            Rows = rows
        };
    }

    public static BenchmarkRunnerEvidence EvaluateRunnerEvidence(
        TestRunSummaryRepositoryState start,
        TestRunSummaryRepositoryState end,
        TestRunSummaryRunnerAssembly entryAssembly,
        TestRunSummaryRunnerAssembly devToolsAssembly,
        BenchmarkAssemblyEvidence benchmarkAssembly,
        TestRunSummaryRepositoryState? benchmarkTargetStart = null,
        TestRunSummaryRepositoryState? benchmarkTargetEnd = null)
    {
        var toolingChanged = RepositoryStateChanged(start, end);
        var targetStart = benchmarkTargetStart ?? start;
        var targetEnd = benchmarkTargetEnd ?? end;
        var targetChanged = RepositoryStateChanged(targetStart, targetEnd);
        var changed = toolingChanged || targetChanged;
        var toolingCommit = start.Commit;
        var targetCommit = targetStart.Commit;
        var toolingAssembliesMatch = IsCleanAssemblyIdentity(entryAssembly, "DataLinq.Benchmark.CLI", toolingCommit) &&
                                     IsCleanAssemblyIdentity(devToolsAssembly, "DataLinq.DevTools", toolingCommit);
        var benchmarkAssemblyMatchesTarget = IsCleanAssemblyIdentity(
            benchmarkAssembly.Identity,
            "DataLinq.Benchmark",
            targetCommit);
        var assembliesMatch = toolingAssembliesMatch && benchmarkAssemblyMatchesTarget;
        var builtClean = string.Equals(entryAssembly.RepositoryBuildState, "clean", StringComparison.Ordinal) &&
                         string.Equals(devToolsAssembly.RepositoryBuildState, "clean", StringComparison.Ordinal) &&
                         string.Equals(benchmarkAssembly.Identity.RepositoryBuildState, "clean", StringComparison.Ordinal);
        var valid = start.Captured && end.Captured && targetStart.Captured && targetEnd.Captured &&
                    !start.Dirty && !end.Dirty && !targetStart.Dirty && !targetEnd.Dirty && !changed &&
                    assembliesMatch && builtClean && IsFullCommit(toolingCommit) && IsFullCommit(targetCommit) &&
                    benchmarkAssembly.Sha256.Length == 64;
        return new BenchmarkRunnerEvidence(
            start,
            end,
            entryAssembly,
            devToolsAssembly,
            benchmarkAssembly,
            changed,
            assembliesMatch,
            builtClean,
            valid)
        {
            BenchmarkTargetStart = benchmarkTargetStart,
            BenchmarkTargetEnd = benchmarkTargetEnd,
            BenchmarkTargetStateChangedDuringRun = targetChanged,
            BenchmarkAssemblyMatchesTarget = benchmarkAssemblyMatchesTarget
        };
    }

    private static bool RepositoryStateChanged(
        TestRunSummaryRepositoryState start,
        TestRunSummaryRepositoryState end) =>
        !start.Captured || !end.Captured ||
                      !string.Equals(start.Commit, end.Commit, StringComparison.Ordinal) ||
                      !string.Equals(start.Branch, end.Branch, StringComparison.Ordinal) ||
                      start.Dirty != end.Dirty ||
                      !string.Equals(start.StatusSha256, end.StatusSha256, StringComparison.Ordinal);

    public static BenchmarkAssemblyEvidence CaptureAssemblyEvidence(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
        {
            return new BenchmarkAssemblyEvidence(
                fullPath,
                string.Empty,
                new TestRunSummaryRunnerAssembly("unknown", "unknown", "unknown", false, "unknown"));
        }

        var hash = ComputeFileSha256(fullPath);
        var loadContext = new AssemblyLoadContext($"benchmark-evidence-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(fullPath);
            return new BenchmarkAssemblyEvidence(
                fullPath,
                hash,
                TestRunSummaryReporter.CaptureRunnerAssembly(assembly));
        }
        catch
        {
            return new BenchmarkAssemblyEvidence(
                fullPath,
                hash,
                new TestRunSummaryRunnerAssembly("unknown", "unknown", "unknown", false, "unknown"));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    public static void WriteHistory(string repositoryRoot, string path, BenchmarkHistoryArtifact artifact) =>
        WriteAtomic(repositoryRoot, path, artifact);

    public static void WriteComparison(string repositoryRoot, string path, BenchmarkComparisonArtifact artifact) =>
        WriteAtomic(repositoryRoot, path, artifact);

    public static BenchmarkHistoryReadResult ReadHistory(string repositoryRoot, string path)
    {
        ValidateBaselinePath(repositoryRoot, path);
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (file.Length <= 0 || file.Length > MaximumHistoryBytes)
            throw new InvalidDataException($"Benchmark history '{fullPath}' has an unsupported size.");

        var jsonBytes = File.ReadAllBytes(fullPath);
        using var document = JsonDocument.Parse(jsonBytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64
        });
        RejectDuplicateJsonProperties(document.RootElement);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(nameof(BenchmarkHistoryArtifact.SchemaVersion), out var schemaElement) ||
            schemaElement.ValueKind != JsonValueKind.Number ||
            !schemaElement.TryGetInt32(out var schemaVersion) ||
            schemaVersion is < 1 or > BenchmarkEvidenceSchemas.HistoryVersion)
        {
            throw new InvalidDataException("The benchmark history schema is missing or unsupported.");
        }

        var artifact = JsonSerializer.Deserialize<BenchmarkHistoryArtifact>(jsonBytes, JsonOptions)
            ?? throw new InvalidDataException($"Benchmark history '{fullPath}' is empty.");
        if (artifact.Rows is null || artifact.Rows.Any(static row => row is null))
            throw new InvalidDataException("The benchmark history rows are missing.");
        if (schemaVersion < BenchmarkEvidenceSchemas.HistoryVersion)
            artifact = NormalizeLegacyHistory(artifact);
        ValidateHistoryStructure(artifact, schemaVersion);
        var aggregate = ComputeRowAggregate(artifact.Rows);
        if (schemaVersion == BenchmarkEvidenceSchemas.HistoryVersion &&
            !string.Equals(artifact.RowAggregateSha256, aggregate, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The benchmark history row aggregate does not match its rows.");
        }

        var sourceValid = schemaVersion == BenchmarkEvidenceSchemas.HistoryVersion &&
                          RevalidateCurrentHistory(repositoryRoot, fullPath, artifact, aggregate);
        var reference = new BenchmarkHistoryReference(
            fullPath,
            Convert.ToHexString(SHA256.HashData(jsonBytes)).ToLowerInvariant(),
            jsonBytes.LongLength,
            schemaVersion,
            artifact.SchemaId,
            artifact.RunId,
            artifact.GeneratedAtUtc,
            artifact.Metadata.Commit,
            artifact.Metadata.Profile,
            artifact.Metadata.Filter,
            artifact.Rows.Count,
            aggregate,
            schemaVersion < BenchmarkEvidenceSchemas.HistoryVersion,
            sourceValid)
        {
            SelectedCategory = ResolveReferenceCategory(artifact),
            ExpectedJob = artifact.Invocation?.ExpectedJob ?? ResolveExpectedJob(artifact.Metadata.Profile),
            ConfiguredProviderIds = artifact.Invocation?.ConfiguredProviderIds.ToArray() ?? artifact.Rows
                .Select(static row => row.ProviderName)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            ExpectedTargetIds = (artifact.ExpectedTargets is { Count: > 0 }
                    ? artifact.ExpectedTargets.Select(static target => target.Id)
                    : artifact.Rows.Select(static row =>
                        $"{row.Category}|{row.ProviderName}|{row.Method}"))
                .Order(StringComparer.Ordinal)
                .ToArray(),
            RunnerOs = artifact.Metadata.RunnerOs,
            RunnerArchitecture = artifact.Metadata.RunnerArchitecture,
            RuntimeDescription = artifact.Metadata.RuntimeDescription,
            ProcessorCount = artifact.Metadata.ProcessorCount,
            ProcessorIdentifier = artifact.Metadata.ProcessorIdentifier,
            BenchmarkDotNetVersion = artifact.Metadata.BenchmarkDotNetVersion,
            Outcome = artifact.Outcome,
            IsCompleteForInvocation = artifact.IsCompleteForInvocation,
            ArtifactsComplete = artifact.ArtifactsComplete,
            ReviewRequired = artifact.ReviewRequired
        };
        return new BenchmarkHistoryReadResult(artifact, reference);
    }

    private static string? ResolveReferenceCategory(BenchmarkHistoryArtifact artifact)
    {
        if (artifact.Invocation?.SelectedCategory is not null)
            return artifact.Invocation.SelectedCategory;
        var groups = artifact.Rows
            .Select(static row => row.TrackingGroup)
            .Where(static group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return groups.Length == 1 ? groups[0] : null;
    }

    public static BenchmarkComparisonArtifact CreateComparison(
        string repositoryRoot,
        BenchmarkHistoryReadResult baseline,
        BenchmarkHistoryReadResult candidate,
        string? comparisonPath,
        double warningThresholdPercent,
        bool releaseEvidenceIntent)
    {
        ValidateThreshold(warningThresholdPercent);
        var startedAtUtc = DateTime.UtcNow;
        var baselineRows = ToUniqueRows(baseline.Artifact.Rows, "baseline");
        var candidateRows = ToUniqueRows(candidate.Artifact.Rows, "candidate");
        var allKeys = baselineRows.Keys
            .Union(candidateRows.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var profilesCompatible = !string.IsNullOrWhiteSpace(baseline.Artifact.Metadata.Profile) &&
                                 !string.IsNullOrWhiteSpace(candidate.Artifact.Metadata.Profile) &&
                                 string.Equals(
                                     baseline.Artifact.Metadata.Profile,
                                     candidate.Artifact.Metadata.Profile,
                                     StringComparison.OrdinalIgnoreCase);
        var filtersCompatible = string.Equals(
            baseline.Artifact.Metadata.Filter,
            candidate.Artifact.Metadata.Filter,
            StringComparison.Ordinal);
        var currentScopesCompatible = AreCurrentScopesCompatible(baseline.Artifact, candidate.Artifact);
        var globalScopeCompatible = filtersCompatible && currentScopesCompatible;
        var rows = new List<BenchmarkComparisonArtifactRow>(allKeys.Length);

        foreach (var key in allKeys)
        {
            baselineRows.TryGetValue(key, out var baselineRow);
            candidateRows.TryGetValue(key, out var candidateRow);
            rows.Add(CreateComparisonRow(
                baselineRow,
                candidateRow,
                warningThresholdPercent,
                profilesCompatible,
                globalScopeCompatible,
                baseline.Reference.LegacySchema));
        }

        var counts = CountStatuses(rows);
        var comparable = profilesCompatible && globalScopeCompatible && rows.Count > 0 &&
                         counts.MissingBaseline == 0 &&
                         counts.MissingCandidate == 0 &&
                         counts.ProfileMismatch == 0 &&
                         counts.ScopeMismatch == 0 &&
                         counts.Invalid == 0;
        var reviewRequired = comparable &&
                             (baseline.Reference.LegacySchema ||
                              baseline.Reference.ReviewRequired ||
                              candidate.Reference.ReviewRequired ||
                              counts.Warning > 0 ||
                              counts.Noisy > 0 ||
                              counts.TelemetryChanges > 0);
        var artifactRoot = GetArtifactRoot(repositoryRoot);
        var artifactsComplete = IsArtifactFile(baseline.Reference.Path, artifactRoot) &&
                                IsArtifactFile(candidate.Reference.Path, artifactRoot) &&
                                comparisonPath is not null &&
                                IsArtifactOutputPath(comparisonPath, artifactRoot) &&
                                string.Equals(ComputeFileSha256(baseline.Reference.Path), baseline.Reference.Sha256, StringComparison.Ordinal) &&
                                string.Equals(ComputeFileSha256(candidate.Reference.Path), candidate.Reference.Sha256, StringComparison.Ordinal);
        var validForEvidence = comparable && artifactsComplete &&
                               baseline.Reference.SourceValidForEvidence &&
                               candidate.Reference.SourceValidForEvidence;
        var outcome = !comparable
            ? BenchmarkEvidenceOutcomes.Incomplete
            : reviewRequired
                ? BenchmarkEvidenceOutcomes.ReviewRequired
                : BenchmarkEvidenceOutcomes.Passed;
        var exitCode = !comparable || releaseEvidenceIntent && !validForEvidence ? 1 : 0;
        var completedAtUtc = DateTime.UtcNow;

        return new BenchmarkComparisonArtifact
        {
            SchemaVersion = BenchmarkEvidenceSchemas.ComparisonVersion,
            SchemaId = BenchmarkEvidenceSchemas.ComparisonId,
            GeneratedAtUtc = completedAtUtc,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            WarningThresholdPercent = warningThresholdPercent,
            WarningCount = counts.Warning,
            Baseline = baseline.Artifact.Metadata,
            Candidate = candidate.Artifact.Metadata,
            BaselineRunId = baseline.Artifact.RunId,
            CandidateRunId = candidate.Artifact.RunId,
            Invocation = new BenchmarkComparisonInvocation(
                baseline.Reference.Path,
                candidate.Reference.Path,
                comparisonPath is null ? null : Path.GetFullPath(comparisonPath),
                warningThresholdPercent,
                releaseEvidenceIntent),
            BaselineArtifact = baseline.Reference,
            CandidateArtifact = candidate.Reference,
            Outcome = outcome,
            OverallExitCode = exitCode,
            IsComplete = comparable,
            ArtifactsComplete = artifactsComplete,
            Comparable = comparable,
            ReviewRequired = reviewRequired,
            ValidForEvidence = validForEvidence,
            StatusCounts = counts,
            Rows = rows
        };
    }

    public static BenchmarkComparisonArtifact CreateErrorComparison(
        BenchmarkComparisonInvocation invocation,
        BenchmarkHistoryReadResult? baseline,
        BenchmarkHistoryReadResult? candidate,
        Exception exception)
    {
        var now = DateTime.UtcNow;
        return new BenchmarkComparisonArtifact
        {
            SchemaVersion = BenchmarkEvidenceSchemas.ComparisonVersion,
            SchemaId = BenchmarkEvidenceSchemas.ComparisonId,
            GeneratedAtUtc = now,
            StartedAtUtc = now,
            CompletedAtUtc = now,
            WarningThresholdPercent = invocation.WarningThresholdPercent,
            Baseline = baseline?.Artifact.Metadata ?? new BenchmarkRunMetadata(
                null, null, null, null, null, null, null, null, null, "unknown", "unknown"),
            Candidate = candidate?.Artifact.Metadata ?? new BenchmarkRunMetadata(
                null, null, null, null, null, null, null, null, null, "unknown", "unknown"),
            BaselineRunId = baseline?.Artifact.RunId,
            CandidateRunId = candidate?.Artifact.RunId,
            Invocation = invocation,
            BaselineArtifact = baseline?.Reference,
            CandidateArtifact = candidate?.Reference,
            Outcome = BenchmarkEvidenceOutcomes.Error,
            OverallExitCode = 1,
            IsComplete = false,
            ArtifactsComplete = false,
            Comparable = false,
            ReviewRequired = false,
            ValidForEvidence = false,
            StatusCounts = new BenchmarkComparisonStatusCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            Failure = new BenchmarkFailure(
                "comparison",
                exception.GetType().FullName ?? exception.GetType().Name,
                TestRunSummaryReporter.SanitizeFailureMessage(exception.Message))
        };
    }

    public static void ValidateThreshold(double value)
    {
        if (!double.IsFinite(value) || value <= 0d || value > 1000d)
            throw new InvalidDataException("The benchmark warning threshold must be finite and greater than zero (maximum 1000).");
    }

    private static void ValidateHistoryStructure(BenchmarkHistoryArtifact artifact, int schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(artifact.RunId) || artifact.RunId.Length > 256 ||
            artifact.GeneratedAtUtc == default ||
            artifact.Metadata is null ||
            string.IsNullOrWhiteSpace(artifact.Metadata.Profile) ||
            string.IsNullOrWhiteSpace(artifact.Metadata.Filter) ||
            artifact.Rows is null ||
            artifact.Rows.Count is 0 or > MaximumHistoryRows)
        {
            throw new InvalidDataException("The benchmark history is missing required identity or rows.");
        }
        _ = ResolveExpectedJob(artifact.Metadata.Profile);
        _ = ToUniqueRows(artifact.Rows, "history");
        if (artifact.Rows.Any(static row => row is null || !IsComparableRow(row)))
            throw new InvalidDataException("The benchmark history contains an invalid or incomplete measurement row.");

        if (schemaVersion == BenchmarkEvidenceSchemas.HistoryVersion)
        {
            if (!string.Equals(artifact.SchemaId, BenchmarkEvidenceSchemas.HistoryId, StringComparison.Ordinal) ||
                artifact.Invocation is null ||
                artifact.Summary is null ||
                artifact.Artifacts is null ||
                artifact.Artifacts.Files is null ||
                artifact.RunnerEvidence is null ||
                artifact.ExpectedTargets is null ||
                artifact.ObservedTargets is null ||
                artifact.Commands is null ||
                artifact.Warnings is null ||
                artifact.Invocation.ConfiguredProviderIds is null ||
                artifact.Invocation.AdditionalArguments is null ||
                artifact.Artifacts.Files.Any(static item => item is null) ||
                artifact.ExpectedTargets.Any(static item => item is null) ||
                artifact.ObservedTargets.Any(static item => item is null) ||
                artifact.Commands.Any(static item =>
                    item is null ||
                    item.Arguments is null ||
                    item.Environment is null ||
                    item.Environment.ProviderIds is null) ||
                artifact.Warnings.Any(static item => item is null) ||
                artifact.StartedAtUtc is null ||
                artifact.CompletedAtUtc is null ||
                artifact.CompletedAtUtc < artifact.StartedAtUtc ||
                artifact.DurationSeconds is null ||
                !double.IsFinite(artifact.DurationSeconds.Value) ||
                artifact.DurationSeconds < 0d ||
                !IsSha256(artifact.RowAggregateSha256) ||
                artifact.Outcome is not (BenchmarkEvidenceOutcomes.Passed or BenchmarkEvidenceOutcomes.ReviewRequired or BenchmarkEvidenceOutcomes.Incomplete or BenchmarkEvidenceOutcomes.Error))
            {
                throw new InvalidDataException("The benchmark v3 history contract is incomplete.");
            }
        }
    }

    private static BenchmarkHistoryArtifact NormalizeLegacyHistory(BenchmarkHistoryArtifact artifact) =>
        artifact with
        {
            Rows = artifact.Rows.Select(static row => row with
            {
                Category = string.IsNullOrWhiteSpace(row.Category)
                    ? BenchmarkHarnessRunner.GetScenarioCategory(row.Method)
                    : row.Category,
                OperationsPerInvoke = row.OperationsPerInvoke ?? row.TelemetryDelta?.OperationsPerInvoke,
                TrackingGroup = string.IsNullOrWhiteSpace(row.TrackingGroup)
                    ? BenchmarkHarnessRunner.GetTrackingGroup(row.Method)
                    : row.TrackingGroup
            }).ToArray()
        };

    private static bool RevalidateCurrentHistory(
        string repositoryRoot,
        string sourcePath,
        BenchmarkHistoryArtifact artifact,
        string aggregate)
    {
        var invocation = artifact.Invocation!;
        var artifacts = artifact.Artifacts!;
        var runner = artifact.RunnerEvidence!;
        var expectedTargets = ResolveExpectedTargets(invocation);
        var observedTargets = artifact.Rows
            .Select(static row => new BenchmarkTarget(row.Method, row.ProviderName, row.Category))
            .OrderBy(static target => target.Id, StringComparer.Ordinal)
            .ToArray();
        var exactTargets = expectedTargets.Count > 0 && expectedTargets
            .Select(static target => target.Id)
            .SequenceEqual(observedTargets.Select(static target => target.Id), StringComparer.Ordinal);
        var expectedTargetsRecorded = TargetListsEqual(artifact.ExpectedTargets, expectedTargets);
        var observedTargetsRecorded = TargetListsEqual(artifact.ObservedTargets, observedTargets);
        var validRowCount = artifact.Rows.Count(row => IsCompleteRow(row, invocation));
        var telemetryRowCount = artifact.Rows.Count(static row => row.TelemetryDelta is not null);
        var rowsComplete = artifact.Rows.Count > 0 && validRowCount == artifact.Rows.Count;
        var summaryMatches = artifact.Summary is
        {
            ExpectedScopeKnown: true,
            ExactTargetSet: true,
            RowsComplete: true,
            InvalidRowCount: 0
        } summary &&
            summary.ExpectedTargetCount == expectedTargets.Count &&
            summary.ObservedTargetCount == observedTargets.Length &&
            summary.MeasuredRowCount == validRowCount &&
            summary.TelemetryRowCount == telemetryRowCount &&
            summary.WarningCount == artifact.Warnings.Count;
        var expectedTelemetryWarnings = BuildTelemetryShapeWarnings(artifact.Rows, invocation);
        var recordedTelemetryWarnings = artifact.Warnings
            .Where(static warning => string.Equals(warning.Kind, "TelemetryShape", StringComparison.Ordinal))
            .ToArray();
        var warningsComplete = artifact.Warnings.Count <= 100 && artifact.Warnings.All(static warning =>
            warning is not null &&
            warning.Kind is "BenchmarkDotNet" or "TelemetryShape" &&
            !string.IsNullOrWhiteSpace(warning.Message) &&
            string.Equals(
                TestRunSummaryReporter.SanitizeFailureMessage(warning.Message),
                warning.Message,
                StringComparison.Ordinal)) &&
            artifact.Warnings
                .Select(static warning => $"{warning.Kind}\u001f{warning.Message}")
                .Distinct(StringComparer.Ordinal)
                .Count() == artifact.Warnings.Count &&
            recordedTelemetryWarnings.SequenceEqual(expectedTelemetryWarnings);
        var reviewRequired = artifact.Warnings.Count > 0;
        var expectedOutcome = reviewRequired
            ? BenchmarkEvidenceOutcomes.ReviewRequired
            : BenchmarkEvidenceOutcomes.Passed;
        var timeComplete = artifact.StartedAtUtc is not null &&
                           artifact.CompletedAtUtc is not null &&
                           artifact.GeneratedAtUtc == artifact.CompletedAtUtc &&
                           Math.Abs(
                               artifact.DurationSeconds!.Value -
                               (artifact.CompletedAtUtc.Value - artifact.StartedAtUtc.Value).TotalSeconds) < 0.001d;
        var metadataComplete = MetadataIsComplete(artifact.Metadata, invocation, runner);
        var artifactPathsMatch = PathsEqual(artifacts.HistoryJsonPath, sourcePath) &&
                                 PathsEqual(invocation.HistoryJsonPath, sourcePath) &&
                                 PathsEqual(artifacts.HistoryJsonPath, invocation.HistoryJsonPath) &&
                                 PathsEqual(artifacts.ComparisonJsonPath, invocation.ComparisonJsonPath);
        var artifactFilesComplete = ValidateArtifactReferences(
            repositoryRoot,
            artifacts.Files,
            invocation.RunArtifactsDirectory);
        var requiredArtifactsPresent = RequiredArtifactsPresent(
            artifacts.Files,
            artifact.Commands,
            artifact.Rows.Count);
        var runnerRecomputed = EvaluateRunnerEvidence(
            runner.Start,
            runner.End,
            runner.EntryAssembly,
            runner.DevToolsAssembly,
            runner.BenchmarkAssembly,
            runner.BenchmarkTargetStart,
            runner.BenchmarkTargetEnd);
        return artifact.ValidForEvidence &&
               artifact.IsCompleteForInvocation &&
               artifact.ArtifactsComplete &&
               artifact.OverallExitCode == 0 &&
               artifact.Failure is null &&
               artifact.ReviewRequired == reviewRequired &&
               string.Equals(artifact.Outcome, expectedOutcome, StringComparison.Ordinal) &&
               timeComplete &&
               metadataComplete &&
               artifactPathsMatch &&
               string.Equals(artifact.RowAggregateSha256, aggregate, StringComparison.Ordinal) &&
               IsCanonicalEvidenceInvocation(invocation, expectedTargets) &&
               InvocationPathsAreComplete(
                   repositoryRoot,
                   invocation,
                   artifact.RunId,
                   runner.BenchmarkAssembly,
                   requireCurrentBenchmarkAssembly: false) &&
               exactTargets &&
               expectedTargetsRecorded &&
               observedTargetsRecorded &&
               rowsComplete &&
               summaryMatches &&
               warningsComplete &&
               CommandsAreComplete(invocation, artifact.Commands, artifact.RunId) &&
               artifactFilesComplete &&
               requiredArtifactsPresent &&
               CommandLogsAreReferenced(artifact.Commands, artifacts.Files) &&
               RunnerEvidenceMatches(runner, runnerRecomputed) &&
               runnerRecomputed.ValidForEvidence &&
               ValidateBenchmarkAssemblyEvidence(
                   repositoryRoot,
                   runner.BenchmarkAssembly,
                   verifyCurrentFile: false);
    }

    private static Dictionary<string, BenchmarkHistoryArtifactRow> ToUniqueRows(
        IReadOnlyList<BenchmarkHistoryArtifactRow> rows,
        string source)
    {
        var result = new Dictionary<string, BenchmarkHistoryArtifactRow>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = TargetKey(row.Method, row.ProviderName);
            if (!result.TryAdd(key, row))
                throw new InvalidDataException($"The benchmark {source} contains duplicate row '{row.Method}/{row.ProviderName}'.");
        }
        return result;
    }

    private static BenchmarkComparisonArtifactRow CreateComparisonRow(
        BenchmarkHistoryArtifactRow? baseline,
        BenchmarkHistoryArtifactRow? candidate,
        double threshold,
        bool profilesCompatible,
        bool globalScopeCompatible,
        bool legacyBaseline)
    {
        var method = candidate?.Method ?? baseline?.Method ?? "unknown";
        var provider = candidate?.ProviderName ?? baseline?.ProviderName ?? "unknown";
        var category = candidate?.Category ?? baseline?.Category ?? BenchmarkHarnessRunner.GetScenarioCategory(method);
        var latencyStatus = "invalid";
        var allocationStatus = "invalid";
        var telemetryStatus = "invalid";
        string status;
        double? meanDelta = null;
        double? allocatedDelta = null;
        var maxNoise = new[] { baseline?.NoisePercent, candidate?.NoisePercent }
            .Where(static value => value.HasValue && double.IsFinite(value.Value))
            .Select(static value => value!.Value)
            .DefaultIfEmpty(0d)
            .Max();

        if (!profilesCompatible)
        {
            status = "profile-mismatch";
        }
        else if (baseline is null)
        {
            status = "missing-baseline";
        }
        else if (candidate is null)
        {
            status = "missing-candidate";
        }
        else if (!globalScopeCompatible || !RowsHaveCompatibleScope(baseline, candidate, legacyBaseline))
        {
            status = "scope-mismatch";
        }
        else if (!IsComparableRow(baseline) || !IsComparableRow(candidate))
        {
            status = "invalid";
        }
        else
        {
            meanDelta = GetDeltaPercent(baseline.MeanMicroseconds!.Value, candidate.MeanMicroseconds!.Value);
            allocatedDelta = GetDeltaPercent(baseline.AllocatedBytes!.Value, candidate.AllocatedBytes!.Value);
            latencyStatus = maxNoise >= LatencyNoiseThresholdPercent
                ? "noisy"
                : GetMetricStatus(baseline.MeanMicroseconds.Value, candidate.MeanMicroseconds.Value, meanDelta, threshold);
            allocationStatus = GetMetricStatus(
                baseline.AllocatedBytes.Value,
                candidate.AllocatedBytes.Value,
                allocatedDelta,
                threshold);
            telemetryStatus = Equals(baseline.TelemetryDelta, candidate.TelemetryDelta) ? "stable" : "changed";
            status = latencyStatus == "warning" || allocationStatus == "warning"
                ? "warning"
                : latencyStatus == "noisy"
                    ? "noisy"
                    : latencyStatus == "improved" || allocationStatus == "improved"
                        ? "improved"
                        : "stable";
        }

        return new BenchmarkComparisonArtifactRow(
            method,
            provider,
            category,
            baseline?.MeanMicroseconds,
            candidate?.MeanMicroseconds,
            meanDelta,
            baseline?.AllocatedBytes,
            candidate?.AllocatedBytes,
            allocatedDelta,
            maxNoise,
            candidate?.TrackingGroup ?? baseline?.TrackingGroup,
            status)
        {
            LatencyStatus = latencyStatus,
            AllocationStatus = allocationStatus,
            TelemetryStatus = telemetryStatus,
            BaselineOperationsPerInvoke = baseline?.OperationsPerInvoke,
            CandidateOperationsPerInvoke = candidate?.OperationsPerInvoke,
            BaselineTelemetry = baseline?.TelemetryDelta,
            CandidateTelemetry = candidate?.TelemetryDelta,
            BaselineJob = baseline?.Job,
            CandidateJob = candidate?.Job,
            BaselineRuntime = baseline?.Runtime,
            CandidateRuntime = candidate?.Runtime,
            BaselineJit = baseline?.Jit,
            CandidateJit = candidate?.Jit,
            BaselinePlatform = baseline?.Platform,
            CandidatePlatform = candidate?.Platform,
            BaselineToolchain = baseline?.Toolchain,
            CandidateToolchain = candidate?.Toolchain
        };
    }

    private static bool AreCurrentScopesCompatible(
        BenchmarkHistoryArtifact baseline,
        BenchmarkHistoryArtifact candidate)
    {
        if (baseline.SchemaVersion < BenchmarkEvidenceSchemas.HistoryVersion)
            return true;
        if (baseline.Invocation is null || candidate.Invocation is null)
            return false;
        if (!IsBoundedIdentity(baseline.Metadata.RunnerOs) ||
            !IsBoundedIdentity(candidate.Metadata.RunnerOs) ||
            !IsBoundedIdentity(baseline.Metadata.RunnerArchitecture) ||
            !IsBoundedIdentity(candidate.Metadata.RunnerArchitecture) ||
            !IsBoundedIdentity(baseline.Metadata.RuntimeDescription) ||
            !IsBoundedIdentity(candidate.Metadata.RuntimeDescription) ||
            !IsBoundedIdentity(baseline.Metadata.ProcessorIdentifier) ||
            !IsBoundedIdentity(candidate.Metadata.ProcessorIdentifier) ||
            !IsBoundedIdentity(baseline.Metadata.BenchmarkDotNetVersion) ||
            !IsBoundedIdentity(candidate.Metadata.BenchmarkDotNetVersion) ||
            baseline.Metadata.ProcessorCount <= 0 ||
            candidate.Metadata.ProcessorCount <= 0)
        {
            return false;
        }
        return string.Equals(baseline.Metadata.RunnerOs, candidate.Metadata.RunnerOs, StringComparison.Ordinal) &&
               string.Equals(
                   baseline.Metadata.RunnerArchitecture,
                   candidate.Metadata.RunnerArchitecture,
                   StringComparison.Ordinal) &&
               string.Equals(
                   baseline.Metadata.RuntimeDescription,
                   candidate.Metadata.RuntimeDescription,
                   StringComparison.Ordinal) &&
               string.Equals(
                   baseline.Metadata.ProcessorIdentifier,
                   candidate.Metadata.ProcessorIdentifier,
                   StringComparison.Ordinal) &&
               string.Equals(
                   baseline.Metadata.BenchmarkDotNetVersion,
                   candidate.Metadata.BenchmarkDotNetVersion,
                   StringComparison.Ordinal) &&
               baseline.Metadata.ProcessorCount == candidate.Metadata.ProcessorCount &&
               string.Equals(
                   baseline.Invocation.SelectedCategory,
                   candidate.Invocation.SelectedCategory,
                   StringComparison.Ordinal) &&
               string.Equals(baseline.Invocation.ExpectedJob, candidate.Invocation.ExpectedJob, StringComparison.Ordinal) &&
               baseline.Invocation.ConfiguredProviderIds.SequenceEqual(
                   candidate.Invocation.ConfiguredProviderIds,
                   StringComparer.Ordinal) &&
               baseline.ExpectedTargets.Select(static target => target.Id).SequenceEqual(
                   candidate.ExpectedTargets.Select(static target => target.Id),
                   StringComparer.Ordinal);
    }

    private static bool MetadataIsComplete(
        BenchmarkRunMetadata metadata,
        BenchmarkInvocation invocation,
        BenchmarkRunnerEvidence runner)
    {
        var target = runner.BenchmarkTargetStart ?? runner.Start;
        return string.Equals(metadata.Profile, invocation.Profile, StringComparison.Ordinal) &&
               string.Equals(metadata.Filter, invocation.Filter, StringComparison.Ordinal) &&
               string.Equals(metadata.Commit, target.Commit, StringComparison.Ordinal) &&
               string.Equals(metadata.Branch, target.Branch, StringComparison.Ordinal) &&
               IsBoundedIdentity(metadata.RunnerOs) &&
               IsBoundedIdentity(metadata.RunnerArchitecture) &&
               IsBoundedIdentity(metadata.RuntimeDescription) &&
               IsBoundedIdentity(metadata.ProcessorIdentifier) &&
               IsBoundedIdentity(metadata.BenchmarkDotNetVersion) &&
               metadata.ProcessorCount > 0;
    }

    private static bool RowsHaveCompatibleScope(
        BenchmarkHistoryArtifactRow baseline,
        BenchmarkHistoryArtifactRow candidate,
        bool legacyBaseline)
    {
        if (!string.Equals(baseline.Category, candidate.Category, StringComparison.Ordinal) ||
            !string.Equals(baseline.TrackingGroup, candidate.TrackingGroup, StringComparison.Ordinal) ||
            baseline.OperationsPerInvoke != candidate.OperationsPerInvoke)
        {
            return false;
        }
        if (legacyBaseline)
            return true;
        return string.Equals(baseline.Job, candidate.Job, StringComparison.Ordinal) &&
               string.Equals(baseline.Runtime, candidate.Runtime, StringComparison.Ordinal) &&
               string.Equals(baseline.Jit, candidate.Jit, StringComparison.Ordinal) &&
               string.Equals(baseline.Platform, candidate.Platform, StringComparison.Ordinal) &&
               string.Equals(baseline.Toolchain, candidate.Toolchain, StringComparison.Ordinal);
    }

    private static bool IsComparableRow(BenchmarkHistoryArtifactRow row) =>
        !string.IsNullOrWhiteSpace(row.Method) &&
        !string.IsNullOrWhiteSpace(row.ProviderName) &&
        !string.IsNullOrWhiteSpace(row.Category) &&
        IsPositiveFinite(row.MeanMicroseconds) &&
        IsOptionalNonnegativeFinite(row.ErrorMicroseconds) &&
        IsNonnegativeFinite(row.AllocatedBytes) &&
        IsOptionalNonnegativeFinite(row.NoisePercent) &&
        row.OperationsPerInvoke is > 0 &&
        row.TelemetryDelta is not null &&
        IsCompleteTelemetry(row);

    private static string GetMetricStatus(
        double baseline,
        double candidate,
        double? deltaPercent,
        double threshold)
    {
        if (baseline == 0d)
            return candidate == 0d ? "stable" : "warning";
        if (!deltaPercent.HasValue)
            return "invalid";
        if (deltaPercent.Value >= threshold)
            return "warning";
        if (deltaPercent.Value <= -threshold)
            return "improved";
        return "stable";
    }

    private static double? GetDeltaPercent(double baseline, double candidate)
    {
        if (!double.IsFinite(baseline) || !double.IsFinite(candidate) || baseline == 0d)
            return null;
        return ((candidate - baseline) / baseline) * 100d;
    }

    private static BenchmarkComparisonStatusCounts CountStatuses(
        IReadOnlyList<BenchmarkComparisonArtifactRow> rows) =>
        new(
            rows.Count,
            rows.Count(static row => row.Status == "stable"),
            rows.Count(static row => row.Status == "improved"),
            rows.Count(static row => row.Status == "warning"),
            rows.Count(static row => row.Status == "noisy"),
            rows.Count(static row => row.Status == "missing-baseline"),
            rows.Count(static row => row.Status == "missing-candidate"),
            rows.Count(static row => row.Status == "profile-mismatch"),
            rows.Count(static row => row.Status == "scope-mismatch"),
            rows.Count(static row => row.Status == "invalid"),
            rows.Count(static row => row.LatencyStatus == "warning"),
            rows.Count(static row => row.AllocationStatus == "warning"),
            rows.Count(static row => row.TelemetryStatus == "changed"));

    private static void RejectDuplicateJsonProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Duplicate JSON property '{property.Name}' is not allowed.");
                RejectDuplicateJsonProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                RejectDuplicateJsonProperties(item);
        }
    }

    private static bool IsCanonicalEvidenceInvocation(
        BenchmarkInvocation invocation,
        IReadOnlyList<BenchmarkTarget> expectedTargets)
    {
        if (!InvocationContractIsValid(invocation) ||
            expectedTargets.Count == 0 ||
            !string.Equals(invocation.Profile, "heavy", StringComparison.Ordinal) ||
            !string.Equals(invocation.ExpectedJob, "MediumRun", StringComparison.Ordinal) ||
            !string.Equals(invocation.Filter, "*", StringComparison.Ordinal) ||
            invocation.NoBuild ||
            invocation.AdditionalArguments.Count != 0 ||
            invocation.ArgumentsRedacted ||
            invocation.HistoryJsonPath is null)
        {
            return false;
        }

        if (string.Equals(invocation.SelectedCategory, BenchmarkHarnessRunner.V09MemoryReadCategory, StringComparison.Ordinal))
            return invocation.ConfiguredProviderIds.SequenceEqual(["memory"], StringComparer.Ordinal);

        if (string.Equals(invocation.SelectedCategory, BenchmarkHarnessRunner.AllocationRegressionCategory, StringComparison.Ordinal))
            return invocation.ConfiguredProviderIds.SequenceEqual(["sqlite-memory"], StringComparer.Ordinal);

        return invocation.ConfiguredProviderIds.SequenceEqual(
            new[] { "sqlite-file", "sqlite-memory" },
            StringComparer.Ordinal);
    }

    private static bool InvocationContractIsValid(BenchmarkInvocation invocation)
    {
        if (!string.Equals(invocation.Command, "run", StringComparison.Ordinal) ||
            invocation.HistoryJsonPath is null ||
            invocation.ComparisonJsonPath is not null && invocation.BaselinePath is null ||
            invocation.ReleaseEvidenceIntent &&
            invocation.BaselinePath is not null &&
            invocation.ComparisonJsonPath is null)
        {
            return false;
        }

        var paths = new[]
        {
            invocation.HistoryJsonPath,
            invocation.BaselinePath,
            invocation.ComparisonJsonPath
        }.Where(static path => path is not null).ToArray();
        for (var left = 0; left < paths.Length; left++)
        {
            for (var right = left + 1; right < paths.Length; right++)
            {
                if (PathsEqual(paths[left], paths[right]))
                    return false;
            }
        }
        return true;
    }

    private static bool InvocationPathsAreComplete(
        string repositoryRoot,
        BenchmarkInvocation invocation,
        string runId,
        BenchmarkAssemblyEvidence benchmarkAssembly,
        bool requireCurrentBenchmarkAssembly)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
            var artifactRoot = GetArtifactRoot(root);
            var benchmarkTargetRoot = invocation.BenchmarkTargetRepositoryRoot is null
                ? root
                : Path.TrimEndingDirectorySeparator(Path.GetFullPath(invocation.BenchmarkTargetRepositoryRoot));
            var targetRootIsValid = invocation.BenchmarkTargetRepositoryRoot is null ||
                                    Path.IsPathFullyQualified(invocation.BenchmarkTargetRepositoryRoot) &&
                                    IsSafeDirectory(
                                        benchmarkTargetRoot,
                                        Path.Combine(artifactRoot, "benchmarks", "targets"));
            var compatibilityFilesAreValid = invocation.BenchmarkTargetRepositoryRoot is null ||
                IsRepositoryFile(
                    Path.Combine(root, "src", "DataLinq.Benchmark.CLI", "BenchmarkTargetProvenance.targets"),
                    root) &&
                IsRepositoryFile(
                    Path.Combine(root, "src", "DataLinq.Benchmark.CLI", "HistoricalBenchmarkConfig.cs.txt"),
                    root);
            var expectedProject = Path.Combine(benchmarkTargetRoot, "src", "DataLinq.Benchmark", "DataLinq.Benchmark.csproj");
            var expectedAssembly = Path.Combine(
                benchmarkTargetRoot,
                "src",
                "DataLinq.Benchmark",
                "bin",
                "Release",
                "net8.0",
                "DataLinq.Benchmark.dll");
            var expectedRunDirectory = Path.Combine(root, "artifacts", "benchmarks", "runs", runId);
            if (!Path.IsPathFullyQualified(invocation.RepositoryRoot) ||
                !Path.IsPathFullyQualified(invocation.BenchmarkProjectPath) ||
                !Path.IsPathFullyQualified(invocation.BenchmarkAssemblyPath) ||
                !Path.IsPathFullyQualified(invocation.RunArtifactsDirectory) ||
                !PathsEqual(invocation.RepositoryRoot, root) ||
                !targetRootIsValid ||
                !compatibilityFilesAreValid ||
                !PathsEqual(invocation.BenchmarkProjectPath, expectedProject) ||
                !PathsEqual(invocation.BenchmarkAssemblyPath, expectedAssembly) ||
                !PathsEqual(invocation.RunArtifactsDirectory, expectedRunDirectory) ||
                !PathsEqual(benchmarkAssembly.Path, invocation.BenchmarkAssemblyPath) ||
                !IsRepositoryFile(expectedProject, benchmarkTargetRoot) ||
                requireCurrentBenchmarkAssembly && !IsRepositoryFile(expectedAssembly, benchmarkTargetRoot) ||
                !IsSafeDirectory(expectedRunDirectory, artifactRoot) ||
                invocation.HistoryJsonPath is null ||
                !IsArtifactOutputPath(invocation.HistoryJsonPath, artifactRoot) ||
                invocation.ComparisonJsonPath is not null &&
                !IsArtifactOutputPath(invocation.ComparisonJsonPath, artifactRoot) ||
                invocation.BaselinePath is not null &&
                !IsArtifactFile(invocation.BaselinePath, artifactRoot) ||
                !double.IsFinite(invocation.WarningThresholdPercent) ||
                invocation.WarningThresholdPercent <= 0d ||
                invocation.WarningThresholdPercent > 1000d ||
                !string.Equals(
                    invocation.ExpectedJob,
                    ResolveExpectedJob(invocation.Profile),
                    StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TargetListsEqual(
        IReadOnlyList<BenchmarkTarget> recorded,
        IReadOnlyList<BenchmarkTarget> computed) =>
        recorded.Count == computed.Count &&
        recorded.Select(static target => target?.Id)
            .SequenceEqual(computed.Select(static target => target.Id), StringComparer.Ordinal);

    private static bool RequiredArtifactsPresent(
        IReadOnlyList<BenchmarkArtifactReference> artifacts,
        IReadOnlyList<BenchmarkCommandRecord> commands,
        int rowCount)
    {
        var kinds = artifacts
            .GroupBy(static artifact => artifact.Kind, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var requiredContentFilesAreNonempty = artifacts
            .Where(static artifact => artifact.Kind is
                "summary-json" or
                "benchmarkdotnet-csv" or
                "benchmarkdotnet-markdown" or
                "telemetry-json")
            .All(static artifact => artifact.SizeBytes > 0L);
        return requiredContentFilesAreNonempty &&
               kinds.GetValueOrDefault("summary-json") == 1 &&
               kinds.GetValueOrDefault("benchmarkdotnet-csv") > 0 &&
               kinds.GetValueOrDefault("benchmarkdotnet-markdown") > 0 &&
               kinds.GetValueOrDefault("telemetry-json") == rowCount &&
               commands.All(command => kinds.GetValueOrDefault($"{command.Stage}-log") == 1);
    }

    private static bool RunnerEvidenceMatches(
        BenchmarkRunnerEvidence recorded,
        BenchmarkRunnerEvidence recomputed) =>
        recorded.StateChangedDuringRun == recomputed.StateChangedDuringRun &&
        recorded.AssembliesMatchCheckout == recomputed.AssembliesMatchCheckout &&
        recorded.AssembliesBuiltFromCleanState == recomputed.AssembliesBuiltFromCleanState &&
        (recorded.BenchmarkTargetStart is null ||
         recorded.BenchmarkTargetStateChangedDuringRun == recomputed.BenchmarkTargetStateChangedDuringRun &&
         recorded.BenchmarkAssemblyMatchesTarget == recomputed.BenchmarkAssemblyMatchesTarget) &&
        recorded.ValidForEvidence == recomputed.ValidForEvidence;

    private static IReadOnlyList<BenchmarkWarning> BuildHistoryWarnings(
        IReadOnlyList<BenchmarkHistoryArtifactRow> rows,
        BenchmarkInvocation invocation,
        IReadOnlyList<BenchmarkWarning> benchmarkWarnings) =>
        BuildTelemetryShapeWarnings(rows, invocation)
            .Concat(benchmarkWarnings.Select(static warning => new BenchmarkWarning(
                "BenchmarkDotNet",
                TestRunSummaryReporter.SanitizeFailureMessage(warning.Message))))
            .Where(static warning => !string.IsNullOrWhiteSpace(warning.Message))
            .Distinct()
            .Take(100)
            .ToArray();

    private static IReadOnlyList<BenchmarkWarning> BuildTelemetryShapeWarnings(
        IReadOnlyList<BenchmarkHistoryArtifactRow> rows,
        BenchmarkInvocation invocation)
    {
        if (invocation.SelectedCategory is null ||
            !ReleaseLaneMethods.ContainsKey(invocation.SelectedCategory))
        {
            return [];
        }

        return rows
            .Where(static row => !TelemetryShapeMatchesExpectedWorkload(row))
            .OrderBy(static row => TargetKey(row.Method, row.ProviderName), StringComparer.Ordinal)
            .Select(row => new BenchmarkWarning(
                "TelemetryShape",
                $"Telemetry shape for '{row.Method}'/'{row.ProviderName}' does not contain the expected {invocation.SelectedCategory} workload signals."))
            .ToArray();
    }

    private static bool TelemetryShapeMatchesExpectedWorkload(BenchmarkHistoryArtifactRow row)
    {
        var telemetry = row.TelemetryDelta;
        if (telemetry is null)
            return false;

        return row.Method switch
        {
            "Provider initialization" => true,
            "Startup primary-key fetch" =>
                telemetry.EntityQueriesPerOperation > 0d &&
                telemetry.DatabaseRowsPerOperation > 0d &&
                telemetry.MaterializationsPerOperation > 0d,
            "Warm primary-key fetch" =>
                telemetry.EntityQueriesPerOperation > 0d &&
                telemetry.RowCacheHitsPerOperation > 0d,
            "Repeated non-PK equality fetch" or "Repeated IN predicate fetch" =>
                telemetry.EntityQueriesPerOperation > 0d,
            "Repeated scalar Any" or "SQL adapter scalar Any" =>
                telemetry.ScalarQueriesPerOperation > 0d,
            "Memory database construction" => telemetry.MemoryDatabasesConstructedPerOperation > 0d,
            "Memory construct and seed" =>
                telemetry.MemoryDatabasesConstructedPerOperation > 0d &&
                telemetry.MemoryRowsSeededPerOperation > 0d,
            "Memory primary-key hit" =>
                telemetry.MemoryPrimaryKeyRequestsPerOperation > 0d &&
                telemetry.MemoryPrimaryKeyProbesPerOperation > 0d &&
                telemetry.MemoryCacheLookupsPerOperation > 0d &&
                telemetry.MemoryCacheHitsPerOperation > 0d,
            "Memory primary-key miss" =>
                telemetry.MemoryPrimaryKeyRequestsPerOperation > 0d &&
                telemetry.MemoryPrimaryKeyProbesPerOperation > 0d,
            "Memory scalar scan" => telemetry.MemoryScanRowsVisitedPerOperation > 0d,
            "Memory filter order page" or
            "Memory direct-Guid equality count" or
            "Memory typed-ID equality count" =>
                telemetry.MemoryScanRowsVisitedPerOperation > 0d &&
                telemetry.MemoryPredicateEvaluationsPerOperation > 0d &&
                telemetry.MemoryPredicateRejectionsPerOperation > 0d,
            "Memory repeated entity identity" =>
                telemetry.MemoryScanRowsVisitedPerOperation > 0d &&
                telemetry.MemoryPredicateEvaluationsPerOperation > 0d &&
                telemetry.MemoryPredicateRejectionsPerOperation > 0d &&
                telemetry.MemoryCacheLookupsPerOperation > 0d &&
                telemetry.MemoryCacheHitsPerOperation > 0d,
            _ => true
        };
    }

    private static bool CommandsAreComplete(
        BenchmarkInvocation invocation,
        IReadOnlyList<BenchmarkCommandRecord> commands,
        string runId)
    {
        var expectedCount = invocation.NoBuild ? 1 : 3;
        if (commands.Count != expectedCount ||
            commands.Any(static command =>
                command.ExitCode != 0 ||
                !string.Equals(command.Executable, "dotnet", StringComparison.Ordinal) ||
                command.StartedAtUtc == default ||
                command.CompletedAtUtc < command.StartedAtUtc ||
                !double.IsFinite(command.DurationSeconds) ||
                command.DurationSeconds < 0d ||
                string.IsNullOrWhiteSpace(command.LogPath)))
        {
            return false;
        }

        var benchmarkArguments = new List<string>
        {
            invocation.BenchmarkAssemblyPath,
            "--artifacts",
            invocation.RunArtifactsDirectory,
            "--filter",
            invocation.Filter,
            "--join",
            "--disableLogFile"
        };
        if (invocation.KeepFiles)
            benchmarkArguments.Add("--keepFiles");
        benchmarkArguments.AddRange(BenchmarkHarnessRunner.GetBenchmarkCategoryArguments(invocation.SelectedCategory));
        benchmarkArguments.AddRange(invocation.AdditionalArguments);

        var benchmark = commands[^1];
        var benchmarkDirectory = Path.GetDirectoryName(invocation.BenchmarkProjectPath)
            ?? invocation.RepositoryRoot;
        if (!string.Equals(benchmark.Stage, "benchmark", StringComparison.Ordinal) ||
            !benchmark.Arguments.SequenceEqual(benchmarkArguments, StringComparer.Ordinal) ||
            !PathsEqual(benchmark.WorkingDirectory, benchmarkDirectory) ||
            !CommandEnvironmentMatches(
                benchmark.Environment,
                invocation,
                runId,
                Path.Combine(invocation.RunArtifactsDirectory, "results")))
        {
            return false;
        }

        if (invocation.NoBuild)
            return true;

        var verbosity = invocation.Verbose ? "minimal" : "q";
        var expectedRestoreArguments = new[]
        {
            "restore",
            invocation.BenchmarkProjectPath,
            "-nologo",
            "-v",
            verbosity,
            "-p:NuGetAudit=false"
        };
        var expectedBuildArguments = new List<string>
        {
            "build",
            invocation.BenchmarkProjectPath,
            "--no-restore",
            "-c",
            "Release",
            "-f",
            "net8.0",
            "-nologo",
            "-v",
            verbosity,
            "-p:NuGetAudit=false"
        };
        if (invocation.BenchmarkTargetRepositoryRoot is not null)
        {
            expectedBuildArguments.Add($"-p:CustomAfterMicrosoftCommonTargets={Path.Combine(
                invocation.RepositoryRoot,
                "src",
                "DataLinq.Benchmark.CLI",
                "BenchmarkTargetProvenance.targets")}");
            expectedBuildArguments.Add($"-p:DataLinqBenchmarkTargetRepositoryRoot={invocation.BenchmarkTargetRepositoryRoot}");
            expectedBuildArguments.Add($"-p:DataLinqBenchmarkCompatibilitySource={Path.Combine(
                invocation.RepositoryRoot,
                "src",
                "DataLinq.Benchmark.CLI",
                "HistoricalBenchmarkConfig.cs.txt")}");
        }
        return string.Equals(commands[0].Stage, "restore", StringComparison.Ordinal) &&
               commands[0].Arguments.SequenceEqual(expectedRestoreArguments, StringComparer.Ordinal) &&
               PathsEqual(commands[0].WorkingDirectory, invocation.RepositoryRoot) &&
               CommandEnvironmentMatches(commands[0].Environment, invocation, null, null) &&
               string.Equals(commands[1].Stage, "build", StringComparison.Ordinal) &&
               commands[1].Arguments.SequenceEqual(expectedBuildArguments, StringComparer.Ordinal) &&
               PathsEqual(commands[1].WorkingDirectory, invocation.RepositoryRoot) &&
               CommandEnvironmentMatches(commands[1].Environment, invocation, null, null);
    }

    private static bool CommandEnvironmentMatches(
        BenchmarkCommandEnvironment environment,
        BenchmarkInvocation invocation,
        string? runId,
        string? resultsDirectory) =>
        string.Equals(environment.Profile, runId is null ? null : invocation.Profile, StringComparison.Ordinal) &&
        string.Equals(environment.BenchmarkRunId, runId, StringComparison.Ordinal) &&
        PathsEqual(
            environment.ArtifactsDirectory,
            runId is null ? null : invocation.RunArtifactsDirectory) &&
        PathsEqual(environment.ResultsDirectory, resultsDirectory) &&
        environment.ProviderIds.SequenceEqual(invocation.ConfiguredProviderIds, StringComparer.Ordinal);

    private static bool PathsEqual(string? left, string? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);
        }
        catch
        {
            return false;
        }
    }

    private static bool CommandLogsAreReferenced(
        IReadOnlyList<BenchmarkCommandRecord> commands,
        IReadOnlyList<BenchmarkArtifactReference> artifacts)
    {
        var paths = artifacts.Select(static artifact => artifact.Path).ToHashSet(PathComparer);
        return commands.All(command => paths.Contains(Path.GetFullPath(command.LogPath)));
    }

    private static bool IsCompleteRow(BenchmarkHistoryArtifactRow row, BenchmarkInvocation invocation)
    {
        var isDryRun = string.Equals(invocation.ExpectedJob, "Dry", StringComparison.Ordinal);
        var statisticalMetricsComplete = isDryRun
            ? IsOptionalNonnegativeFinite(row.ErrorMicroseconds) &&
              IsOptionalNonnegativeFinite(row.NoisePercent) &&
              IsOptionalNonnegativeFinite(row.UncertaintyPercent) &&
              IsOptionalNonnegativeFinite(row.StdDevPercent)
            : IsNonnegativeFinite(row.ErrorMicroseconds) &&
              IsNonnegativeFinite(row.NoisePercent) &&
              IsNonnegativeFinite(row.UncertaintyPercent) &&
              IsNonnegativeFinite(row.StdDevPercent);

        return !string.IsNullOrWhiteSpace(row.Method) &&
               !string.IsNullOrWhiteSpace(row.ProviderName) &&
               !string.IsNullOrWhiteSpace(row.Category) &&
               !string.Equals(row.Category, "other", StringComparison.Ordinal) &&
               string.Equals(row.Job, invocation.ExpectedJob, StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(row.Runtime) &&
               !string.IsNullOrWhiteSpace(row.Jit) &&
               !string.IsNullOrWhiteSpace(row.Platform) &&
               !string.IsNullOrWhiteSpace(row.Toolchain) &&
               IsPositiveFinite(row.MeanMicroseconds) &&
               statisticalMetricsComplete &&
               IsOptionalNonnegativeFinite(row.MedianMicroseconds) &&
               IsOptionalNonnegativeFinite(row.StdDevMicroseconds) &&
               IsOptionalNonnegativeFinite(row.MinMicroseconds) &&
               IsOptionalNonnegativeFinite(row.MaxMicroseconds) &&
               IsNonnegativeFinite(row.AllocatedBytes) &&
               row.OperationsPerInvoke is > 0 &&
               (!ReleaseOperationCounts.TryGetValue(row.Method, out var expectedOperations) ||
                row.OperationsPerInvoke == expectedOperations) &&
               (invocation.SelectedCategory is null ||
                !ReleaseLaneMethods.ContainsKey(invocation.SelectedCategory) ||
                string.Equals(row.TrackingGroup, invocation.SelectedCategory, StringComparison.Ordinal)) &&
               IsCompleteTelemetry(row);
    }

    private static bool IsCompleteTelemetry(BenchmarkHistoryArtifactRow row)
    {
        var telemetry = row.TelemetryDelta;
        if (telemetry is null ||
            telemetry.OperationsPerInvoke != row.OperationsPerInvoke ||
            !string.Equals(telemetry.Method, row.Method, StringComparison.Ordinal) ||
            !string.Equals(telemetry.ProviderName, row.ProviderName, StringComparison.Ordinal))
        {
            return false;
        }

        return typeof(BenchmarkTelemetryDeltaArtifact)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.PropertyType == typeof(double))
            .Select(property => (double)property.GetValue(telemetry)!)
            .All(static value => double.IsFinite(value) && value >= 0d);
    }

    private static bool IsCleanAssemblyIdentity(
        TestRunSummaryRunnerAssembly assembly,
        string expectedName,
        string? commit) =>
        string.Equals(assembly.Name, expectedName, StringComparison.Ordinal) &&
        assembly.RepositoryCommitCaptured &&
        string.Equals(assembly.RepositoryCommit, commit, StringComparison.Ordinal);

    private static bool ValidateBenchmarkAssemblyEvidence(
        string repositoryRoot,
        BenchmarkAssemblyEvidence evidence,
        bool verifyCurrentFile)
    {
        try
        {
            var fullPath = Path.GetFullPath(evidence.Path);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
            var relativePath = Path.GetRelativePath(root, fullPath);
            if (!Path.IsPathFullyQualified(evidence.Path) ||
                Path.IsPathRooted(relativePath) ||
                relativePath.Equals("..", StringComparison.Ordinal) ||
                relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) ||
                !IsSha256(evidence.Sha256))
            {
                return false;
            }
            return !verifyCurrentFile ||
                   IsRepositoryFile(fullPath, root) &&
                   string.Equals(ComputeFileSha256(fullPath), evidence.Sha256, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFullCommit(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsBoundedIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 512 &&
        !value.Contains("[REDACTED]", StringComparison.Ordinal) &&
        string.Equals(
            TestRunSummaryReporter.SanitizeFailureMessage(value),
            value,
            StringComparison.Ordinal);

    private static bool IsPositiveFinite(double? value) =>
        value.HasValue && double.IsFinite(value.Value) && value.Value > 0d;

    private static bool IsNonnegativeFinite(double? value) =>
        value.HasValue && double.IsFinite(value.Value) && value.Value >= 0d;

    private static bool IsOptionalNonnegativeFinite(double? value) =>
        !value.HasValue || double.IsFinite(value.Value) && value.Value >= 0d;

    private static string? NormalizeOptionalArtifactPath(string repositoryRoot, string? path, string name)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var fullPath = Path.GetFullPath(path);
        if (!IsArtifactOutputPath(fullPath, GetArtifactRoot(repositoryRoot)))
        {
            throw new InvalidDataException(
                $"The benchmark {name} path must remain beneath repository artifacts without reparse-point traversal.");
        }
        return fullPath;
    }

    private static void InvalidateOutput(string repositoryRoot, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsArtifactOutputPath(fullPath, GetArtifactRoot(repositoryRoot)))
            throw new InvalidDataException("Refusing to invalidate an unsafe benchmark report path.");
        if (Directory.Exists(fullPath))
            throw new InvalidDataException($"Benchmark report path '{fullPath}' is a directory.");
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }

    private static void WriteAtomic<T>(string repositoryRoot, string path, T artifact)
    {
        var fullPath = Path.GetFullPath(path);
        var artifactRoot = GetArtifactRoot(repositoryRoot);
        if (!IsArtifactOutputPath(fullPath, artifactRoot))
            throw new InvalidDataException("The benchmark report path is outside the safe artifact tree.");
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("The benchmark report path has no directory.");
        Directory.CreateDirectory(directory);
        if (!IsArtifactOutputPath(fullPath, artifactRoot))
            throw new InvalidDataException("The benchmark report path became unsafe while creating its directory.");

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(artifact, JsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (!IsArtifactOutputPath(fullPath, artifactRoot) ||
                !IsArtifactFile(temporaryPath, artifactRoot))
            {
                throw new InvalidDataException("The benchmark report destination became unsafe before promotion.");
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static bool ValidateArtifactReferences(
        string repositoryRoot,
        IReadOnlyList<BenchmarkArtifactReference> artifacts,
        string? requiredParentDirectory = null)
    {
        if (artifacts.Count == 0)
            return false;
        var artifactRoot = GetArtifactRoot(repositoryRoot);
        var requiredRoot = requiredParentDirectory is null
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(requiredParentDirectory));
        var uniquePaths = new HashSet<string>(PathComparer);
        foreach (var artifact in artifacts)
        {
            var fullPath = Path.GetFullPath(artifact.Path);
            var expectedRelativePath = Path.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/');
            if (artifact is null ||
                string.IsNullOrWhiteSpace(artifact.Kind) ||
                !Path.IsPathFullyQualified(artifact.Path) ||
                !PathsEqual(artifact.Path, fullPath) ||
                !string.Equals(artifact.RepositoryRelativePath, expectedRelativePath, StringComparison.Ordinal) ||
                artifact.SizeBytes < 0L ||
                !IsSha256(artifact.Sha256) ||
                !uniquePaths.Add(fullPath) ||
                !IsArtifactFile(fullPath, artifactRoot) ||
                requiredRoot is not null &&
                !HasSafeArtifactPath(fullPath, requiredRoot, allowMissingLeaf: false))
            {
                return false;
            }
            var file = new FileInfo(fullPath);
            if (file.Length != artifact.SizeBytes ||
                !string.Equals(ComputeFileSha256(fullPath), artifact.Sha256, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    internal static string ComputeRowAggregate(IReadOnlyList<BenchmarkHistoryArtifactRow> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, BenchmarkEvidenceSchemas.RowAggregateFormat);
        foreach (var row in rows
                     .OrderBy(static row => TargetKey(row.Method, row.ProviderName), StringComparer.Ordinal))
        {
            AppendHashField(hash, row.Method);
            AppendHashField(hash, row.ProviderName);
            AppendHashField(hash, row.Category);
            AppendHashField(hash, FormatNumber(row.MeanMicroseconds));
            AppendHashField(hash, FormatNumber(row.ErrorMicroseconds));
            AppendHashField(hash, FormatNumber(row.MedianMicroseconds));
            AppendHashField(hash, FormatNumber(row.StdDevMicroseconds));
            AppendHashField(hash, FormatNumber(row.MinMicroseconds));
            AppendHashField(hash, FormatNumber(row.MaxMicroseconds));
            AppendHashField(hash, FormatNumber(row.AllocatedBytes));
            AppendHashField(hash, FormatNumber(row.NoisePercent));
            AppendHashField(hash, FormatNumber(row.UncertaintyPercent));
            AppendHashField(hash, FormatNumber(row.StdDevPercent));
            AppendHashField(hash, row.OperationsPerInvoke?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            AppendHashField(hash, row.TrackingGroup ?? string.Empty);
            AppendHashField(hash, row.Job ?? string.Empty);
            AppendHashField(hash, row.Runtime ?? string.Empty);
            AppendHashField(hash, row.Jit ?? string.Empty);
            AppendHashField(hash, row.Platform ?? string.Empty);
            AppendHashField(hash, row.Toolchain ?? string.Empty);
            AppendHashField(hash, row.TelemetryDelta is null
                ? string.Empty
                : JsonSerializer.Serialize(row.TelemetryDelta));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string FormatNumber(double? value) =>
        value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

    private static void AppendHashField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string GetArtifactRoot(string repositoryRoot) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts")));

    private static bool IsArtifactFile(string path, string artifactRoot)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!HasSafeArtifactPath(fullPath, artifactRoot, allowMissingLeaf: false))
                return false;
            var attributes = File.GetAttributes(fullPath);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRepositoryFile(string path, string repositoryRoot)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!HasSafeArtifactPath(fullPath, repositoryRoot, allowMissingLeaf: false))
                return false;
            var attributes = File.GetAttributes(fullPath);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsArtifactOutputPath(string path, string artifactRoot)
    {
        try
        {
            return HasSafeArtifactPath(Path.GetFullPath(path), artifactRoot, allowMissingLeaf: true);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSafeDirectory(string path, string artifactRoot)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!HasSafeArtifactPath(Path.Combine(fullPath, ".probe"), artifactRoot, allowMissingLeaf: true))
                return false;
            var attributes = File.GetAttributes(fullPath);
            return (attributes & FileAttributes.Directory) != 0 &&
                   (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasSafeArtifactPath(string fullPath, string artifactRoot, bool allowMissingLeaf)
    {
        var relativePath = Path.GetRelativePath(artifactRoot, fullPath);
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == "." ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        if (!Directory.Exists(artifactRoot))
            return allowMissingLeaf;
        var rootAttributes = File.GetAttributes(artifactRoot);
        if ((rootAttributes & FileAttributes.Directory) == 0 ||
            (rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var current = artifactRoot;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            current = Path.Combine(current, segments[index]);
            if (!Directory.Exists(current))
                return allowMissingLeaf;
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
        }

        if (Directory.Exists(fullPath))
            return false;
        if (!File.Exists(fullPath))
            return allowMissingLeaf;
        var leafAttributes = File.GetAttributes(fullPath);
        return (leafAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    }

    private static string TargetKey(string method, string provider) => $"{method}\u001f{provider}";
}
