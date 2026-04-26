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
}
