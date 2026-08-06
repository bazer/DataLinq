using System;
using System.Reflection;
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
            v09MemoryRead: false);

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
            v09MemoryRead: false);

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
                v09MemoryRead: false);
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
            v09MemoryRead: true);

        await Assert.That(result).IsEqualTo(BenchmarkHarnessRunner.V09MemoryReadCategory);
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
                v09MemoryRead: true);
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
    public async Task TelemetryDelta_LegacyJsonDefaultsMemoryMetricsToZero()
    {
        const string legacyJson = """
            {
              "Method": "Legacy SQL benchmark",
              "ProviderName": "sqlite-file",
              "OperationsPerInvoke": 1000,
              "EntityQueriesPerOperation": 1
            }
            """;

        var artifactType = typeof(BenchmarkHarnessRunner).GetNestedType(
            "BenchmarkTelemetryDeltaArtifact",
            BindingFlags.NonPublic)!;
        var artifact = JsonSerializer.Deserialize(legacyJson, artifactType)!;

        var memoryMetricNames = new[]
        {
            "MemoryDatabasesConstructedPerOperation",
            "MemoryRowsSeededPerOperation",
            "MemoryPrimaryKeyRequestsPerOperation",
            "MemoryPrimaryKeyProbesPerOperation",
            "MemoryScanRowsVisitedPerOperation",
            "MemoryPredicateEvaluationsPerOperation",
            "MemoryPredicateRejectionsPerOperation",
            "MemoryCacheLookupsPerOperation",
            "MemoryCacheHitsPerOperation",
            "MemoryCacheMissesPerOperation",
            "MemoryMaterializationsPerOperation",
            "MemoryCacheInsertionsPerOperation"
        };

        foreach (var metricName in memoryMetricNames)
        {
            var value = (double)artifactType.GetProperty(metricName)!.GetValue(artifact)!;
            await Assert.That(value).IsEqualTo(0d);
        }
    }
}
