using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataLinq.DevTools;

public sealed record TestShardEvidenceContract(
    string Suite,
    string? TargetId,
    string? ProviderAffinityRole,
    int ExpectedCases);

public sealed record TestShardEvidenceEntry(
    string Suite,
    string? TargetId,
    string? ProviderAffinityRole,
    int Cases,
    string RunId,
    string SourceFile);

public sealed record TestShardEvidenceAggregate(
    string SchemaVersion,
    string TestSummarySchemaVersion,
    string CommitSha,
    string Configuration,
    string OperatingSystem,
    string ProcessArchitecture,
    string FrameworkDescription,
    bool Complete,
    int TotalCases,
    IReadOnlyList<TestShardEvidenceEntry> Shards);

public static class TestShardEvidenceAggregator
{
    public const string SchemaVersion = "v0.9.testing-shard-aggregate.v1";
    public const int CompleteUnitSuiteExpectedCases = 1686;

    public static IReadOnlyList<TestShardEvidenceContract> FullMatrixContract { get; } =
    [
        new("generators", null, null, 61),
        new("unit", null, null, CompleteUnitSuiteExpectedCases),
        new("memory", null, null, 141),
        new("compliance", "sqlite-file", "AnchorWithInvariant", 496),
        new("compliance", "sqlite-memory", "TargetSpecific", 367),
        new("compliance", "mysql-8.4", "TargetSpecific", 373),
        new("compliance", "mysql-9.7", "TargetSpecific", 373),
        new("compliance", "mariadb-10.11", "TargetSpecific", 373),
        new("compliance", "mariadb-11.4", "TargetSpecific", 373),
        new("compliance", "mariadb-11.8", "TargetSpecific", 373),
        new("compliance", "mariadb-12.3", "TargetSpecific", 373),
        new("mysql", "mysql-8.4", "TargetSpecific", 62),
        new("mysql", "mysql-9.7", "AnchorWithInvariant", 127),
        new("mysql", "mariadb-10.11", "TargetSpecific", 64),
        new("mysql", "mariadb-11.4", "TargetSpecific", 64),
        new("mysql", "mariadb-11.8", "TargetSpecific", 64),
        new("mysql", "mariadb-12.3", "TargetSpecific", 64)
    ];

    public static TestShardEvidenceAggregate AggregateDirectory(
        string inputRoot,
        string expectedCommitSha,
        string expectedConfiguration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCommitSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedConfiguration);

        var root = Path.GetFullPath(inputRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Shard evidence directory does not exist: '{root}'.");

        var summaryPaths = Directory.EnumerateFiles(root, "*-summary.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (summaryPaths.Length == 0)
            throw new InvalidDataException($"No '*-summary.json' shard reports were found beneath '{root}'.");

        var reports = summaryPaths
            .Select(path => new LoadedShard(path, ReadSummary(path), FindArtifactRoot(root, path)))
            .ToArray();
        return Aggregate(reports, expectedCommitSha, expectedConfiguration);
    }

    public static void Write(TestShardEvidenceAggregate aggregate, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!aggregate.Complete)
            throw new InvalidDataException("Incomplete shard evidence cannot be written as an aggregate.");

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var options = JsonOptions(writeIndented: true);
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(aggregate, options) + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static TestShardEvidenceAggregate Aggregate(
        IReadOnlyList<LoadedShard> loaded,
        string expectedCommitSha,
        string expectedConfiguration)
    {
        var duplicateRunIds = loaded.GroupBy(static item => item.Report.RunId, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        if (duplicateRunIds.Length > 0)
            throw new InvalidDataException($"Duplicate shard run ids: {string.Join(", ", duplicateRunIds)}.");

        TestRunSummaryRuntimeEnvironment? commonRuntime = null;
        var entries = new List<TestShardEvidenceEntry>();
        foreach (var item in loaded)
        {
            var report = item.Report;
            ValidateReportEnvelope(report, item.Path, expectedCommitSha, expectedConfiguration);
            ValidateDownloadedArtifacts(report, item.ArtifactRoot);

            commonRuntime ??= report.RuntimeEnvironment;
            if (!SameRuntime(commonRuntime, report.RuntimeEnvironment))
            {
                throw new InvalidDataException(
                    $"Shard '{item.Path}' has incompatible runtime identity '{FormatRuntime(report.RuntimeEnvironment)}'; " +
                    $"expected '{FormatRuntime(commonRuntime)}'.");
            }

            var result = report.Results.Single();
            var targetId = result.TargetIds.SingleOrDefault();
            entries.Add(new TestShardEvidenceEntry(
                result.Suite,
                targetId,
                NormalizeOptionalRole(result.ProviderAffinityRole),
                result.Total!.Value,
                report.RunId,
                item.Path));
        }

        var expectedByKey = FullMatrixContract.ToDictionary(ContractKey, StringComparer.OrdinalIgnoreCase);
        var actualGroups = entries.GroupBy(EntryKey, StringComparer.OrdinalIgnoreCase).ToArray();
        var duplicates = actualGroups.Where(static group => group.Count() > 1).Select(static group => group.Key).ToArray();
        if (duplicates.Length > 0)
            throw new InvalidDataException($"Duplicate full-matrix shards: {string.Join(", ", duplicates)}.");

        var actualByKey = actualGroups.ToDictionary(static group => group.Key, static group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var missing = expectedByKey.Keys.Except(actualByKey.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var unexpected = actualByKey.Keys.Except(expectedByKey.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || unexpected.Length > 0)
        {
            throw new InvalidDataException(
                $"Full-matrix shard coverage mismatch. Missing: [{string.Join(", ", missing)}]; " +
                $"unexpected: [{string.Join(", ", unexpected)}].");
        }

        foreach (var (key, contract) in expectedByKey)
        {
            var entry = actualByKey[key];
            if (entry.Cases != contract.ExpectedCases ||
                !string.Equals(entry.ProviderAffinityRole, contract.ProviderAffinityRole, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Shard '{key}' contract mismatch: expected {contract.ExpectedCases} cases and role " +
                    $"'{contract.ProviderAffinityRole ?? "-"}', got {entry.Cases} and '{entry.ProviderAffinityRole ?? "-"}'.");
            }
        }

        var orderedEntries = FullMatrixContract.Select(contract => actualByKey[ContractKey(contract)]).ToArray();
        return new TestShardEvidenceAggregate(
            SchemaVersion,
            TestRunSummaryReporter.SchemaVersion,
            expectedCommitSha,
            expectedConfiguration,
            commonRuntime!.OperatingSystem,
            commonRuntime.ProcessArchitecture,
            commonRuntime.FrameworkDescription,
            Complete: true,
            TotalCases: orderedEntries.Sum(static entry => entry.Cases),
            Shards: orderedEntries);
    }

    private static void ValidateReportEnvelope(
        TestRunSummaryReport report,
        string path,
        string expectedCommitSha,
        string expectedConfiguration)
    {
        if (!string.Equals(report.SchemaVersion, TestRunSummaryReporter.SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Shard '{path}' has incompatible schema '{report.SchemaVersion}'.");
        if (report.Outcome != TestRunSummaryOutcome.Passed || report.OverallExitCode != 0 ||
            !report.CountsComplete || !report.IsCompleteForInvocation || !report.ArtifactsComplete)
        {
            throw new InvalidDataException($"Shard '{path}' is not a complete passing invocation.");
        }
        if (!report.RunnerEvidence.Start.Captured ||
            !report.RunnerEvidence.End.Captured ||
            report.RunnerEvidence.StateChangedDuringRun ||
            !report.RunnerEvidence.AssembliesMatchCheckout ||
            !report.RunnerEvidence.AssembliesBuiltFromCleanState ||
            report.RunnerEvidence.Start.Dirty ||
            report.RunnerEvidence.End.Dirty)
        {
            throw new InvalidDataException($"Shard '{path}' was not produced from a stable clean checkout.");
        }

        string[] commits =
        [
            report.RunnerEvidence.Start.Commit,
            report.RunnerEvidence.End.Commit,
            report.RunnerEvidence.EntryAssembly.RepositoryCommit,
            report.RunnerEvidence.DevToolsAssembly.RepositoryCommit
        ];
        if (commits.Any(commit => !string.Equals(commit, expectedCommitSha, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Shard '{path}' does not match commit '{expectedCommitSha}'.");
        if (!string.Equals(report.Invocation.Configuration, expectedConfiguration, StringComparison.Ordinal))
            throw new InvalidDataException($"Shard '{path}' does not use configuration '{expectedConfiguration}'.");
        if (report.Invocation.Profile != ToolingProfile.Ci || !report.Invocation.BuildProject)
            throw new InvalidDataException($"Shard '{path}' was not built once under the CI profile.");
        if (report.ExpectedResults.Count != 1 || report.Results.Count != 1 || report.Invocation.ResolvedSuites.Count != 1)
            throw new InvalidDataException($"Shard '{path}' must contain exactly one suite/result row.");

        var expected = report.ExpectedResults[0];
        var result = report.Results[0];
        if (!string.Equals(expected.Suite, result.Suite, StringComparison.OrdinalIgnoreCase) ||
            !expected.TargetIds.SequenceEqual(result.TargetIds, StringComparer.OrdinalIgnoreCase) ||
            !string.Equals(expected.ProviderAffinityRole, result.ProviderAffinityRole, StringComparison.Ordinal) ||
            !string.Equals(report.Invocation.ProviderAffinityRole, result.ProviderAffinityRole, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Shard '{path}' expected and actual row identities differ.");
        }
        if (result.TargetIds.Count > 1 || (result.TargetIds.Count == 1 && result.BatchIndex != 1) ||
            (result.TargetIds.Count == 0 && result.BatchIndex is not null))
        {
            throw new InvalidDataException($"Shard '{path}' obscures per-target identity.");
        }
        if (result.Total is null || result.Passed != result.Total || result.Failed != 0 || result.Skipped != 0 ||
            !result.Performance.Captured || result.Performance.TestCount != result.Total)
        {
            throw new InvalidDataException($"Shard '{path}' has incomplete or inconsistent case counts.");
        }
    }

    private static void ValidateDownloadedArtifacts(TestRunSummaryReport report, string artifactRoot)
    {
        var downloadedFiles = Directory.EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var result in report.Results)
        {
            foreach (var originalPath in new[] { result.LogPath, result.HtmlReportPath, result.TrxReportPath })
            {
                if (!downloadedFiles.Contains(Path.GetFileName(originalPath)))
                    throw new InvalidDataException($"Downloaded shard '{report.RunId}' is missing '{Path.GetFileName(originalPath)}'.");
            }
        }
    }

    private static TestRunSummaryReport ReadSummary(string path)
    {
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        ValidateNoDuplicateProperties(document.RootElement, "$", path);
        return JsonSerializer.Deserialize<TestRunSummaryReport>(json, JsonOptions(writeIndented: false))
            ?? throw new InvalidDataException($"Shard summary '{path}' deserialized to null.");
    }

    private static void ValidateNoDuplicateProperties(JsonElement element, string jsonPath, string sourcePath)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Duplicate JSON property '{property.Name}' at '{jsonPath}' in '{sourcePath}'.");
                ValidateNoDuplicateProperties(property.Value, $"{jsonPath}.{property.Name}", sourcePath);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                ValidateNoDuplicateProperties(item, $"{jsonPath}[{index++}]", sourcePath);
        }
    }

    private static string FindArtifactRoot(string inputRoot, string summaryPath)
    {
        var relative = Path.GetRelativePath(inputRoot, summaryPath);
        var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return Path.Combine(inputRoot, firstSegment);
    }

    private static JsonSerializerOptions JsonOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions { WriteIndented = writeIndented };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static bool SameRuntime(TestRunSummaryRuntimeEnvironment left, TestRunSummaryRuntimeEnvironment right) =>
        string.Equals(left.OperatingSystem, right.OperatingSystem, StringComparison.Ordinal) &&
        string.Equals(left.ProcessArchitecture, right.ProcessArchitecture, StringComparison.Ordinal) &&
        string.Equals(left.FrameworkDescription, right.FrameworkDescription, StringComparison.Ordinal);

    private static string FormatRuntime(TestRunSummaryRuntimeEnvironment runtime) =>
        $"{runtime.OperatingSystem}/{runtime.ProcessArchitecture}/{runtime.FrameworkDescription}";

    private static string ContractKey(TestShardEvidenceContract contract) =>
        $"{contract.Suite}:{contract.TargetId ?? "-"}";

    private static string EntryKey(TestShardEvidenceEntry entry) =>
        $"{entry.Suite}:{entry.TargetId ?? "-"}";

    private static string? NormalizeOptionalRole(string? role) =>
        string.IsNullOrEmpty(role) ? null : role;

    private sealed record LoadedShard(string Path, TestRunSummaryReport Report, string ArtifactRoot);
}
