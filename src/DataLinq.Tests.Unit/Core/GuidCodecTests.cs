using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;

namespace DataLinq.Tests.Unit.Core;

public sealed class GuidCodecTests
{
    private const string KnownText36 = "00112233-4455-6677-8899-aabbccddeeff";
    private const string KnownText32 = "00112233445566778899aabbccddeeff";
    private const string KnownLittleEndianHex = "33221100554477668899AABBCCDDEEFF";
    private const string KnownRfc4122Hex = "00112233445566778899AABBCCDDEEFF";
    private static readonly Guid KnownGuid = Guid.ParseExact(KnownText36, "D");

    [Test]
    public async Task GuidStorageFormat_PublicVocabulary_IsFrozen()
    {
        await Assert.That(Enum.GetNames<GuidStorageFormat>()).IsEquivalentTo(
        [
            nameof(GuidStorageFormat.NativeUuid),
            nameof(GuidStorageFormat.Text36),
            nameof(GuidStorageFormat.Text32),
            nameof(GuidStorageFormat.Binary16LittleEndian),
            nameof(GuidStorageFormat.Binary16Rfc4122)
        ]);
    }

    [Test]
    public async Task ToPhysicalValue_KnownVector_UsesFrozenRepresentations()
    {
        var native = GuidCodec.ToPhysicalValue(KnownGuid, GuidStorageFormat.NativeUuid);
        var text36 = GuidCodec.ToPhysicalValue(KnownGuid, GuidStorageFormat.Text36);
        var text32 = GuidCodec.ToPhysicalValue(KnownGuid, GuidStorageFormat.Text32);
        var littleEndian = (byte[])GuidCodec.ToPhysicalValue(
            KnownGuid,
            GuidStorageFormat.Binary16LittleEndian);
        var rfc4122 = (byte[])GuidCodec.ToPhysicalValue(
            KnownGuid,
            GuidStorageFormat.Binary16Rfc4122);

        await Assert.That(native).IsEqualTo(KnownText36);
        await Assert.That(text36).IsEqualTo(KnownText36);
        await Assert.That(text32).IsEqualTo(KnownText32);
        await Assert.That(Convert.ToHexString(littleEndian)).IsEqualTo(KnownLittleEndianHex);
        await Assert.That(Convert.ToHexString(rfc4122)).IsEqualTo(KnownRfc4122Hex);
    }

    [Test]
    public async Task EveryFormat_RoundTripsCanonicalValues()
    {
        Guid[] values =
        [
            Guid.Empty,
            KnownGuid,
            Guid.ParseExact("fedcba98-7654-3210-89ab-cdef01234567", "D")
        ];

        foreach (var format in Enum.GetValues<GuidStorageFormat>())
        {
            foreach (var value in values)
            {
                var physical = GuidCodec.ToPhysicalValue(value, format);
                var roundTrip = GuidCodec.FromPhysicalValue(physical, format);

                await Assert.That(roundTrip).IsEqualTo(value);
            }
        }
    }

    [Test]
    public async Task TextAndNativeReads_AcceptUppercaseButWritesRemainLowercase()
    {
        var uppercase36 = KnownText36.ToUpperInvariant();
        var uppercase32 = KnownText32.ToUpperInvariant();

        var nativeFromGuid = GuidCodec.FromPhysicalValue(
            KnownGuid,
            GuidStorageFormat.NativeUuid);
        var nativeFromText = GuidCodec.FromPhysicalValue(
            uppercase36,
            GuidStorageFormat.NativeUuid);
        var text36 = GuidCodec.FromPhysicalValue(
            uppercase36,
            GuidStorageFormat.Text36);
        var text32 = GuidCodec.FromPhysicalValue(
            uppercase32,
            GuidStorageFormat.Text32);

        await Assert.That(nativeFromGuid).IsEqualTo(KnownGuid);
        await Assert.That(nativeFromText).IsEqualTo(KnownGuid);
        await Assert.That(text36).IsEqualTo(KnownGuid);
        await Assert.That(text32).IsEqualTo(KnownGuid);
        await Assert.That(GuidCodec.ToPhysicalValue(text36, GuidStorageFormat.Text36))
            .IsEqualTo(KnownText36);
        await Assert.That(GuidCodec.ToPhysicalValue(text32, GuidStorageFormat.Text32))
            .IsEqualTo(KnownText32);
    }

    [Test]
    public async Task BinaryLayouts_AreNotInterchangeable()
    {
        var littleEndian = Convert.FromHexString(KnownLittleEndianHex);
        var rfc4122 = Convert.FromHexString(KnownRfc4122Hex);

        var littleAsRfc = GuidCodec.FromPhysicalValue(
            littleEndian,
            GuidStorageFormat.Binary16Rfc4122);
        var rfcAsLittle = GuidCodec.FromPhysicalValue(
            rfc4122,
            GuidStorageFormat.Binary16LittleEndian);

        await Assert.That(littleEndian.SequenceEqual(rfc4122)).IsFalse();
        await Assert.That(littleAsRfc).IsNotEqualTo(KnownGuid);
        await Assert.That(rfcAsLittle).IsNotEqualTo(KnownGuid);
    }

    [Test]
    public async Task FromPhysicalValue_RejectsMalformedTextWithoutCoercion()
    {
        string[] invalidText36 =
        [
            KnownText32,
            $" {KnownText36}",
            $"{{{KnownText36}}}",
            "not-a-guid"
        ];
        string[] invalidText32 =
        [
            KnownText36,
            $" {KnownText32}",
            $"{{{KnownText32}}}",
            "00112233445566778899aabbccddeezz"
        ];

        foreach (var value in invalidText36)
        {
            _ = Capture<FormatException>(() =>
                GuidCodec.FromPhysicalValue(value, GuidStorageFormat.Text36));
        }

        foreach (var value in invalidText32)
        {
            _ = Capture<FormatException>(() =>
                GuidCodec.FromPhysicalValue(value, GuidStorageFormat.Text32));
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task FromPhysicalValue_RejectsWrongBinaryLengthsAndPhysicalTypes()
    {
        foreach (var length in new[] { 0, 15, 17 })
        {
            var exception = Capture<FormatException>(() =>
                GuidCodec.FromPhysicalValue(
                    new byte[length],
                    GuidStorageFormat.Binary16LittleEndian));

            await Assert.That(exception.Message).Contains($"length {length}");
        }

        _ = Capture<InvalidCastException>(() =>
            GuidCodec.FromPhysicalValue(KnownGuid, GuidStorageFormat.Text36));
        _ = Capture<InvalidCastException>(() =>
            GuidCodec.FromPhysicalValue(KnownText36, GuidStorageFormat.Binary16Rfc4122));
        _ = Capture<InvalidCastException>(() =>
            GuidCodec.FromPhysicalValue(new byte[16], GuidStorageFormat.NativeUuid));
    }

    [Test]
    public async Task Codec_RejectsNullAndUndefinedFormats()
    {
        var undefined = (GuidStorageFormat)int.MaxValue;
        var nullException = Capture<ArgumentNullException>(() =>
            GuidCodec.FromPhysicalValue(null!, GuidStorageFormat.Text36));
        var encodeException = Capture<ArgumentOutOfRangeException>(() =>
            GuidCodec.ToPhysicalValue(KnownGuid, undefined));
        var decodeException = Capture<ArgumentOutOfRangeException>(() =>
            GuidCodec.FromPhysicalValue(KnownGuid, undefined));

        await Assert.That(nullException.ParamName).IsEqualTo("physicalValue");
        await Assert.That(encodeException.ParamName).IsEqualTo("format");
        await Assert.That(decodeException.ParamName).IsEqualTo("format");
    }

    [Test]
    public async Task BinaryEncoding_ReturnsIndependentlyOwnedArrays()
    {
        var first = (byte[])GuidCodec.ToPhysicalValue(
            KnownGuid,
            GuidStorageFormat.Binary16LittleEndian);
        var second = (byte[])GuidCodec.ToPhysicalValue(
            KnownGuid,
            GuidStorageFormat.Binary16LittleEndian);
        var expectedSecond = second.ToArray();

        first[0] ^= 0xFF;

        await Assert.That(first).IsNotSameReferenceAs(second);
        await Assert.That(second.SequenceEqual(expectedSecond)).IsTrue();
        await Assert.That(GuidCodec.FromPhysicalValue(
            second,
            GuidStorageFormat.Binary16LittleEndian)).IsEqualTo(KnownGuid);
    }

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }
}
