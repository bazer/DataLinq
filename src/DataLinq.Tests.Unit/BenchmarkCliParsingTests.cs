using System;
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
            v09QueryBackend: true);

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
            v09QueryBackend: false);

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
                v09QueryBackend: true);
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
}
