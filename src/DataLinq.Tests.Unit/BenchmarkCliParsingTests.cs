using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataLinq.Benchmark.CLI;

namespace DataLinq.Tests.Unit;

public class BenchmarkCliParsingTests
{
    [Test]
    [Arguments("1.25 \u03BCs")]
    [Arguments("1.25 \u00B5s")]
    [Arguments("1.25 us")]
    public async Task DurationParser_AcceptsMicrosecondUnitVariants(string value)
    {
        var result = BenchmarkHarnessRunner.TryParseDurationInMicroseconds(value);

        await Assert.That(result).IsEqualTo(1.25d);
    }

    [Test]
    public async Task DurationParser_ConvertsNanosecondsToMicroseconds()
    {
        var result = BenchmarkHarnessRunner.TryParseDurationInMicroseconds("250 ns");

        await Assert.That(result).IsEqualTo(0.25d);
    }

    [Test]
    public async Task ProfileCompatibility_DefaultsMissingProfileToDefault()
    {
        var result = BenchmarkHarnessRunner.AreBenchmarkProfilesCompatible(null, "default");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ProfileCompatibility_RejectsMixedBenchmarkProfiles()
    {
        var result = BenchmarkHarnessRunner.AreBenchmarkProfilesCompatible("default", "heavy");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CategorySelection_SelectsV09QueryBackend()
    {
        var result = BenchmarkHarnessRunner.ResolveSelectedCategory(
            phase2Watch: false,
            phase3QueryHotPath: false,
            phase10KeyFoundation: false,
            phase11CacheInvalidation: false,
            phase12CacheMemory: false,
            v09QueryBackend: true,
            v09MemoryRead: false,
            allocationRegression: false,
            allocationStages: false);

        await Assert.That(result).IsEqualTo(BenchmarkHarnessRunner.V09QueryBackendCategory);
    }

    [Test]
    public async Task CategorySelection_LeavesCategoryUnsetWhenNoSelectorIsEnabled()
    {
        var result = BenchmarkHarnessRunner.ResolveSelectedCategory(
            phase2Watch: false,
            phase3QueryHotPath: false,
            phase10KeyFoundation: false,
            phase11CacheInvalidation: false,
            phase12CacheMemory: false,
            v09QueryBackend: false,
            v09MemoryRead: false,
            allocationRegression: false,
            allocationStages: false);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task CategorySelection_RejectsCombinedSelectors()
    {
        InvalidOperationException? exception = null;

        try
        {
            _ = BenchmarkHarnessRunner.ResolveSelectedCategory(
                phase2Watch: true,
                phase3QueryHotPath: false,
                phase10KeyFoundation: false,
                phase11CacheInvalidation: false,
                phase12CacheMemory: false,
                v09QueryBackend: true,
                v09MemoryRead: false,
                allocationRegression: true,
                allocationStages: false);
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("--phase2-watch")
            .And.Contains("--v09-query-backend");
    }

    [Test]
    public async Task CategorySelection_SelectsV09MemoryRead()
    {
        var result = BenchmarkHarnessRunner.ResolveSelectedCategory(
            phase2Watch: false,
            phase3QueryHotPath: false,
            phase10KeyFoundation: false,
            phase11CacheInvalidation: false,
            phase12CacheMemory: false,
            v09QueryBackend: false,
            v09MemoryRead: true,
            allocationRegression: false,
            allocationStages: false);

        await Assert.That(result).IsEqualTo(BenchmarkHarnessRunner.V09MemoryReadCategory);
    }

    [Test]
    public async Task CategorySelection_SelectsAllocationRegression()
    {
        var result = BenchmarkHarnessRunner.ResolveSelectedCategory(
            phase2Watch: false,
            phase3QueryHotPath: false,
            phase10KeyFoundation: false,
            phase11CacheInvalidation: false,
            phase12CacheMemory: false,
            v09QueryBackend: false,
            v09MemoryRead: false,
            allocationRegression: true,
            allocationStages: false);

        await Assert.That(result).IsEqualTo(BenchmarkHarnessRunner.AllocationRegressionCategory);
        await Assert.That(BenchmarkHarnessRunner.GetBenchmarkCategoryArguments(result))
            .IsEquivalentTo(["--anyCategories", "allocation-regression"]);
    }

    [Test]
    [Arguments("Canonical provider-row decoding", "row-decoding")]
    [Arguments("Provider-row model materialization", "row-materialization")]
    [Arguments("Provider-row decode/materialization pipeline", "row-materialization-pipeline")]
    [Arguments("Singular source argument validation", "singular-source-argument")]
    [Arguments("Singular source SQL preparation", "singular-source-sql")]
    [Arguments("Singular source result validation", "singular-source-result")]
    [Arguments("Known-miss materialization/publication", "known-miss-publication")]
    [Arguments("Composite key reconstruction baseline", "canonical-key-composite-baseline")]
    [Arguments("Scalar canonical-key propagation", "canonical-key-scalar")]
    [Arguments("Composite canonical-key propagation", "canonical-key-composite")]
    [Arguments("Typed-ID canonical-key propagation", "canonical-key-typed-id")]
    [Arguments("Converter-backed canonical-key propagation", "canonical-key-converter")]
    [Arguments("Binary canonical-key propagation", "canonical-key-binary")]
    [Arguments("Source batch slice creation", "source-batch-slice")]
    [Arguments("Source request construction", "source-request")]
    [Arguments("Source loader result construction", "source-loader-result")]
    [Arguments("Source result validation", "source-result-validation")]
    [Arguments("Source cache result publication", "source-cache-publication")]
    [Arguments("Mutation state-change capture", "mutation-capture")]
    [Arguments("Mutation execution preflight", "mutation-preflight")]
    [Arguments("Mutation command preparation", "mutation-command")]
    [Arguments("Mutation final drift validation", "mutation-final-drift")]
    [Arguments("Cold typed-ID exact terminal", "typed-id-exact-cold")]
    [Arguments("Warm typed-ID exact terminal", "typed-id-exact-warm")]
    public async Task AllocationStages_HaveStableTrackingAndScenarioCategories(
        string method,
        string expectedCategory)
    {
        await Assert.That(BenchmarkHarnessRunner.GetTrackingGroup(method))
            .IsEqualTo(BenchmarkHarnessRunner.AllocationStagesCategory);
        await Assert.That(BenchmarkHarnessRunner.GetScenarioCategory(method))
            .IsEqualTo(expectedCategory);
    }

    [Test]
    public async Task AllocationRegression_OverridesExistingTrackingGroupsForOneComparableLane()
    {
        var trackingGroup = BenchmarkHarnessRunner.GetTrackingGroup(
            "Warm primary-key fetch",
            BenchmarkHarnessRunner.AllocationRegressionCategory);

        await Assert.That(trackingGroup).IsEqualTo(BenchmarkHarnessRunner.AllocationRegressionCategory);
    }

    [Test]
    public async Task RunExitDecision_AllowsValidHistoryWithoutComparison()
    {
        var history = new BenchmarkHistoryArtifact
        {
            OverallExitCode = 0,
            ValidForEvidence = true
        };

        await Assert.That(BenchmarkHarnessRunner.ShouldFailRun(
            history,
            comparison: null,
            releaseEvidenceIntent: true)).IsFalse();
        await Assert.That(BenchmarkHarnessRunner.ShouldFailRun(
            history with { ValidForEvidence = false },
            comparison: null,
            releaseEvidenceIntent: true)).IsTrue();
    }

    [Test]
    public async Task CategorySelection_RejectsCombinedV09Selectors()
    {
        InvalidOperationException? exception = null;

        try
        {
            _ = BenchmarkHarnessRunner.ResolveSelectedCategory(
                phase2Watch: false,
                phase3QueryHotPath: false,
                phase10KeyFoundation: false,
                phase11CacheInvalidation: false,
                phase12CacheMemory: false,
                v09QueryBackend: true,
                v09MemoryRead: true,
                allocationRegression: false,
                allocationStages: false);
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("--v09-query-backend")
            .And.Contains("--v09-memory-read");
    }

    [Test]
    [Arguments("Expression parse/structural template")]
    [Arguments("Expression parse/template/initial bind")]
    [Arguments("Template freeze/validation")]
    [Arguments("Invocation bind scalar/local sequence")]
    [Arguments("SQL request/capability preparation")]
    [Arguments("SQL adapter scalar Any")]
    public async Task TrackingGroup_MapsV09QueryBackendScenarios(string method)
    {
        var result = BenchmarkHarnessRunner.GetTrackingGroup(method);

        await Assert.That(result).IsEqualTo(BenchmarkHarnessRunner.V09QueryBackendCategory);
    }

    [Test]
    [Arguments("Expression parse/structural template", "query-planning")]
    [Arguments("Expression parse/template/initial bind", "query-planning")]
    [Arguments("Template freeze/validation", "query-planning")]
    [Arguments("Invocation bind scalar/local sequence", "query-binding")]
    [Arguments("SQL request/capability preparation", "sql-adapter")]
    [Arguments("SQL adapter scalar Any", "sql-adapter")]
    public async Task ScenarioCategory_MapsV09QueryBackendScenarios(string method, string expectedCategory)
    {
        var result = BenchmarkHarnessRunner.GetScenarioCategory(method);

        await Assert.That(result).IsEqualTo(expectedCategory);
    }

    [Test]
    [Arguments("Memory database construction")]
    [Arguments("Memory construct and seed")]
    [Arguments("Memory primary-key hit")]
    [Arguments("Memory primary-key miss")]
    [Arguments("Memory scalar scan")]
    [Arguments("Memory filter order page")]
    [Arguments("Memory repeated entity identity")]
    [Arguments("Memory direct-Guid equality count")]
    [Arguments("Memory typed-ID equality count")]
    public async Task TrackingGroup_MapsV09MemoryReadScenarios(string method)
    {
        var result = BenchmarkHarnessRunner.GetTrackingGroup(method);

        await Assert.That(result).IsEqualTo(BenchmarkHarnessRunner.V09MemoryReadCategory);
    }

    [Test]
    [Arguments("Memory database construction", "memory-startup")]
    [Arguments("Memory construct and seed", "memory-seed")]
    [Arguments("Memory primary-key hit", "memory-primary-key")]
    [Arguments("Memory primary-key miss", "memory-primary-key")]
    [Arguments("Memory scalar scan", "memory-query")]
    [Arguments("Memory filter order page", "memory-query")]
    [Arguments("Memory repeated entity identity", "memory-identity")]
    [Arguments("Memory direct-Guid equality count", "memory-conversion")]
    [Arguments("Memory typed-ID equality count", "memory-conversion")]
    public async Task ScenarioCategory_MapsV09MemoryReadScenarios(string method, string expectedCategory)
    {
        var result = BenchmarkHarnessRunner.GetScenarioCategory(method);

        await Assert.That(result).IsEqualTo(expectedCategory);
    }

    [Test]
    public async Task TelemetryDelta_LegacyJsonDefaultsAdditiveMetricsToZero()
    {
        const string legacyJson = """
            {
              "Method": "Legacy SQL benchmark",
              "ProviderName": "sqlite-file",
              "OperationsPerInvoke": 1000,
              "EntityQueriesPerOperation": 1
            }
            """;

        var artifact = JsonSerializer.Deserialize<BenchmarkTelemetryDeltaArtifact>(legacyJson)!;
        var memoryMetrics = new[]
        {
            artifact.MemoryDatabasesConstructedPerOperation,
            artifact.MemoryRowsSeededPerOperation,
            artifact.MemoryPrimaryKeyRequestsPerOperation,
            artifact.MemoryPrimaryKeyProbesPerOperation,
            artifact.MemoryScanRowsVisitedPerOperation,
            artifact.MemoryPredicateEvaluationsPerOperation,
            artifact.MemoryPredicateRejectionsPerOperation,
            artifact.MemoryCacheLookupsPerOperation,
            artifact.MemoryCacheHitsPerOperation,
            artifact.MemoryCacheMissesPerOperation,
            artifact.MemoryMaterializationsPerOperation,
            artifact.MemoryCacheInsertionsPerOperation
        };

        await Assert.That(artifact.ReaderExecutionsPerOperation).IsEqualTo(0d);
        await Assert.That(memoryMetrics.All(static value => value == 0d)).IsTrue();
    }
}
