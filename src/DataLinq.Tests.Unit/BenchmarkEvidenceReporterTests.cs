using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using DataLinq.Benchmark.CLI;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class BenchmarkEvidenceReporterTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Test]
    public async Task CreateHistory_CanonicalHeavyMemoryRunProducesValidV3Evidence()
    {
        using var fixture = new BenchmarkFixture();
        var historyPath = fixture.OutputPath("canonical-history.json");
        var artifact = BenchmarkEvidenceReporter.CreateHistory(
            fixture.CreateInput("canonical", historyPath: historyPath));

        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, historyPath, artifact);
        var persisted = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, historyPath);
        using var json = JsonDocument.Parse(File.ReadAllText(historyPath));
        var root = json.RootElement;

        await Assert.That(artifact.SchemaVersion).IsEqualTo(BenchmarkEvidenceSchemas.HistoryVersion);
        await Assert.That(artifact.SchemaId).IsEqualTo(BenchmarkEvidenceSchemas.HistoryId);
        await Assert.That(artifact.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Passed);
        await Assert.That(artifact.IsCompleteForInvocation).IsTrue();
        await Assert.That(artifact.ArtifactsComplete).IsTrue();
        await Assert.That(artifact.ValidForEvidence).IsTrue();
        await Assert.That(artifact.ReviewRequired).IsFalse();
        await Assert.That(artifact.Summary!.ExactTargetSet).IsTrue();
        await Assert.That(artifact.Summary.ExpectedTargetCount).IsEqualTo(9);
        await Assert.That(artifact.Summary.ObservedTargetCount).IsEqualTo(9);
        await Assert.That(artifact.Summary.TelemetryRowCount).IsEqualTo(9);
        await Assert.That(artifact.RowAggregateSha256!.Length).IsEqualTo(64);
        await Assert.That(artifact.RunnerEvidence!.ValidForEvidence).IsTrue();
        await Assert.That(persisted.Reference.LegacySchema).IsFalse();
        await Assert.That(persisted.Reference.SourceValidForEvidence).IsTrue();
        await Assert.That(persisted.Reference.ProcessorIdentifier).IsEqualTo("fixture processor");
        await Assert.That(persisted.Reference.BenchmarkDotNetVersion).IsEqualTo("0.15.8");
        await Assert.That(persisted.Reference.RowAggregateSha256).IsEqualTo(artifact.RowAggregateSha256);
        await Assert.That(root.GetProperty("SchemaVersion").GetInt32()).IsEqualTo(3);
        await Assert.That(root.GetProperty("SchemaId").GetString()).IsEqualTo(BenchmarkEvidenceSchemas.HistoryId);
        await Assert.That(root.GetProperty("Invocation").GetProperty("SelectedCategory").GetString())
            .IsEqualTo(BenchmarkHarnessRunner.V09MemoryReadCategory);
        await Assert.That(root.GetProperty("ExpectedTargets").GetArrayLength()).IsEqualTo(9);
        await Assert.That(root.GetProperty("Artifacts").GetProperty("Files").GetArrayLength())
            .IsEqualTo(15);
    }

    [Test]
    public async Task ResolveExpectedTargets_MemoryLaneIsTheExactCanonicalNineRowScope()
    {
        using var fixture = new BenchmarkFixture();
        var invocation = fixture.CreateInvocation("scope", fixture.OutputPath("scope.json"));

        var targets = BenchmarkEvidenceReporter.ResolveExpectedTargets(invocation);

        await Assert.That(targets.Count).IsEqualTo(9);
        await Assert.That(targets.All(static target => target.ProviderName == "memory")).IsTrue();
        await Assert.That(targets.Select(static target => target.Id).ToArray())
            .IsEquivalentTo(fixture.CreateMemoryRows().Select(static row =>
                $"{row.Category}|{row.ProviderName}|{row.Method}").ToArray());
        await Assert.That(targets.Select(static target => target.Id).SequenceEqual(
            targets.Select(static target => target.Id).Order(StringComparer.Ordinal),
            StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ResolveExpectedTargets_AllocationRegressionIsTheExactSQLiteMemoryNineRowScope()
    {
        using var fixture = new BenchmarkFixture();
        var invocation = fixture.CreateInvocation("allocation", fixture.OutputPath("allocation.json")) with
        {
            SelectedCategory = BenchmarkHarnessRunner.AllocationRegressionCategory,
            ConfiguredProviderIds = ["sqlite-memory"]
        };

        var targets = BenchmarkEvidenceReporter.ResolveExpectedTargets(invocation);

        await Assert.That(targets.Count).IsEqualTo(9);
        await Assert.That(targets.All(static target => target.ProviderName == "sqlite-memory")).IsTrue();
        await Assert.That(targets.Select(static target => target.Method)).Contains("CRUD workflow batch");
        await Assert.That(targets.Select(static target => target.Method)).Contains("Warm relation traversal");
    }

    [Test]
    [Arguments("Provider initialization", 1)]
    [Arguments("Startup primary-key fetch", 1)]
    [Arguments("CRUD workflow small", 250)]
    [Arguments("CRUD workflow batch", 350)]
    [Arguments("Update employees", 2000)]
    [Arguments("Cold primary-key fetch", 1000)]
    [Arguments("Warm primary-key fetch", 60000)]
    [Arguments("Cold relation traversal", 1000)]
    [Arguments("Warm relation traversal", 1000000)]
    public async Task AllocationRegression_UsesExactCalibratedOperationCounts(
        string method,
        int expectedOperations)
    {
        await Assert.That(BenchmarkEvidenceReporter.GetExpectedOperationsPerInvoke(method))
            .IsEqualTo(expectedOperations);
    }

    [Test]
    public async Task EvaluateRunnerEvidence_SeparatesToolingCheckoutFromHistoricalRuntimeTarget()
    {
        const string targetCommit = "89abcdef0123456789abcdef0123456789abcdef";
        var tooling = new TestRunSummaryRepositoryState(true, Commit, "feature", false, "tooling-clean");
        var target = new TestRunSummaryRepositoryState(true, targetCommit, "HEAD", false, "target-clean");
        var benchmarkAssembly = new BenchmarkAssemblyEvidence(
            "benchmark.dll",
            new string('a', 64),
            BenchmarkFixture.RunnerAssembly("DataLinq.Benchmark", targetCommit));

        var evidence = BenchmarkEvidenceReporter.EvaluateRunnerEvidence(
            tooling,
            tooling,
            BenchmarkFixture.RunnerAssembly("DataLinq.Benchmark.CLI"),
            BenchmarkFixture.RunnerAssembly("DataLinq.DevTools"),
            benchmarkAssembly,
            target,
            target);
        var dirtyTarget = BenchmarkEvidenceReporter.EvaluateRunnerEvidence(
            tooling,
            tooling,
            BenchmarkFixture.RunnerAssembly("DataLinq.Benchmark.CLI"),
            BenchmarkFixture.RunnerAssembly("DataLinq.DevTools"),
            benchmarkAssembly,
            target,
            target with { Dirty = true, StatusSha256 = "target-dirty" });

        await Assert.That(evidence.ValidForEvidence).IsTrue();
        await Assert.That(evidence.BenchmarkAssemblyMatchesTarget).IsTrue();
        await Assert.That(evidence.BenchmarkTargetStateChangedDuringRun).IsFalse();
        await Assert.That(dirtyTarget.ValidForEvidence).IsFalse();
        await Assert.That(dirtyTarget.BenchmarkTargetStateChangedDuringRun).IsTrue();
    }

    [Test]
    public async Task CreateHistory_HistoricalRuntimeTargetProducesRevalidatableV3Evidence()
    {
        using var fixture = new BenchmarkFixture();
        var historyPath = fixture.OutputPath("historical-target.json");
        var artifact = BenchmarkEvidenceReporter.CreateHistory(
            fixture.CreateHistoricalTargetInput("historical-target", historyPath));

        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, historyPath, artifact);
        var persisted = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, historyPath);

        await Assert.That(artifact.ValidForEvidence).IsTrue();
        await Assert.That(artifact.Metadata.Commit).IsEqualTo(BenchmarkFixture.HistoricalCommit);
        await Assert.That(artifact.RunnerEvidence!.BenchmarkAssemblyMatchesTarget).IsTrue();
        await Assert.That(artifact.RunnerEvidence.BenchmarkTargetStart!.Commit)
            .IsEqualTo(BenchmarkFixture.HistoricalCommit);
        await Assert.That(persisted.Reference.SourceValidForEvidence).IsTrue();
    }

    [Test]
    public async Task CreateHistory_MissingExpectedRowFailsExactScope()
    {
        using var fixture = new BenchmarkFixture();
        var rows = fixture.CreateMemoryRows().SkipLast(1).ToArray();

        var artifact = BenchmarkEvidenceReporter.CreateHistory(fixture.CreateInput("missing", rows));

        await Assert.That(artifact.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Incomplete);
        await Assert.That(artifact.IsCompleteForInvocation).IsFalse();
        await Assert.That(artifact.ValidForEvidence).IsFalse();
        await Assert.That(artifact.Summary!.ExpectedTargetCount).IsEqualTo(9);
        await Assert.That(artifact.Summary.ObservedTargetCount).IsEqualTo(8);
        await Assert.That(artifact.Summary.ExactTargetSet).IsFalse();
        await Assert.That(artifact.OverallExitCode).IsEqualTo(1);
    }

    [Test]
    public async Task CreateHistory_ExtraObservedRowFailsExactScope()
    {
        using var fixture = new BenchmarkFixture();
        var rows = fixture.CreateMemoryRows()
            .Append(fixture.CreateRow("Unexpected memory row", category: "memory-query"))
            .ToArray();

        var artifact = BenchmarkEvidenceReporter.CreateHistory(fixture.CreateInput("extra", rows));

        await Assert.That(artifact.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Incomplete);
        await Assert.That(artifact.IsCompleteForInvocation).IsFalse();
        await Assert.That(artifact.ValidForEvidence).IsFalse();
        await Assert.That(artifact.Summary!.RowsComplete).IsTrue();
        await Assert.That(artifact.Summary.ExactTargetSet).IsFalse();
        await Assert.That(artifact.Summary.ObservedTargetCount).IsEqualTo(10);
    }

    [Test]
    public async Task CreateHistory_DuplicateObservedRowFailsCompleteness()
    {
        using var fixture = new BenchmarkFixture();
        var original = fixture.CreateMemoryRows();
        var rows = original.Append(original[0]).ToArray();

        var artifact = BenchmarkEvidenceReporter.CreateHistory(fixture.CreateInput("duplicate", rows));

        await Assert.That(artifact.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Incomplete);
        await Assert.That(artifact.IsCompleteForInvocation).IsFalse();
        await Assert.That(artifact.ValidForEvidence).IsFalse();
        await Assert.That(artifact.Summary!.RowsComplete).IsFalse();
        await Assert.That(artifact.Summary.ExactTargetSet).IsFalse();
    }

    [Test]
    public async Task CreateHistory_SmokeRunCanBeCompleteButIsNotReleaseEvidence()
    {
        using var fixture = new BenchmarkFixture();
        var rows = fixture.CreateMemoryRows(expectedJob: "Dry");
        var input = fixture.CreateInput(
            "smoke",
            rows,
            profile: "smoke",
            expectedJob: "Dry",
            releaseEvidenceIntent: false,
            noBuild: true);

        var artifact = BenchmarkEvidenceReporter.CreateHistory(input);

        await Assert.That(artifact.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Passed);
        await Assert.That(artifact.IsCompleteForInvocation).IsTrue();
        await Assert.That(artifact.ArtifactsComplete).IsTrue();
        await Assert.That(artifact.Summary!.ExactTargetSet).IsTrue();
        await Assert.That(artifact.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task CreateHistory_CanonicalHeavyEvidenceRequiresBuildCommands()
    {
        using var fixture = new BenchmarkFixture();
        var built = BenchmarkEvidenceReporter.CreateHistory(fixture.CreateInput("heavy-built"));
        var noBuild = BenchmarkEvidenceReporter.CreateHistory(
            fixture.CreateInput("heavy-no-build", noBuild: true));

        await Assert.That(built.Invocation!.NoBuild).IsFalse();
        await Assert.That(built.ValidForEvidence).IsTrue();
        await Assert.That(noBuild.Invocation!.NoBuild).IsTrue();
        await Assert.That(noBuild.IsCompleteForInvocation).IsTrue();
        await Assert.That(noBuild.ArtifactsComplete).IsTrue();
        await Assert.That(noBuild.ValidForEvidence).IsFalse();
        await Assert.That(noBuild.OverallExitCode).IsEqualTo(1);
    }

    [Test]
    public async Task CreateHistory_WrongOperationsOrTrackingGroupMakesCanonicalRowsIncomplete()
    {
        using var fixture = new BenchmarkFixture();
        var operationsRows = fixture.CreateMemoryRows().ToArray();
        operationsRows[0] = operationsRows[0] with
        {
            OperationsPerInvoke = 2,
            TelemetryDelta = operationsRows[0].TelemetryDelta! with { OperationsPerInvoke = 2 }
        };
        var trackingRows = fixture.CreateMemoryRows().ToArray();
        trackingRows[0] = trackingRows[0] with { TrackingGroup = "unexpected-lane" };

        var wrongOperations = BenchmarkEvidenceReporter.CreateHistory(
            fixture.CreateInput("wrong-operations", operationsRows));
        var wrongTracking = BenchmarkEvidenceReporter.CreateHistory(
            fixture.CreateInput("wrong-tracking", trackingRows));

        await Assert.That(wrongOperations.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Incomplete);
        await Assert.That(wrongOperations.IsCompleteForInvocation).IsFalse();
        await Assert.That(wrongOperations.Summary!.InvalidRowCount).IsEqualTo(1);
        await Assert.That(wrongOperations.Summary.ExactTargetSet).IsTrue();
        await Assert.That(wrongTracking.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Incomplete);
        await Assert.That(wrongTracking.IsCompleteForInvocation).IsFalse();
        await Assert.That(wrongTracking.Summary!.InvalidRowCount).IsEqualTo(1);
        await Assert.That(wrongTracking.Summary.ExactTargetSet).IsTrue();
    }

    [Test]
    public async Task CreateHistory_MissingExpectedMemorySignalRequiresReviewButRemainsAuthentic()
    {
        using var fixture = new BenchmarkFixture();
        var historyPath = fixture.OutputPath("telemetry-shape.json");
        var rows = fixture.CreateMemoryRows().ToArray();
        var hitIndex = Array.FindIndex(rows, static row => row.Method == "Memory primary-key hit");
        rows[hitIndex] = rows[hitIndex] with
        {
            TelemetryDelta = rows[hitIndex].TelemetryDelta! with { MemoryCacheHitsPerOperation = 0d }
        };

        var artifact = BenchmarkEvidenceReporter.CreateHistory(
            fixture.CreateInput("telemetry-shape", rows, historyPath: historyPath));
        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, historyPath, artifact);
        var persisted = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, historyPath);

        await Assert.That(artifact.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.ReviewRequired);
        await Assert.That(artifact.IsCompleteForInvocation).IsTrue();
        await Assert.That(artifact.ValidForEvidence).IsTrue();
        await Assert.That(artifact.ReviewRequired).IsTrue();
        await Assert.That(artifact.OverallExitCode).IsEqualTo(0);
        await Assert.That(artifact.Warnings.Count).IsEqualTo(1);
        await Assert.That(artifact.Warnings[0].Kind).IsEqualTo("TelemetryShape");
        await Assert.That(persisted.Reference.SourceValidForEvidence).IsTrue();
    }

    [Test]
    public async Task CreateComparison_CanonicalV3InputsAreComparableAndValid()
    {
        using var fixture = new BenchmarkFixture();
        var baseline = fixture.CreateAndReadHistory("baseline");
        var candidate = fixture.CreateAndReadHistory("candidate");
        var comparisonPath = fixture.OutputPath("comparison.json");

        var comparison = BenchmarkEvidenceReporter.CreateComparison(
            fixture.RepositoryRoot,
            baseline,
            candidate,
            comparisonPath,
            warningThresholdPercent: 10d,
            releaseEvidenceIntent: true);
        BenchmarkEvidenceReporter.WriteComparison(fixture.RepositoryRoot, comparisonPath, comparison);
        using var json = JsonDocument.Parse(File.ReadAllText(comparisonPath));
        var root = json.RootElement;

        await Assert.That(comparison.SchemaVersion).IsEqualTo(BenchmarkEvidenceSchemas.ComparisonVersion);
        await Assert.That(comparison.SchemaId).IsEqualTo(BenchmarkEvidenceSchemas.ComparisonId);
        await Assert.That(comparison.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Passed);
        await Assert.That(comparison.IsComplete).IsTrue();
        await Assert.That(comparison.ArtifactsComplete).IsTrue();
        await Assert.That(comparison.Comparable).IsTrue();
        await Assert.That(comparison.ReviewRequired).IsFalse();
        await Assert.That(comparison.ValidForEvidence).IsTrue();
        await Assert.That(comparison.OverallExitCode).IsEqualTo(0);
        await Assert.That(comparison.StatusCounts!.Stable).IsEqualTo(9);
        await Assert.That(comparison.StatusCounts.Warning).IsEqualTo(0);
        await Assert.That(comparison.BaselineArtifact!.SelectedCategory)
            .IsEqualTo(BenchmarkHarnessRunner.V09MemoryReadCategory);
        await Assert.That(comparison.BaselineArtifact.ExpectedJob).IsEqualTo("MediumRun");
        await Assert.That(comparison.BaselineArtifact.ConfiguredProviderIds).IsEquivalentTo(["memory"]);
        await Assert.That(comparison.BaselineArtifact.ExpectedTargetIds.Count).IsEqualTo(9);
        await Assert.That(comparison.BaselineArtifact.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Passed);
        await Assert.That(comparison.BaselineArtifact.IsCompleteForInvocation).IsTrue();
        await Assert.That(comparison.BaselineArtifact.ArtifactsComplete).IsTrue();
        await Assert.That(comparison.Rows.All(static row =>
            row.BaselineJob == "MediumRun" &&
            row.CandidateJob == "MediumRun" &&
            row.BaselineRuntime == row.CandidateRuntime &&
            row.BaselineToolchain == row.CandidateToolchain)).IsTrue();
        await Assert.That(root.GetProperty("SchemaId").GetString())
            .IsEqualTo(BenchmarkEvidenceSchemas.ComparisonId);
        await Assert.That(root.GetProperty("BaselineArtifact").GetProperty("ExpectedTargetIds").GetArrayLength())
            .IsEqualTo(9);
        await Assert.That(root.GetProperty("Rows")[0].TryGetProperty("BaselineTelemetry", out _)).IsTrue();
    }

    [Test]
    public async Task ReadHistory_LegacyV2RemainsDiagnosticOnly()
    {
        using var fixture = new BenchmarkFixture();
        var historyPath = fixture.OutputPath("legacy-v2.json");
        var legacy = new BenchmarkHistoryArtifact
        {
            SchemaVersion = 2,
            RunId = "legacy-v2",
            GeneratedAtUtc = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc),
            Metadata = fixture.CreateMetadata("heavy"),
            Rows = fixture.CreateMemoryRows()
        };
        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, historyPath, legacy);

        var result = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, historyPath);

        await Assert.That(result.Reference.SchemaVersion).IsEqualTo(2);
        await Assert.That(result.Reference.LegacySchema).IsTrue();
        await Assert.That(result.Reference.SourceValidForEvidence).IsFalse();
        await Assert.That(result.Artifact.Rows.Count).IsEqualTo(9);
    }

    [Test]
    public async Task ReadHistory_GenuineV1RowNormalizesMissingScopeFieldsButRemainsDiagnosticOnly()
    {
        using var fixture = new BenchmarkFixture();
        var historyPath = fixture.OutputPath("legacy-v1.json");
        var row = fixture.CreateMemoryRows().Single(
            static candidate => candidate.Method == "Memory primary-key hit");
        var legacy = new
        {
            SchemaVersion = 1,
            RunId = "legacy-v1",
            GeneratedAtUtc = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc),
            Metadata = fixture.CreateMetadata("heavy"),
            Rows = new[]
            {
                new
                {
                    row.Method,
                    row.ProviderName,
                    row.MeanMicroseconds,
                    row.ErrorMicroseconds,
                    row.MedianMicroseconds,
                    row.StdDevMicroseconds,
                    row.MinMicroseconds,
                    row.MaxMicroseconds,
                    row.AllocatedBytes,
                    row.NoisePercent,
                    row.UncertaintyPercent,
                    row.StdDevPercent,
                    row.TelemetryDelta,
                    row.Job,
                    row.Runtime,
                    row.Jit,
                    row.Platform,
                    row.Toolchain
                }
            }
        };
        File.WriteAllText(historyPath, JsonSerializer.Serialize(legacy));

        var result = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, historyPath);
        var normalized = result.Artifact.Rows.Single();

        await Assert.That(result.Reference.SchemaVersion).IsEqualTo(1);
        await Assert.That(result.Reference.LegacySchema).IsTrue();
        await Assert.That(result.Reference.SourceValidForEvidence).IsFalse();
        await Assert.That(normalized.Category)
            .IsEqualTo(BenchmarkHarnessRunner.GetScenarioCategory(row.Method));
        await Assert.That(normalized.OperationsPerInvoke)
            .IsEqualTo(row.TelemetryDelta!.OperationsPerInvoke);
        await Assert.That(normalized.TrackingGroup)
            .IsEqualTo(BenchmarkHarnessRunner.GetTrackingGroup(row.Method));
    }

    [Test]
    public async Task CreateComparison_AllocationRegressionSurvivesNoisyLatencyClassification()
    {
        using var fixture = new BenchmarkFixture();
        var baselineRows = fixture.CreateMemoryRows().ToArray();
        var candidateRows = fixture.CreateMemoryRows().ToArray();
        baselineRows[0] = baselineRows[0] with
        {
            MeanMicroseconds = 100d,
            AllocatedBytes = 100d,
            NoisePercent = 25d
        };
        candidateRows[0] = candidateRows[0] with
        {
            MeanMicroseconds = 130d,
            AllocatedBytes = 120d,
            NoisePercent = 25d
        };
        var baseline = fixture.CreateAndReadHistory("noisy-baseline", baselineRows);
        var candidate = fixture.CreateAndReadHistory("noisy-candidate", candidateRows);

        var comparison = BenchmarkEvidenceReporter.CreateComparison(
            fixture.RepositoryRoot,
            baseline,
            candidate,
            fixture.OutputPath("noisy-comparison.json"),
            warningThresholdPercent: 10d,
            releaseEvidenceIntent: true);
        var changed = comparison.Rows.Single(row => row.Method == baselineRows[0].Method);

        await Assert.That(changed.LatencyStatus).IsEqualTo("noisy");
        await Assert.That(changed.AllocationStatus).IsEqualTo("warning");
        await Assert.That(changed.Status).IsEqualTo("warning");
        await Assert.That(comparison.StatusCounts!.LatencyWarnings).IsEqualTo(0);
        await Assert.That(comparison.StatusCounts.AllocationWarnings).IsEqualTo(1);
        await Assert.That(comparison.WarningCount).IsEqualTo(1);
        await Assert.That(comparison.ReviewRequired).IsTrue();
        await Assert.That(comparison.ValidForEvidence).IsTrue();
    }

    [Test]
    public async Task CreateComparison_ZeroToPositiveAllocationIsAWarningWithoutSyntheticDelta()
    {
        using var fixture = new BenchmarkFixture();
        var baselineRows = fixture.CreateMemoryRows().ToArray();
        var candidateRows = fixture.CreateMemoryRows().ToArray();
        baselineRows[0] = baselineRows[0] with { AllocatedBytes = 0d };
        candidateRows[0] = candidateRows[0] with { AllocatedBytes = 1d };
        var baseline = fixture.CreateAndReadHistory("zero-allocation-baseline", baselineRows);
        var candidate = fixture.CreateAndReadHistory("zero-allocation-candidate", candidateRows);

        var comparison = BenchmarkEvidenceReporter.CreateComparison(
            fixture.RepositoryRoot,
            baseline,
            candidate,
            fixture.OutputPath("zero-allocation-comparison.json"),
            warningThresholdPercent: 10d,
            releaseEvidenceIntent: true);
        var changed = comparison.Rows.Single(row => row.Method == baselineRows[0].Method);

        await Assert.That(changed.AllocatedDeltaPercent).IsNull();
        await Assert.That(changed.AllocationStatus).IsEqualTo("warning");
        await Assert.That(changed.Status).IsEqualTo("warning");
        await Assert.That(comparison.StatusCounts!.AllocationWarnings).IsEqualTo(1);
        await Assert.That(comparison.ReviewRequired).IsTrue();
    }

    [Test]
    public async Task CreateComparison_V3EnvironmentMismatchMakesComparisonIncomplete()
    {
        using var fixture = new BenchmarkFixture();
        var baseline = fixture.CreateAndReadHistory("environment-baseline");
        var candidatePath = fixture.OutputPath("environment-candidate.json");
        var candidateInput = fixture.CreateInput(
            "environment-candidate",
            historyPath: candidatePath);
        var candidateArtifact = BenchmarkEvidenceReporter.CreateHistory(candidateInput with
        {
            Metadata = candidateInput.Metadata with { RunnerOs = "different-os" }
        });
        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, candidatePath, candidateArtifact);
        var candidate = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, candidatePath);

        var comparison = BenchmarkEvidenceReporter.CreateComparison(
            fixture.RepositoryRoot,
            baseline,
            candidate,
            fixture.OutputPath("environment-comparison.json"),
            warningThresholdPercent: 10d,
            releaseEvidenceIntent: true);

        await Assert.That(baseline.Reference.SourceValidForEvidence).IsTrue();
        await Assert.That(candidate.Reference.SourceValidForEvidence).IsTrue();
        await Assert.That(comparison.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Incomplete);
        await Assert.That(comparison.IsComplete).IsFalse();
        await Assert.That(comparison.Comparable).IsFalse();
        await Assert.That(comparison.ValidForEvidence).IsFalse();
        await Assert.That(comparison.OverallExitCode).IsEqualTo(1);
        await Assert.That(comparison.StatusCounts!.ScopeMismatch).IsEqualTo(9);
    }

    [Test]
    public async Task CreateHistory_MissingRunnerEnvironmentCannotBecomeComparableEvidence()
    {
        using var fixture = new BenchmarkFixture();
        var baseline = fixture.CreateAndReadHistory("missing-environment-baseline");
        var candidatePath = fixture.OutputPath("missing-environment-candidate.json");
        var candidateInput = fixture.CreateInput(
            "missing-environment-candidate",
            historyPath: candidatePath);
        var candidateArtifact = BenchmarkEvidenceReporter.CreateHistory(candidateInput with
        {
            Metadata = candidateInput.Metadata with
            {
                RunnerOs = null,
                RunnerArchitecture = null
            }
        });
        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, candidatePath, candidateArtifact);
        var candidate = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, candidatePath);

        var comparison = BenchmarkEvidenceReporter.CreateComparison(
            fixture.RepositoryRoot,
            baseline,
            candidate,
            fixture.OutputPath("missing-environment-comparison.json"),
            warningThresholdPercent: 10d,
            releaseEvidenceIntent: true);

        await Assert.That(candidateArtifact.IsCompleteForInvocation).IsTrue();
        await Assert.That(candidateArtifact.ValidForEvidence).IsFalse();
        await Assert.That(candidateArtifact.OverallExitCode).IsEqualTo(1);
        await Assert.That(candidate.Reference.SourceValidForEvidence).IsFalse();
        await Assert.That(comparison.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Incomplete);
        await Assert.That(comparison.Comparable).IsFalse();
        await Assert.That(comparison.ValidForEvidence).IsFalse();
        await Assert.That(comparison.OverallExitCode).IsEqualTo(1);
        await Assert.That(comparison.StatusCounts!.ScopeMismatch).IsEqualTo(9);
    }

    [Test]
    public async Task CreateHistory_MissingProcessorOrBenchmarkVersionCannotBecomeEvidence()
    {
        using var fixture = new BenchmarkFixture();
        var processorPath = fixture.OutputPath("missing-processor.json");
        var processorInput = fixture.CreateInput("missing-processor", historyPath: processorPath);
        var processorArtifact = BenchmarkEvidenceReporter.CreateHistory(processorInput with
        {
            Metadata = processorInput.Metadata with { ProcessorIdentifier = null }
        });
        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, processorPath, processorArtifact);

        var versionPath = fixture.OutputPath("missing-benchmark-version.json");
        var versionInput = fixture.CreateInput("missing-benchmark-version", historyPath: versionPath);
        var versionArtifact = BenchmarkEvidenceReporter.CreateHistory(versionInput with
        {
            Metadata = versionInput.Metadata with { BenchmarkDotNetVersion = null }
        });
        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, versionPath, versionArtifact);

        var missingProcessor = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, processorPath);
        var missingVersion = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, versionPath);

        await Assert.That(processorArtifact.IsCompleteForInvocation).IsTrue();
        await Assert.That(processorArtifact.ValidForEvidence).IsFalse();
        await Assert.That(versionArtifact.IsCompleteForInvocation).IsTrue();
        await Assert.That(versionArtifact.ValidForEvidence).IsFalse();
        await Assert.That(missingProcessor.Reference.SourceValidForEvidence).IsFalse();
        await Assert.That(missingVersion.Reference.SourceValidForEvidence).IsFalse();
    }

    [Test]
    public async Task CreateComparison_ProcessorOrBenchmarkVersionMismatchIsIncomplete()
    {
        using var fixture = new BenchmarkFixture();
        var baseline = fixture.CreateAndReadHistory("identity-baseline");

        var processorPath = fixture.OutputPath("processor-mismatch.json");
        var processorInput = fixture.CreateInput("processor-mismatch", historyPath: processorPath);
        var processorArtifact = BenchmarkEvidenceReporter.CreateHistory(processorInput with
        {
            Metadata = processorInput.Metadata with { ProcessorIdentifier = "different processor" }
        });
        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, processorPath, processorArtifact);
        var processorCandidate = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, processorPath);

        var versionPath = fixture.OutputPath("benchmark-version-mismatch.json");
        var versionInput = fixture.CreateInput("benchmark-version-mismatch", historyPath: versionPath);
        var versionArtifact = BenchmarkEvidenceReporter.CreateHistory(versionInput with
        {
            Metadata = versionInput.Metadata with { BenchmarkDotNetVersion = "0.16.0" }
        });
        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, versionPath, versionArtifact);
        var versionCandidate = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, versionPath);

        var processorComparison = BenchmarkEvidenceReporter.CreateComparison(
            fixture.RepositoryRoot,
            baseline,
            processorCandidate,
            fixture.OutputPath("processor-mismatch-comparison.json"),
            warningThresholdPercent: 10d,
            releaseEvidenceIntent: true);
        var versionComparison = BenchmarkEvidenceReporter.CreateComparison(
            fixture.RepositoryRoot,
            baseline,
            versionCandidate,
            fixture.OutputPath("benchmark-version-mismatch-comparison.json"),
            warningThresholdPercent: 10d,
            releaseEvidenceIntent: true);

        await Assert.That(processorCandidate.Reference.SourceValidForEvidence).IsTrue();
        await Assert.That(versionCandidate.Reference.SourceValidForEvidence).IsTrue();
        await Assert.That(processorComparison.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Incomplete);
        await Assert.That(processorComparison.StatusCounts!.ScopeMismatch).IsEqualTo(9);
        await Assert.That(processorComparison.ValidForEvidence).IsFalse();
        await Assert.That(versionComparison.Outcome).IsEqualTo(BenchmarkEvidenceOutcomes.Incomplete);
        await Assert.That(versionComparison.StatusCounts!.ScopeMismatch).IsEqualTo(9);
        await Assert.That(versionComparison.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task ReadHistory_MalformedAndDuplicateSchemaJsonFailClosed()
    {
        using var fixture = new BenchmarkFixture();
        var malformedPath = fixture.OutputPath("malformed.json");
        var duplicatePath = fixture.OutputPath("duplicate-schema.json");
        File.WriteAllText(malformedPath, "{\"SchemaVersion\":3");
        File.WriteAllText(duplicatePath, "{\"SchemaVersion\":3,\"SchemaVersion\":2}");

        var malformed = Capture(() => BenchmarkEvidenceReporter.ReadHistory(
            fixture.RepositoryRoot,
            malformedPath));
        var duplicate = Capture(() => BenchmarkEvidenceReporter.ReadHistory(
            fixture.RepositoryRoot,
            duplicatePath));

        await Assert.That(malformed).IsNotNull();
        await Assert.That(duplicate).IsTypeOf<InvalidDataException>();
        await Assert.That(duplicate!.Message).Contains("Duplicate JSON property 'SchemaVersion'");
    }

    [Test]
    public async Task NormalizePaths_RejectsOutsideAndAliasedEvidenceFiles()
    {
        using var fixture = new BenchmarkFixture();
        var historyPath = fixture.OutputPath("alias.json");
        var aliasedPath = Path.Combine(Path.GetDirectoryName(historyPath)!, ".", Path.GetFileName(historyPath));
        var outsidePath = Path.Combine(
            Path.GetDirectoryName(fixture.RepositoryRoot)!,
            $"outside-{Guid.NewGuid():N}.json");
        File.WriteAllText(outsidePath, "sentinel");

        var aliasFailure = Capture(() => BenchmarkEvidenceReporter.NormalizePaths(
            fixture.RepositoryRoot,
            historyPath,
            aliasedPath,
            comparisonJsonPath: null,
            releaseEvidenceIntent: false));
        var outsideFailure = Capture(() => BenchmarkEvidenceReporter.NormalizePaths(
            fixture.RepositoryRoot,
            outsidePath,
            baselinePath: null,
            comparisonJsonPath: null,
            releaseEvidenceIntent: false));

        await Assert.That(aliasFailure).IsTypeOf<InvalidDataException>();
        await Assert.That(aliasFailure!.Message).Contains("must be distinct");
        await Assert.That(outsideFailure).IsTypeOf<InvalidDataException>();
        await Assert.That(File.ReadAllText(outsidePath)).IsEqualTo("sentinel");
        File.Delete(outsidePath);
    }

    [Test]
    public async Task InvalidateRequestedOutputs_RemovesStaleReportsButPreservesBaseline()
    {
        using var fixture = new BenchmarkFixture();
        var historyPath = fixture.OutputPath("stale-history.json");
        var baselinePath = fixture.OutputPath("retained-baseline.json");
        var comparisonPath = fixture.OutputPath("stale-comparison.json");
        File.WriteAllText(historyPath, "{\"Outcome\":\"Passed\"}");
        File.WriteAllText(baselinePath, "{\"SchemaVersion\":2}");
        File.WriteAllText(comparisonPath, "{\"Outcome\":\"Passed\"}");
        var paths = BenchmarkEvidenceReporter.NormalizePaths(
            fixture.RepositoryRoot,
            historyPath,
            baselinePath,
            comparisonPath,
            releaseEvidenceIntent: false);

        BenchmarkEvidenceReporter.InvalidateRequestedOutputs(fixture.RepositoryRoot, paths);

        await Assert.That(File.Exists(historyPath)).IsFalse();
        await Assert.That(File.Exists(comparisonPath)).IsFalse();
        await Assert.That(File.Exists(baselinePath)).IsTrue();
    }

    [Test]
    public async Task ValidatePathDependencies_RejectsComparisonWithoutBaselineAfterStaleInvalidation()
    {
        using var fixture = new BenchmarkFixture();
        var comparisonPath = fixture.OutputPath("orphaned-comparison.json");
        File.WriteAllText(comparisonPath, "{\"Outcome\":\"Passed\"}");

        var paths = BenchmarkEvidenceReporter.NormalizePaths(
            fixture.RepositoryRoot,
            historyJsonPath: null,
            baselinePath: null,
            comparisonPath,
            releaseEvidenceIntent: false);
        await Assert.That(File.Exists(comparisonPath)).IsTrue();

        BenchmarkEvidenceReporter.InvalidateRequestedOutputs(fixture.RepositoryRoot, paths);
        var failure = Capture(() => BenchmarkEvidenceReporter.ValidatePathDependencies(
            paths,
            releaseEvidenceIntent: false));

        await Assert.That(File.Exists(comparisonPath)).IsFalse();
        await Assert.That(failure).IsTypeOf<InvalidDataException>();
        await Assert.That(failure!.Message).Contains("--comparison-json requires --baseline");
    }

    [Test]
    public async Task CreateHistory_RunnerAndArtifactFailuresFailEvidenceClosed()
    {
        using var fixture = new BenchmarkFixture();
        var runnerInput = fixture.CreateInput("bad-runner");
        var badRunner = runnerInput.RunnerEvidence with { ValidForEvidence = false };
        var runnerArtifact = BenchmarkEvidenceReporter.CreateHistory(
            runnerInput with { RunnerEvidence = badRunner });

        var artifactInput = fixture.CreateInput("bad-artifact");
        var referencedFile = artifactInput.Artifacts.Files.Single(
            static artifact => artifact.Kind == "benchmarkdotnet-csv").Path;
        File.AppendAllText(referencedFile, "tampered");
        var artifactFailure = BenchmarkEvidenceReporter.CreateHistory(artifactInput);

        await Assert.That(runnerArtifact.IsCompleteForInvocation).IsTrue();
        await Assert.That(runnerArtifact.ArtifactsComplete).IsTrue();
        await Assert.That(runnerArtifact.ValidForEvidence).IsFalse();
        await Assert.That(artifactFailure.ArtifactsComplete).IsFalse();
        await Assert.That(artifactFailure.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task ReadHistory_RemovedRequiredArtifactReferenceInvalidatesPersistedEvidence()
    {
        using var fixture = new BenchmarkFixture();
        var historyPath = fixture.OutputPath("missing-reference.json");
        var artifact = BenchmarkEvidenceReporter.CreateHistory(
            fixture.CreateInput("missing-reference", historyPath: historyPath));
        var missingReference = artifact with
        {
            Artifacts = artifact.Artifacts! with
            {
                Files = artifact.Artifacts.Files
                    .Where(static reference => reference.Kind != "telemetry-json")
                    .ToArray()
            }
        };
        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, historyPath, missingReference);

        var persisted = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, historyPath);

        await Assert.That(persisted.Artifact.ArtifactsComplete).IsTrue();
        await Assert.That(persisted.Reference.SourceValidForEvidence).IsFalse();
    }

    [Test]
    public async Task ReadHistory_BenchmarkAssemblyPathMismatchInvalidatesPersistedEvidence()
    {
        using var fixture = new BenchmarkFixture();
        var historyPath = fixture.OutputPath("assembly-path-mismatch.json");
        var artifact = BenchmarkEvidenceReporter.CreateHistory(
            fixture.CreateInput("assembly-path-mismatch", historyPath: historyPath));
        var originalRunner = artifact.RunnerEvidence!;
        var mismatchedRunner = BenchmarkEvidenceReporter.EvaluateRunnerEvidence(
            originalRunner.Start,
            originalRunner.End,
            originalRunner.EntryAssembly,
            originalRunner.DevToolsAssembly,
            fixture.CreateAlternateBenchmarkAssemblyEvidence());
        var mismatch = artifact with { RunnerEvidence = mismatchedRunner };
        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, historyPath, mismatch);

        var persisted = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, historyPath);

        await Assert.That(mismatchedRunner.ValidForEvidence).IsTrue();
        await Assert.That(persisted.Reference.SourceValidForEvidence).IsFalse();
    }

    [Test]
    public async Task ReadHistory_RevalidatesCommandAndDistinctPathContract()
    {
        using var fixture = new BenchmarkFixture();
        var commandPath = fixture.OutputPath("invalid-command.json");
        var commandArtifact = BenchmarkEvidenceReporter.CreateHistory(
            fixture.CreateInput("invalid-command", historyPath: commandPath));
        BenchmarkEvidenceReporter.WriteHistory(
            fixture.RepositoryRoot,
            commandPath,
            commandArtifact with
            {
                Invocation = commandArtifact.Invocation! with { Command = "list" }
            });

        var aliasPath = fixture.OutputPath("aliased-evidence-paths.json");
        var aliasArtifact = BenchmarkEvidenceReporter.CreateHistory(
            fixture.CreateInput("aliased-evidence-paths", historyPath: aliasPath));
        BenchmarkEvidenceReporter.WriteHistory(
            fixture.RepositoryRoot,
            aliasPath,
            aliasArtifact with
            {
                Invocation = aliasArtifact.Invocation! with
                {
                    BaselinePath = aliasPath,
                    ComparisonJsonPath = aliasPath
                },
                Artifacts = aliasArtifact.Artifacts! with { ComparisonJsonPath = aliasPath }
            });

        var invalidCommand = BenchmarkEvidenceReporter.ReadHistory(
            fixture.RepositoryRoot,
            commandPath);
        var aliasedPaths = BenchmarkEvidenceReporter.ReadHistory(
            fixture.RepositoryRoot,
            aliasPath);

        await Assert.That(commandArtifact.ValidForEvidence).IsTrue();
        await Assert.That(aliasArtifact.ValidForEvidence).IsTrue();
        await Assert.That(invalidCommand.Reference.SourceValidForEvidence).IsFalse();
        await Assert.That(aliasedPaths.Reference.SourceValidForEvidence).IsFalse();
    }

    [Test]
    public async Task ReadHistory_PersistedRunnerHashSurvivesLaterBuildOutputReplacement()
    {
        using var fixture = new BenchmarkFixture();
        var historyPath = fixture.OutputPath("persisted-runner-hash.json");
        var artifact = BenchmarkEvidenceReporter.CreateHistory(
            fixture.CreateInput("persisted-runner-hash", historyPath: historyPath));
        BenchmarkEvidenceReporter.WriteHistory(fixture.RepositoryRoot, historyPath, artifact);

        fixture.ReplaceBenchmarkAssemblyOutput();
        var persisted = BenchmarkEvidenceReporter.ReadHistory(fixture.RepositoryRoot, historyPath);

        await Assert.That(persisted.Artifact.RunnerEvidence!.BenchmarkAssembly.Sha256).IsEqualTo(
            artifact.RunnerEvidence!.BenchmarkAssembly.Sha256);
        await Assert.That(persisted.Reference.SourceValidForEvidence).IsTrue();
    }

    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class BenchmarkFixture : IDisposable
    {
        internal const string HistoricalCommit = "89abcdef0123456789abcdef0123456789abcdef";
        private static readonly string[] MemoryMethods =
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
        ];

        public BenchmarkFixture()
        {
            RepositoryRoot = Path.Combine(
                AppContext.BaseDirectory,
                nameof(BenchmarkEvidenceReporterTests),
                Guid.NewGuid().ToString("N"));
            BenchmarkAssemblyPath = Path.Combine(
                RepositoryRoot,
                "src",
                "DataLinq.Benchmark",
                "bin",
                "Release",
                "net8.0",
                "DataLinq.Benchmark.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(BenchmarkAssemblyPath)!);
            File.WriteAllText(BenchmarkAssemblyPath, "benchmark assembly fixture");
            File.WriteAllText(
                Path.Combine(
                    RepositoryRoot,
                    "src",
                    "DataLinq.Benchmark",
                    "DataLinq.Benchmark.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var compatibilityDirectory = Path.Combine(RepositoryRoot, "src", "DataLinq.Benchmark.CLI");
            Directory.CreateDirectory(compatibilityDirectory);
            File.WriteAllText(Path.Combine(compatibilityDirectory, "BenchmarkTargetProvenance.targets"), "<Project />");
            File.WriteAllText(Path.Combine(compatibilityDirectory, "HistoricalBenchmarkConfig.cs.txt"), "// fixture");
            File.WriteAllText(
                Path.Combine(RepositoryRoot, "src", "DataLinq.Benchmark", "AllocationRegressionBenchmarks.cs"),
                "// fixture");
        }

        public string RepositoryRoot { get; }

        private string BenchmarkAssemblyPath { get; }

        public string OutputPath(string fileName)
        {
            var path = Path.Combine(RepositoryRoot, "artifacts", "benchmarks", "evidence", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        public BenchmarkRunMetadata CreateMetadata(string profile) =>
            new(
                Repository: "example/DataLinq",
                Branch: "v0.9",
                Commit,
                Workflow: "fixture",
                RunId: "123",
                RunNumber: "1",
                EventName: "workflow_dispatch",
                RunnerOs: "fixture-os",
                RunnerArchitecture: "x64",
                Profile: profile,
                Filter: "*")
            {
                RuntimeDescription = ".NET fixture",
                ProcessorCount = 1,
                ProcessorIdentifier = "fixture processor",
                BenchmarkDotNetVersion = "0.15.8"
            };

        public BenchmarkInvocation CreateInvocation(
            string runId,
            string historyPath,
            string profile = "heavy",
            string expectedJob = "MediumRun",
            bool releaseEvidenceIntent = true,
            bool noBuild = false)
        {
            var runDirectory = Path.Combine(RepositoryRoot, "artifacts", "benchmarks", "runs", runId);
            Directory.CreateDirectory(runDirectory);
            return new BenchmarkInvocation(
                Command: "run",
                RepositoryRoot,
                BenchmarkProjectPath: Path.Combine(
                    RepositoryRoot,
                    "src",
                    "DataLinq.Benchmark",
                    "DataLinq.Benchmark.csproj"),
                BenchmarkAssemblyPath,
                RunArtifactsDirectory: runDirectory,
                Profile: profile,
                ExpectedJob: expectedJob,
                Filter: "*",
                SelectedCategory: BenchmarkHarnessRunner.V09MemoryReadCategory,
                ConfiguredProviderIds: ["memory"],
                NoBuild: noBuild,
                KeepFiles: false,
                Verbose: false,
                AdditionalArguments: Array.Empty<string>(),
                ArgumentsRedacted: false,
                HistoryJsonPath: historyPath,
                BaselinePath: null,
                ComparisonJsonPath: null,
                WarningThresholdPercent: 10d,
                ReleaseEvidenceIntent: releaseEvidenceIntent);
        }

        public IReadOnlyList<BenchmarkHistoryArtifactRow> CreateMemoryRows(string expectedJob = "MediumRun") =>
            MemoryMethods.Select((method, index) => CreateRow(
                    method,
                    BenchmarkHarnessRunner.GetScenarioCategory(method),
                    expectedJob,
                    mean: 10d + index,
                    allocated: 100d + index))
                .ToArray();

        public BenchmarkHistoryArtifactRow CreateRow(
            string method,
            string category,
            string expectedJob = "MediumRun",
            double mean = 10d,
            double allocated = 100d)
        {
            var telemetry = CreateTelemetry(method);
            return new BenchmarkHistoryArtifactRow(
                Method: method,
                ProviderName: "memory",
                Category: category,
                MeanMicroseconds: mean,
                ErrorMicroseconds: 0.1d,
                MedianMicroseconds: mean,
                StdDevMicroseconds: 0.1d,
                MinMicroseconds: mean - 0.1d,
                MaxMicroseconds: mean + 0.1d,
                AllocatedBytes: allocated,
                NoisePercent: 1d,
                UncertaintyPercent: 1d,
                StdDevPercent: 1d,
                OperationsPerInvoke: 1,
                TrackingGroup: BenchmarkHarnessRunner.V09MemoryReadCategory,
                TelemetryDelta: telemetry)
            {
                Job = expectedJob,
                Runtime = ".NET fixture",
                Jit = "RyuJIT",
                Platform = "X64",
                Toolchain = "fixture"
            };
        }

        public BenchmarkHistoryCreationInput CreateInput(
            string runId,
            IReadOnlyList<BenchmarkHistoryArtifactRow>? rows = null,
            string profile = "heavy",
            string expectedJob = "MediumRun",
            bool releaseEvidenceIntent = true,
            bool noBuild = false,
            string? historyPath = null)
        {
            var actualHistoryPath = historyPath ?? OutputPath($"{runId}.json");
            var actualRows = rows ?? CreateMemoryRows(expectedJob);
            var invocation = CreateInvocation(
                runId,
                actualHistoryPath,
                profile,
                expectedJob,
                releaseEvidenceIntent,
                noBuild);
            var startedAt = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc);
            var completedAt = startedAt.AddSeconds(2);
            var logPath = WriteRunFile(runId, "benchmark.log", "benchmark log");
            var references = new List<BenchmarkArtifactReference>
            {
                Reference("benchmark-log", logPath),
                Reference("summary-json", WriteRunFile(runId, "summary.json", "{}", underResults: true)),
                Reference("benchmarkdotnet-csv", WriteRunFile(
                    runId,
                    "report.csv",
                    "Method,Mean\nfixture,1",
                    underResults: true)),
                Reference("benchmarkdotnet-markdown", WriteRunFile(
                    runId,
                    "report.md",
                    "# Benchmark",
                    underResults: true))
            };
            for (var index = 0; index < actualRows.Count; index++)
            {
                references.Add(Reference(
                    "telemetry-json",
                    WriteRunFile(
                        runId,
                        $"telemetry-{index}.json",
                        JsonSerializer.Serialize(actualRows[index].TelemetryDelta),
                        underResults: true)));
            }

            var commands = new List<BenchmarkCommandRecord>();
            if (!invocation.NoBuild)
            {
                var restoreLogPath = WriteRunFile(runId, "restore.log", "restore log");
                var buildLogPath = WriteRunFile(runId, "build.log", "build log");
                references.Insert(0, Reference("build-log", buildLogPath));
                references.Insert(0, Reference("restore-log", restoreLogPath));
                var buildEnvironment = new BenchmarkCommandEnvironment(
                    Profile: null,
                    BenchmarkRunId: null,
                    ArtifactsDirectory: null,
                    ResultsDirectory: null,
                    ProviderIds: ["memory"]);
                commands.Add(new BenchmarkCommandRecord(
                    Stage: "restore",
                    Executable: "dotnet",
                    Arguments:
                    [
                        "restore",
                        invocation.BenchmarkProjectPath,
                        "-nologo",
                        "-v",
                        "q",
                        "-p:NuGetAudit=false"
                    ],
                    WorkingDirectory: RepositoryRoot,
                    StartedAtUtc: startedAt,
                    CompletedAtUtc: completedAt,
                    DurationSeconds: 2d,
                    ExitCode: 0,
                    LogPath: restoreLogPath,
                    Environment: buildEnvironment));
                commands.Add(new BenchmarkCommandRecord(
                    Stage: "build",
                    Executable: "dotnet",
                    Arguments:
                    [
                        "build",
                        invocation.BenchmarkProjectPath,
                        "--no-restore",
                        "-c",
                        "Release",
                        "-f",
                        "net8.0",
                        "-nologo",
                        "-v",
                        "q",
                        "-p:NuGetAudit=false"
                    ],
                    WorkingDirectory: RepositoryRoot,
                    StartedAtUtc: startedAt,
                    CompletedAtUtc: completedAt,
                    DurationSeconds: 2d,
                    ExitCode: 0,
                    LogPath: buildLogPath,
                    Environment: buildEnvironment));
            }
            var benchmarkCommand = new BenchmarkCommandRecord(
                Stage: "benchmark",
                Executable: "dotnet",
                Arguments:
                [
                    BenchmarkAssemblyPath,
                    "--artifacts",
                    invocation.RunArtifactsDirectory,
                    "--filter",
                    "*",
                    "--join",
                    "--disableLogFile",
                    "--anyCategories",
                    BenchmarkHarnessRunner.V09MemoryReadCategory
                ],
                WorkingDirectory: Path.GetDirectoryName(invocation.BenchmarkProjectPath)!,
                StartedAtUtc: startedAt,
                CompletedAtUtc: completedAt,
                DurationSeconds: 2d,
                ExitCode: 0,
                LogPath: logPath,
                Environment: new BenchmarkCommandEnvironment(
                    Profile: profile,
                    BenchmarkRunId: runId,
                    ArtifactsDirectory: invocation.RunArtifactsDirectory,
                    ResultsDirectory: Path.Combine(invocation.RunArtifactsDirectory, "results"),
                    ProviderIds: ["memory"]));
            commands.Add(benchmarkCommand);

            return new BenchmarkHistoryCreationInput(
                RunId: runId,
                StartedAtUtc: startedAt,
                CompletedAtUtc: completedAt,
                Metadata: CreateMetadata(profile),
                Invocation: invocation,
                Rows: actualRows,
                Commands: commands,
                Warnings: Array.Empty<BenchmarkWarning>(),
                Failure: null,
                Artifacts: new BenchmarkArtifactPaths(
                    HistoryJsonPath: actualHistoryPath,
                    ComparisonJsonPath: null,
                    Files: references),
                RunnerEvidence: CreateRunnerEvidence());
        }

        public BenchmarkHistoryCreationInput CreateHistoricalTargetInput(string runId, string historyPath)
        {
            var input = CreateInput(runId, historyPath: historyPath);
            var targetRoot = Path.Combine(RepositoryRoot, "artifacts", "benchmarks", "targets", "final-0.8");
            var projectPath = Path.Combine(targetRoot, "src", "DataLinq.Benchmark", "DataLinq.Benchmark.csproj");
            var assemblyPath = Path.Combine(
                targetRoot,
                "src",
                "DataLinq.Benchmark",
                "bin",
                "Release",
                "net8.0",
                "DataLinq.Benchmark.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(assemblyPath, "historical benchmark assembly fixture");

            var invocation = input.Invocation with
            {
                BenchmarkProjectPath = projectPath,
                BenchmarkAssemblyPath = assemblyPath,
                BenchmarkTargetRepositoryRoot = targetRoot
            };
            var commands = input.Commands.Select((command, index) => index switch
            {
                0 => command with
                {
                    Arguments = command.Arguments.Select((argument, argumentIndex) =>
                        argumentIndex == 1 ? projectPath : argument).ToArray()
                },
                1 => command with
                {
                    Arguments = command.Arguments.Select((argument, argumentIndex) =>
                            argumentIndex == 1 ? projectPath : argument)
                        .Concat(
                        [
                            $"-p:CustomAfterMicrosoftCommonTargets={Path.Combine(RepositoryRoot, "src", "DataLinq.Benchmark.CLI", "BenchmarkTargetProvenance.targets")}",
                            $"-p:DataLinqBenchmarkTargetRepositoryRoot={targetRoot}",
                            $"-p:DataLinqBenchmarkCompatibilitySource={Path.Combine(RepositoryRoot, "src", "DataLinq.Benchmark.CLI", "HistoricalBenchmarkConfig.cs.txt")}",
                            $"-p:DataLinqBenchmarkCalibrationSource={Path.Combine(RepositoryRoot, "src", "DataLinq.Benchmark", "AllocationRegressionBenchmarks.cs")}"
                        ])
                        .ToArray()
                },
                _ => command with
                {
                    Arguments = command.Arguments.Select((argument, argumentIndex) =>
                        argumentIndex == 0 ? assemblyPath : argument).ToArray(),
                    WorkingDirectory = Path.GetDirectoryName(projectPath)!
                }
            })
            .Select(command => command with
            {
                Environment = command.Environment with
                {
                    CustomAfterMicrosoftCommonTargets = Path.Combine(
                        RepositoryRoot,
                        "src",
                        "DataLinq.Benchmark.CLI",
                        "BenchmarkTargetProvenance.targets"),
                    BenchmarkTargetRepositoryRoot = targetRoot,
                    BenchmarkCompatibilitySource = Path.Combine(
                        RepositoryRoot,
                        "src",
                        "DataLinq.Benchmark.CLI",
                        "HistoricalBenchmarkConfig.cs.txt"),
                    BenchmarkCalibrationSource = Path.Combine(
                        RepositoryRoot,
                        "src",
                        "DataLinq.Benchmark",
                        "AllocationRegressionBenchmarks.cs")
                }
            })
            .ToArray();
            var toolingState = new TestRunSummaryRepositoryState(true, Commit, "v0.9", false, "clean");
            var targetState = new TestRunSummaryRepositoryState(true, HistoricalCommit, "HEAD", false, "clean");
            var benchmarkAssembly = new BenchmarkAssemblyEvidence(
                assemblyPath,
                ComputeSha256(assemblyPath),
                RunnerAssembly("DataLinq.Benchmark", HistoricalCommit));
            var runnerEvidence = BenchmarkEvidenceReporter.EvaluateRunnerEvidence(
                toolingState,
                toolingState,
                RunnerAssembly("DataLinq.Benchmark.CLI"),
                RunnerAssembly("DataLinq.DevTools"),
                benchmarkAssembly,
                targetState,
                targetState);

            return input with
            {
                Metadata = CreateMetadata("heavy") with
                {
                    Branch = targetState.Branch,
                    Commit = targetState.Commit
                },
                Invocation = invocation,
                Commands = commands,
                RunnerEvidence = runnerEvidence
            };
        }

        public BenchmarkHistoryReadResult CreateAndReadHistory(
            string runId,
            IReadOnlyList<BenchmarkHistoryArtifactRow>? rows = null)
        {
            var historyPath = OutputPath($"{runId}.json");
            var artifact = BenchmarkEvidenceReporter.CreateHistory(
                CreateInput(runId, rows, historyPath: historyPath));
            BenchmarkEvidenceReporter.WriteHistory(RepositoryRoot, historyPath, artifact);
            return BenchmarkEvidenceReporter.ReadHistory(RepositoryRoot, historyPath);
        }

        public BenchmarkAssemblyEvidence CreateAlternateBenchmarkAssemblyEvidence()
        {
            var alternatePath = Path.Combine(
                RepositoryRoot,
                "artifacts",
                "benchmarks",
                "alternate",
                "DataLinq.Benchmark.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(alternatePath)!);
            File.WriteAllText(alternatePath, "alternate benchmark assembly fixture");
            return new BenchmarkAssemblyEvidence(
                alternatePath,
                ComputeSha256(alternatePath),
                RunnerAssembly("DataLinq.Benchmark"));
        }

        public void ReplaceBenchmarkAssemblyOutput() =>
            File.WriteAllText(BenchmarkAssemblyPath, "a later benchmark build output");

        public void Dispose()
        {
            if (Directory.Exists(RepositoryRoot))
                Directory.Delete(RepositoryRoot, recursive: true);
        }

        private BenchmarkRunnerEvidence CreateRunnerEvidence()
        {
            var state = new TestRunSummaryRepositoryState(
                Captured: true,
                Commit,
                Branch: "v0.9",
                Dirty: false,
                StatusSha256: "clean");
            var benchmarkAssembly = new BenchmarkAssemblyEvidence(
                BenchmarkAssemblyPath,
                ComputeSha256(BenchmarkAssemblyPath),
                RunnerAssembly("DataLinq.Benchmark"));
            return BenchmarkEvidenceReporter.EvaluateRunnerEvidence(
                state,
                state,
                RunnerAssembly("DataLinq.Benchmark.CLI"),
                RunnerAssembly("DataLinq.DevTools"),
                benchmarkAssembly);
        }

        private BenchmarkArtifactReference Reference(string kind, string path) =>
            BenchmarkEvidenceReporter.CreateArtifactReference(RepositoryRoot, kind, path);

        private string WriteRunFile(
            string runId,
            string fileName,
            string content,
            bool underResults = false)
        {
            var path = Path.Combine(
                RepositoryRoot,
                "artifacts",
                "benchmarks",
                "runs",
                runId,
                underResults ? "results" : string.Empty,
                fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        internal static TestRunSummaryRunnerAssembly RunnerAssembly(string name, string commit = Commit) =>
            new(
                name,
                InformationalVersion: $"0.9.0+{commit}",
                RepositoryCommit: commit,
                RepositoryCommitCaptured: true,
                RepositoryBuildState: "clean");

        private static BenchmarkTelemetryDeltaArtifact CreateTelemetry(string method)
        {
            var telemetry = new BenchmarkTelemetryDeltaArtifact(
                Method: method,
                ProviderName: "memory",
                OperationsPerInvoke: 1,
                EntityQueriesPerOperation: 0d,
                ScalarQueriesPerOperation: 0d,
                TransactionStartsPerOperation: 0d,
                TransactionCommitsPerOperation: 0d,
                TransactionRollbacksPerOperation: 0d,
                MutationInsertsPerOperation: 0d,
                MutationUpdatesPerOperation: 0d,
                MutationDeletesPerOperation: 0d,
                MutationAffectedRowsPerOperation: 0d,
                RowCacheHitsPerOperation: 0d,
                RowCacheMissesPerOperation: 0d,
                RowCacheStoresPerOperation: 0d,
                DatabaseRowsPerOperation: 0d,
                MaterializationsPerOperation: 0d,
                RelationHitsPerOperation: 0d,
                RelationLoadsPerOperation: 0d,
                CacheInvalidationOperationsPerOperation: 0d,
                CacheInvalidationRowsRemovedPerOperation: 0d,
                CacheInvalidationTablesClearedPerOperation: 0d,
                CacheInvalidationProviderKeysPerOperation: 0d,
                CacheInvalidationApproximateWorkPerOperation: 0d,
                CacheInvalidationPreciseOperationsPerOperation: 0d,
                CacheInvalidationConservativeFallbackOperationsPerOperation: 0d);
            return method switch
            {
                "Memory database construction" => telemetry with
                {
                    MemoryDatabasesConstructedPerOperation = 1d
                },
                "Memory construct and seed" => telemetry with
                {
                    MemoryDatabasesConstructedPerOperation = 1d,
                    MemoryRowsSeededPerOperation = 1280d
                },
                "Memory primary-key hit" => telemetry with
                {
                    MemoryPrimaryKeyRequestsPerOperation = 1d,
                    MemoryPrimaryKeyProbesPerOperation = 1d,
                    MemoryCacheLookupsPerOperation = 1d,
                    MemoryCacheHitsPerOperation = 1d
                },
                "Memory primary-key miss" => telemetry with
                {
                    MemoryPrimaryKeyRequestsPerOperation = 1d,
                    MemoryPrimaryKeyProbesPerOperation = 1d,
                    MemoryCacheMissesPerOperation = 1d
                },
                "Memory scalar scan" => telemetry with
                {
                    MemoryScanRowsVisitedPerOperation = 1280d
                },
                "Memory filter order page" or
                "Memory direct-Guid equality count" or
                "Memory typed-ID equality count" => telemetry with
                {
                    MemoryScanRowsVisitedPerOperation = 1280d,
                    MemoryPredicateEvaluationsPerOperation = 1280d,
                    MemoryPredicateRejectionsPerOperation = 1279d
                },
                "Memory repeated entity identity" => telemetry with
                {
                    MemoryScanRowsVisitedPerOperation = 1024d,
                    MemoryPredicateEvaluationsPerOperation = 1024d,
                    MemoryPredicateRejectionsPerOperation = 1023d,
                    MemoryCacheLookupsPerOperation = 1d,
                    MemoryCacheHitsPerOperation = 1d
                },
                _ => telemetry
            };
        }

        private static string ComputeSha256(string path) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }
}
