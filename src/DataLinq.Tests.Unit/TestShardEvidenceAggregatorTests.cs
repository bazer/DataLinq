using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class TestShardEvidenceAggregatorTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string BaselineCommit = "abcdef0123456789abcdef0123456789abcdef01";

    [Test]
    public async Task AggregateDirectory_RequiresCompleteFullMatrixAndProducesRatchetBaseline()
    {
        using var fixture = new ShardFixture();
        fixture.WriteContract();

        var aggregate = TestShardEvidenceAggregator.AggregateDirectory(fixture.Root, Commit, "Release");

        await Assert.That(aggregate.Complete).IsTrue();
        await Assert.That(aggregate.Shards).Count().IsEqualTo(17);
        await Assert.That(aggregate.TotalCases).IsEqualTo(5436);
        await Assert.That(aggregate.PreviousBaselineCommitSha).IsNull();
        await Assert.That(aggregate.CaseCountBaseline.SchemaVersion)
            .IsEqualTo(TestShardEvidenceAggregator.BaselineSchemaVersion);
        await Assert.That(aggregate.CaseCountBaseline.Epoch)
            .IsEqualTo(TestShardEvidenceAggregator.BaselineEpoch);
        await Assert.That(aggregate.CaseCountBaseline.CommitSha).IsEqualTo(Commit);
        await Assert.That(aggregate.CaseCountBaseline.Shards).Count().IsEqualTo(17);
        await Assert.That(aggregate.Shards.Select(static shard => $"{shard.Suite}:{shard.TargetId ?? "-"}").Distinct()).Count().IsEqualTo(17);
    }

    [Test]
    public async Task AggregateDirectory_AcceptsCaseGrowthAndRatchetsObservedCounts()
    {
        using var fixture = new ShardFixture();
        fixture.WriteContract(countOffsets: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit:-"] = 1,
            ["compliance:sqlite-file"] = 2
        });

        var aggregate = TestShardEvidenceAggregator.AggregateDirectory(fixture.Root, Commit, "Release");
        var unit = aggregate.CaseCountBaseline.Shards.Single(static shard => shard.Suite == "unit");
        var sqliteAnchor = aggregate.CaseCountBaseline.Shards.Single(static shard =>
            shard.Suite == "compliance" && shard.TargetId == "sqlite-file");

        await Assert.That(aggregate.TotalCases).IsEqualTo(5439);
        await Assert.That(unit.Cases).IsEqualTo(TestShardEvidenceAggregator.CompleteUnitMinimumCases + 1);
        await Assert.That(sqliteAnchor.Cases)
            .IsEqualTo(TestShardEvidenceAggregator.ComplianceSqliteAnchorMinimumCases + 2);
    }

    [Test]
    public async Task AggregateDirectory_RejectsMissingAndDuplicateTargets()
    {
        using var missingFixture = new ShardFixture();
        missingFixture.WriteContract(skipKey: "compliance:mariadb-12.3");
        var missing = Capture(() => TestShardEvidenceAggregator.AggregateDirectory(missingFixture.Root, Commit, "Release"));

        using var duplicateFixture = new ShardFixture();
        duplicateFixture.WriteContract(duplicateKey: "mysql:mysql-9.7");
        var duplicate = Capture(() => TestShardEvidenceAggregator.AggregateDirectory(duplicateFixture.Root, Commit, "Release"));

        await Assert.That(missing).IsTypeOf<InvalidDataException>();
        await Assert.That(missing!.Message).Contains("Missing").And.Contains("compliance:mariadb-12.3");
        await Assert.That(duplicate).IsTypeOf<InvalidDataException>();
        await Assert.That(duplicate!.Message).Contains("Duplicate full-matrix shards");
    }

    [Test]
    public async Task AggregateDirectory_RejectsWrongCommitConfigurationSchemaAndCaseRegression()
    {
        using var commitFixture = new ShardFixture();
        commitFixture.WriteContract(commitOverride: new string('a', 40));
        var commit = Capture(() => TestShardEvidenceAggregator.AggregateDirectory(commitFixture.Root, Commit, "Release"));

        using var configurationFixture = new ShardFixture();
        configurationFixture.WriteContract(configurationOverride: "Debug");
        var configuration = Capture(() => TestShardEvidenceAggregator.AggregateDirectory(configurationFixture.Root, Commit, "Release"));

        using var schemaFixture = new ShardFixture();
        schemaFixture.WriteContract(schemaOverride: "future-schema");
        var schema = Capture(() => TestShardEvidenceAggregator.AggregateDirectory(schemaFixture.Root, Commit, "Release"));

        using var countFixture = new ShardFixture();
        countFixture.WriteContract(countOffsets: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit:-"] = -1
        });
        var count = Capture(() => TestShardEvidenceAggregator.AggregateDirectory(countFixture.Root, Commit, "Release"));

        await Assert.That(commit!.Message).Contains("does not match commit");
        await Assert.That(configuration!.Message).Contains("configuration 'Release'");
        await Assert.That(schema!.Message).Contains("incompatible schema");
        await Assert.That(count!.Message).Contains("case-count regression").And.Contains("required at least 1686");
    }

    [Test]
    public async Task AggregateDirectory_ReportsEveryCountAndRoleMismatchTogether()
    {
        using var fixture = new ShardFixture();
        fixture.WriteContract(
            countOffsets: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["unit:-"] = -1,
                ["compliance:sqlite-file"] = -2
            },
            roleOverrides: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["mysql:mysql-8.4"] = "AnchorWithInvariant"
            });

        var failure = Capture(() => TestShardEvidenceAggregator.AggregateDirectory(
            fixture.Root,
            Commit,
            "Release"));

        await Assert.That(failure).IsTypeOf<InvalidDataException>();
        await Assert.That(failure!.Message)
            .Contains("unit:-")
            .And.Contains("compliance:sqlite-file")
            .And.Contains("mysql:mysql-8.4")
            .And.Contains("role mismatch");
    }

    [Test]
    public async Task AggregateDirectory_UsesPreviousSuccessfulCountsAsHigherFloors()
    {
        using var regressionFixture = new ShardFixture();
        var regressionBaseline = regressionFixture.WriteBaseline(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit:-"] = 5
        });
        regressionFixture.WriteContract(countOffsets: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit:-"] = 4
        });
        var regression = Capture(() => TestShardEvidenceAggregator.AggregateDirectory(
            regressionFixture.Root,
            Commit,
            "Release",
            regressionBaseline));

        using var acceptedFixture = new ShardFixture();
        var acceptedBaseline = acceptedFixture.WriteBaseline(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit:-"] = 5
        });
        acceptedFixture.WriteContract(countOffsets: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit:-"] = 6
        });
        var accepted = TestShardEvidenceAggregator.AggregateDirectory(
            acceptedFixture.Root,
            Commit,
            "Release",
            acceptedBaseline);

        await Assert.That(regression).IsTypeOf<InvalidDataException>();
        await Assert.That(regression!.Message)
            .Contains("previous successful baseline 1691")
            .And.Contains(BaselineCommit);
        await Assert.That(accepted.PreviousBaselineCommitSha).IsEqualTo(BaselineCommit);
        await Assert.That(accepted.CaseCountBaseline.Shards.Single(static shard => shard.Suite == "unit").Cases)
            .IsEqualTo(TestShardEvidenceAggregator.CompleteUnitMinimumCases + 6);
    }

    [Test]
    public async Task AggregateDirectory_RejectsFutureOrIncompleteBaselines()
    {
        using var futureFixture = new ShardFixture();
        var futureBaseline = futureFixture.WriteBaseline(
            epoch: TestShardEvidenceAggregator.BaselineEpoch + 1);
        futureFixture.WriteContract();
        var future = Capture(() => TestShardEvidenceAggregator.AggregateDirectory(
            futureFixture.Root,
            Commit,
            "Release",
            futureBaseline));

        using var incompleteFixture = new ShardFixture();
        var incompleteBaseline = incompleteFixture.WriteBaseline(skipKey: "mysql:mariadb-12.3");
        incompleteFixture.WriteContract();
        var incomplete = Capture(() => TestShardEvidenceAggregator.AggregateDirectory(
            incompleteFixture.Root,
            Commit,
            "Release",
            incompleteBaseline));

        await Assert.That(future).IsTypeOf<InvalidDataException>();
        await Assert.That(future!.Message).Contains("newer than supported epoch");
        await Assert.That(incomplete).IsTypeOf<InvalidDataException>();
        await Assert.That(incomplete!.Message)
            .Contains("Case-count baseline coverage mismatch")
            .And.Contains("mysql:mariadb-12.3");
    }

    [Test]
    public async Task AggregateDirectory_RejectsUnattestedShardRunnerProvenance()
    {
        using var checkoutFixture = new ShardFixture();
        checkoutFixture.WriteContract();
        checkoutFixture.ReplaceInSummary(
            "unit-local-summary.json",
            "\"AssembliesMatchCheckout\": true",
            "\"AssembliesMatchCheckout\": false");
        var checkout = Capture(() => TestShardEvidenceAggregator.AggregateDirectory(
            checkoutFixture.Root,
            Commit,
            "Release"));

        using var buildFixture = new ShardFixture();
        buildFixture.WriteContract();
        buildFixture.ReplaceInSummary(
            "unit-local-summary.json",
            "\"AssembliesBuiltFromCleanState\": true",
            "\"AssembliesBuiltFromCleanState\": false");
        var build = Capture(() => TestShardEvidenceAggregator.AggregateDirectory(
            buildFixture.Root,
            Commit,
            "Release"));

        await Assert.That(checkout).IsTypeOf<InvalidDataException>();
        await Assert.That(checkout!.Message).Contains("stable clean checkout");
        await Assert.That(build).IsTypeOf<InvalidDataException>();
        await Assert.That(build!.Message).Contains("stable clean checkout");
    }

    [Test]
    public async Task AggregateDirectory_TreatsEmptyTargetlessRoleAsAbsent()
    {
        using var fixture = new ShardFixture();
        fixture.WriteContract(emptyRoleKey: "unit:-");

        var aggregate = TestShardEvidenceAggregator.AggregateDirectory(fixture.Root, Commit, "Release");
        var unit = aggregate.Shards.Single(static shard => shard.Suite == "unit");

        await Assert.That(unit.ProviderAffinityRole).IsNull();
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

    private sealed class ShardFixture : IDisposable
    {
        public ShardFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"datalinq-shards-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void WriteContract(
            string? skipKey = null,
            string? duplicateKey = null,
            string? commitOverride = null,
            string? configurationOverride = null,
            string? schemaOverride = null,
            string? emptyRoleKey = null,
            IReadOnlyDictionary<string, int>? countOffsets = null,
            IReadOnlyDictionary<string, string?>? roleOverrides = null)
        {
            var index = 0;
            foreach (var contract in TestShardEvidenceAggregator.FullMatrixContract)
            {
                var key = $"{contract.Suite}:{contract.TargetId ?? "-"}";
                if (string.Equals(key, skipKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                var shardContract = string.Equals(key, emptyRoleKey, StringComparison.OrdinalIgnoreCase)
                    ? contract with { ProviderAffinityRole = string.Empty }
                    : roleOverrides?.TryGetValue(key, out var roleOverride) == true
                        ? contract with { ProviderAffinityRole = roleOverride }
                        : contract;
                WriteShard(
                    index++,
                    shardContract,
                    commitOverride ?? Commit,
                    configurationOverride ?? "Release",
                    schemaOverride ?? TestRunSummaryReporter.SchemaVersion,
                    contract.MinimumCases + (countOffsets?.GetValueOrDefault(key) ?? 0));
                if (string.Equals(key, duplicateKey, StringComparison.OrdinalIgnoreCase))
                    WriteShard(index++, contract, Commit, "Release", TestRunSummaryReporter.SchemaVersion, contract.MinimumCases);
            }
        }

        public string WriteBaseline(
            IReadOnlyDictionary<string, int>? countOffsets = null,
            int? epoch = null,
            string? skipKey = null)
        {
            var baseline = new TestShardEvidenceBaseline(
                TestShardEvidenceAggregator.BaselineSchemaVersion,
                epoch ?? TestShardEvidenceAggregator.BaselineEpoch,
                BaselineCommit,
                TestShardEvidenceAggregator.FullMatrixContract
                    .Where(contract => !string.Equals(
                        $"{contract.Suite}:{contract.TargetId ?? "-"}",
                        skipKey,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(contract =>
                {
                    var key = $"{contract.Suite}:{contract.TargetId ?? "-"}";
                    return new TestShardEvidenceBaselineEntry(
                        contract.Suite,
                        contract.TargetId,
                        contract.ProviderAffinityRole,
                        contract.MinimumCases + (countOffsets?.GetValueOrDefault(key) ?? 0));
                }).ToArray());
            var path = Path.Combine(Root, "full-matrix-baseline.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());
            File.WriteAllText(path, JsonSerializer.Serialize(baseline, options));
            return path;
        }

        public void ReplaceInSummary(string fileName, string oldValue, string newValue)
        {
            var path = Directory
                .EnumerateFiles(Root, fileName, SearchOption.AllDirectories)
                .Single();
            var content = File.ReadAllText(path);
            if (!content.Contains(oldValue, StringComparison.Ordinal))
                throw new InvalidDataException($"Summary '{path}' did not contain '{oldValue}'.");
            File.WriteAllText(path, content.Replace(oldValue, newValue, StringComparison.Ordinal));
        }

        private void WriteShard(
            int index,
            TestShardEvidenceContract contract,
            string commit,
            string configuration,
            string schema,
            int cases)
        {
            var artifactRoot = Path.Combine(Root, $"artifact-{index:D2}");
            var resultRoot = Path.Combine(artifactRoot, "artifacts", "test-results", $"run-{index:D2}");
            var summaryRoot = Path.Combine(artifactRoot, "artifacts", "ci");
            Directory.CreateDirectory(resultRoot);
            Directory.CreateDirectory(summaryRoot);
            var log = Path.Combine(resultRoot, $"run-{index:D2}.log");
            var html = Path.Combine(resultRoot, $"run-{index:D2}.html");
            var trx = Path.Combine(resultRoot, $"run-{index:D2}.trx");
            File.WriteAllText(log, "log");
            File.WriteAllText(html, "html");
            File.WriteAllText(trx, "trx");

            var targetIds = contract.TargetId is null ? Array.Empty<string>() : new[] { contract.TargetId };
            var project = $"/repo/src/{contract.Suite}/{contract.Suite}.csproj";
            var repositoryState = new TestRunSummaryRepositoryState(true, commit, "master", false, "clean");
            var runner = new TestRunSummaryRunnerAssembly("runner", "1.0", commit, true, "clean");
            var environment = new TestRunSummaryCommandEnvironment(
                contract.TargetId?.Contains("mysql", StringComparison.OrdinalIgnoreCase) == true ||
                    contract.TargetId?.Contains("mariadb", StringComparison.OrdinalIgnoreCase) == true,
                contract.TargetId?.Contains("mysql", StringComparison.OrdinalIgnoreCase) == true ||
                    contract.TargetId?.Contains("mariadb", StringComparison.OrdinalIgnoreCase) == true,
                contract.TargetId is null ? null : "127.0.0.1",
                contract.TargetId is not null,
                true,
                targetIds);
            var performance = new TestRunSummaryPerformance(
                true, null, cases, cases, 0.01, 0.01, 0.01, 0.01, 1, 8, "TUNIT_MAX_PARALLEL_TESTS", [], []);
            var result = new TestRunSummaryResult(
                contract.Suite,
                project,
                contract.TargetId is null ? null : 1,
                contract.TargetId ?? "-",
                targetIds,
                TestRunSummaryOutcome.Passed,
                "dotnet",
                [],
                "/repo",
                environment,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                1,
                0,
                cases,
                cases,
                0,
                0,
                [log, html, trx],
                log,
                html,
                trx,
                0,
                performance,
                contract.ProviderAffinityRole);
            var expected = new TestRunSummaryExpectedResult(
                contract.Suite,
                project,
                contract.TargetId is null ? null : 1,
                targetIds,
                contract.ProviderAffinityRole);
            var invocation = new TestRunSummaryInvocation(
                "run",
                "/repo",
                null,
                contract.TargetId is null
                    ? []
                    : [new TestRunSummaryTarget(contract.TargetId, contract.TargetId, "test", true, 1234)],
                [new TestRunSummarySuite(contract.Suite, project, contract.TargetId is not null, contract.Suite == "compliance")],
                new TestRunSummarySafeEnvironment(false, true, null, "targets", true),
                false,
                false,
                true,
                contract.Suite,
                null,
                null,
                configuration,
                true,
                1,
                false,
                false,
                "Failures",
                ToolingProfile.Ci,
                null,
                8,
                contract.ProviderAffinityRole);
            var report = new TestRunSummaryReport(
                schema,
                $"run-{index:D2}",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                1,
                invocation,
                Path.Combine(summaryRoot, $"{contract.Suite}-{contract.TargetId ?? "local"}-summary.json"),
                TestRunSummaryOutcome.Passed,
                true,
                true,
                true,
                false,
                contract.TargetId is not null,
                false,
                0,
                cases,
                cases,
                0,
                0,
                new TestRunSummaryTimingBreakdown(1, 0, 1, cases, 0),
                new TestRunSummaryRuntimeEnvironment("Linux", "X64", ".NET 10.0.0", 4),
                // A single shard is not independently valid release evidence because it is
                // intentionally narrower than the canonical full matrix. The aggregate must
                // validate its runner provenance fields without requiring full-matrix scope.
                new TestRunSummaryRunnerEvidence(repositoryState, repositoryState, runner, runner, false, true, true, false),
                [expected],
                [],
                [result],
                [log, html, trx],
                null,
                null);

            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());
            File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, options));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
