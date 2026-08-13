using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Tests.Models.GeneratedDefaults;

namespace DataLinq.Tests.Unit.Core;

public class GeneratedDefaultValueFactoryTests
{
    [Test]
    public async Task CreateVersion7Guid_Rfc9562AppendixA6Vector_MatchesExactly()
    {
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1_645_557_742_000L);
        var entropy = Guid.Parse("ffffffff-ffff-fcc3-d8c4-dc0c0c07398f");

        var value = GeneratedDefaultValueFactory.CreateVersion7Guid(timestamp, entropy);

        await Assert.That(value).IsEqualTo(Guid.Parse("017f22e2-79b0-7cc3-98c4-dc0c0c07398f"));
    }

    [Test]
    public async Task CreateVersion7Guid_PreUnixTimestamp_Rejects()
    {
        ArgumentOutOfRangeException? exception = null;
        try
        {
            _ = GeneratedDefaultValueFactory.CreateVersion7Guid(
                DateTimeOffset.UnixEpoch.AddMilliseconds(-1),
                Guid.Empty);
        }
        catch (ArgumentOutOfRangeException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.ParamName).IsEqualTo("timestamp");
    }

    [Test]
    public async Task CreateVersion7Guid_PublicPath_EncodesCurrentTimestampVersionAndVariant()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var value = GeneratedDefaultValueFactory.CreateVersion7Guid();

        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var bytes = value.ToByteArray(bigEndian: true);
        var encodedTimestamp = ReadUnixTimeMilliseconds(bytes);

        await Assert.That(encodedTimestamp).IsGreaterThanOrEqualTo(before);
        await Assert.That(encodedTimestamp).IsLessThanOrEqualTo(after);
        await Assert.That(bytes[6] >> 4).IsEqualTo(7);
        await Assert.That(bytes[8] & 0xC0).IsEqualTo(0x80);
    }

    [Test]
    public async Task CreateVersion7Guid_ConcurrentCalls_ProduceDistinctValidValues()
    {
        var values = new Guid[4_096];

        Parallel.For(0, values.Length, index =>
            values[index] = GeneratedDefaultValueFactory.CreateVersion7Guid());

        await Assert.That(values.Distinct().Count()).IsEqualTo(values.Length);
        await Assert.That(values.All(IsVersion7WithRfcVariant)).IsTrue();
    }

    [Test]
    public async Task GeneratedClientDefaults_BothConstructionPaths_CreateVersionedDistinctValues()
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel<GeneratedDefaultDb>();
        await Assert.That(metadata.HasFailed).IsFalse();

        var parameterless = new MutableGeneratedDefaultRow { Name = "parameterless" };
        var required = new MutableGeneratedDefaultRow("required");

        await Assert.That(IsVersion4WithRfcVariant(parameterless.Version4Id)).IsTrue();
        await Assert.That(IsVersion7WithRfcVariant(parameterless.Version7Id)).IsTrue();
        await Assert.That(IsVersion4WithRfcVariant(required.Version4Id)).IsTrue();
        await Assert.That(IsVersion7WithRfcVariant(required.Version7Id)).IsTrue();
        await Assert.That(required.Name).IsEqualTo("required");
        await Assert.That(parameterless.Version4Id).IsNotEqualTo(required.Version4Id);
        await Assert.That(parameterless.Version7Id).IsNotEqualTo(required.Version7Id);
    }

    private static bool IsVersion4WithRfcVariant(Guid value)
    {
        var bytes = value.ToByteArray(bigEndian: true);
        return bytes[6] >> 4 == 4 && (bytes[8] & 0xC0) == 0x80;
    }

    private static bool IsVersion7WithRfcVariant(Guid value)
    {
        var bytes = value.ToByteArray(bigEndian: true);
        return bytes[6] >> 4 == 7 && (bytes[8] & 0xC0) == 0x80;
    }

    private static long ReadUnixTimeMilliseconds(byte[] bytes) =>
        ((long)bytes[0] << 40) |
        ((long)bytes[1] << 32) |
        ((long)bytes[2] << 24) |
        ((long)bytes[3] << 16) |
        ((long)bytes[4] << 8) |
        bytes[5];
}
