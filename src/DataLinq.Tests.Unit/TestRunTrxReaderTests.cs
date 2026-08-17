using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class TestRunTrxReaderTests
{
    [Test]
    public async Task Read_ProducesPercentilesSlowRowsAndEffectiveConcurrency()
    {
        var directory = CreateArtifactDirectory();
        try
        {
            var path = Path.Combine(directory, "report.trx");
            File.WriteAllText(path, TrxFixture);

            var performance = TestRunTrxReader.Read(
                path,
                testHostDurationSeconds: 5,
                configuredMaximumParallelTests: 8);

            await Assert.That(performance.Captured).IsTrue();
            await Assert.That(performance.CaptureError).IsNull();
            await Assert.That(performance.TestCount).IsEqualTo(4);
            await Assert.That(performance.TotalTestDurationSeconds).IsEqualTo(10);
            await Assert.That(performance.P50DurationSeconds).IsEqualTo(2);
            await Assert.That(performance.P95DurationSeconds).IsEqualTo(4);
            await Assert.That(performance.P99DurationSeconds).IsEqualTo(4);
            await Assert.That(performance.MaximumDurationSeconds).IsEqualTo(4);
            await Assert.That(performance.EffectiveConcurrency).IsEqualTo(2);
            await Assert.That(performance.ConfiguredMaximumParallelTests).IsEqualTo(8);
            await Assert.That(performance.ConfiguredParallelismSource).IsEqualTo("TUNIT_MAX_PARALLEL_TESTS");
            await Assert.That(performance.SlowestTests.Select(static test => test.Name))
                .IsEquivalentTo(["Fourth", "Third", "Second", "First"]);
            await Assert.That(performance.SlowestTests[0].ClassName).IsEqualTo("Example.AlphaTests");
            await Assert.That(performance.SlowestClasses[0].TotalDurationSeconds).IsEqualTo(5);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Read_MissingOrMalformedReportFailsClosed()
    {
        var directory = CreateArtifactDirectory();
        try
        {
            var missing = TestRunTrxReader.Read(Path.Combine(directory, "missing.trx"), 1, null);
            var malformedPath = Path.Combine(directory, "malformed.trx");
            File.WriteAllText(malformedPath, "<not-trx>");
            var malformed = TestRunTrxReader.Read(malformedPath, 1, null);

            await Assert.That(missing.Captured).IsFalse();
            await Assert.That(missing.CaptureError).Contains("not produced");
            await Assert.That(malformed.Captured).IsFalse();
            await Assert.That(malformed.CaptureError).Contains("could not be parsed");
            await Assert.That(malformed.SlowestTests).IsEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateArtifactDirectory()
    {
        var path = Path.Combine(
            RepositoryRootLocator.Find(),
            "artifacts",
            "test-results",
            $"trx-reader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private const string TrxFixture = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="1" testName="First" duration="00:00:01" outcome="Passed" />
            <UnitTestResult testId="2" testName="Second" duration="00:00:02" outcome="Passed" />
            <UnitTestResult testId="3" testName="Third" duration="00:00:03" outcome="Passed" />
            <UnitTestResult testId="4" testName="Fourth" duration="00:00:04" outcome="Failed" />
          </Results>
          <TestDefinitions>
            <UnitTest id="1"><TestMethod className="Example.AlphaTests" name="First" /></UnitTest>
            <UnitTest id="2"><TestMethod className="Example.BetaTests" name="Second" /></UnitTest>
            <UnitTest id="3"><TestMethod className="Example.BetaTests" name="Third" /></UnitTest>
            <UnitTest id="4"><TestMethod className="Example.AlphaTests" name="Fourth" /></UnitTest>
          </TestDefinitions>
        </TestRun>
        """;
}
