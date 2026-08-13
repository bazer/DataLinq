using System;
using System.Collections;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DataLinq.DevTools;

[assembly: TypeForwardedTo(typeof(ArrayList))]

namespace DataLinq.Tests.Unit;

public sealed class PublicApiSnapshotterTests
{
    private const string PackageAssetPath = "lib/net10.0/DataLinq.Tests.Unit.dll";

    [Test]
    public async Task SnapshotAssembly_IsDeterministicAndIncludesExactImageIdentity()
    {
        var assemblyPath = typeof(PublicApiSnapshotterTests).Assembly.Location;
        using var firstStream = File.OpenRead(assemblyPath);
        using var secondStream = File.OpenRead(assemblyPath);

        var first = PublicApiSnapshotter.SnapshotAssembly(firstStream, "unit-test-assembly");
        var second = PublicApiSnapshotter.SnapshotAssembly(secondStream, "unit-test-assembly");
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath)))
            .ToLowerInvariant();

        await Assert.That(first.AssemblyIdentity).IsEqualTo(second.AssemblyIdentity);
        await Assert.That(first.AssemblyIdentity).Contains("DataLinq.Tests.Unit, Version=");
        await Assert.That(first.ModuleVersionId).IsNotEqualTo(Guid.Empty);
        await Assert.That(first.ModuleVersionId).IsEqualTo(second.ModuleVersionId);
        await Assert.That(first.FileSha256).IsEqualTo(expectedSha256);
        await Assert.That(first.FileSha256).IsEqualTo(second.FileSha256);
        await Assert.That(first.ApiLines.SequenceEqual(second.ApiLines)).IsTrue();
        await Assert.That(first.SemanticApiText).IsEqualTo(second.SemanticApiText);
        await Assert.That(first.SemanticApiSha256).IsEqualTo(second.SemanticApiSha256);
        await Assert.That(first.SemanticApiSha256.Length).IsEqualTo(64);
        await Assert.That(first.SemanticApiSha256).IsEqualTo(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first.SemanticApiText)))
                .ToLowerInvariant());
        await Assert.That(first.SemanticApiText).DoesNotContain(first.FileSha256);
        await Assert.That(first.SemanticApiText).DoesNotContain(first.ModuleVersionId.ToString("D"));
        await Assert.That(first.CanonicalText).IsEqualTo(second.CanonicalText);
        await Assert.That(first.ApiLines.SequenceEqual(
                first.ApiLines.OrderBy(static line => line, StringComparer.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task SnapshotAssembly_DistinguishesInitOnlyAndMutablePropertySetters()
    {
        var fixtureLines = GetFixtureLines(SnapshotTestAssembly());
        var initOnly = fixtureLines.Single(line => line.Contains(" InitOnly ", StringComparison.Ordinal));
        var mutable = fixtureLines.Single(line => line.Contains(" Mutable ", StringComparison.Ordinal));

        await Assert.That(initOnly).Contains("init:public");
        await Assert.That(initOnly).DoesNotContain("set:public");
        await Assert.That(mutable).Contains("set:public");
        await Assert.That(mutable).DoesNotContain("init:public");
    }

    [Test]
    public async Task SnapshotAssembly_PreservesTypeReferenceScopeAndRawKind()
    {
        var fixtureLines = GetFixtureLines(SnapshotTestAssembly());
        var referenceType = fixtureLines.Single(line => line.Contains(" EchoVersion(", StringComparison.Ordinal));
        var valueType = fixtureLines.Single(line => line.Contains(" EchoDateTime(", StringComparison.Ordinal));

        await Assert.That(referenceType).Contains("class [assembly-ref:System.Runtime, Version=");
        await Assert.That(referenceType).Contains("]System.Version");
        await Assert.That(valueType).Contains("valuetype [assembly-ref:System.Runtime, Version=");
        await Assert.That(valueType).Contains("]System.DateTime");
    }

    [Test]
    public async Task FormatGeneralArrayType_DistinguishesRankOneArrayFromSzArrayNotation()
    {
        var generalRankOne = PublicApiSnapshotter.FormatGeneralArrayType(
            "int32",
            new ArrayShape(1, ImmutableArray<int>.Empty, ImmutableArray<int>.Empty));
        var generalRankTwo = PublicApiSnapshotter.FormatGeneralArrayType(
            "int32",
            new ArrayShape(2, ImmutableArray<int>.Empty, ImmutableArray<int>.Empty));

        await Assert.That(generalRankOne).IsEqualTo("int32[*]");
        await Assert.That(generalRankOne).IsNotEqualTo("int32[]");
        await Assert.That(generalRankTwo).IsEqualTo("int32[,]");
    }

    [Test]
    public async Task SnapshotMetadataValidation_RejectsMalformedArrayAndGenericShapes()
    {
        var invalidArray = Capture<BadImageFormatException>(() =>
            PublicApiSnapshotter.FormatGeneralArrayType(
                "int32",
                new ArrayShape(1, ImmutableArray.Create(1, 2), ImmutableArray<int>.Empty)));
        var invalidReference = Capture<BadImageFormatException>(() =>
            PublicApiSnapshotter.GetGenericParameterName(["T"], 1, "type"));
        var invalidLayout = Capture<BadImageFormatException>(() =>
            PublicApiSnapshotter.ValidateGenericParameterLayout([0, 0], "fixture"));
        var invalidCount = Capture<BadImageFormatException>(() =>
            PublicApiSnapshotter.ValidateGenericParameterCount(2, 1, "fixture"));

        await Assert.That(invalidArray).IsNotNull();
        await Assert.That(invalidReference).IsNotNull();
        await Assert.That(invalidLayout).IsNotNull();
        await Assert.That(invalidCount).IsNotNull();
    }

    [Test]
    public async Task SnapshotAssembly_IncludesPublicTypeForwarderAndTargetScope()
    {
        var forwarder = SnapshotTestAssembly().ApiLines.Single(line =>
            line.StartsWith("type-forwarder public System.Collections.ArrayList -> ", StringComparison.Ordinal));

        await Assert.That(forwarder).Contains("assembly-ref:");
        await Assert.That(forwarder).Contains(", Version=");
        await Assert.That(forwarder).Contains(", PublicKeyToken=");
    }

    [Test]
    public async Task SnapshotAssembly_ExcludesPrivateInternalAndPrivateProtectedSurface()
    {
        var snapshot = SnapshotTestAssembly();
        var fixtureLines = GetFixtureLines(snapshot);

        await Assert.That(fixtureLines.Any(line => line.Contains("PublicMethod", StringComparison.Ordinal))).IsTrue();
        await Assert.That(fixtureLines.Any(line => line.Contains(" PrivateMethod(", StringComparison.Ordinal))).IsFalse();
        await Assert.That(fixtureLines.Any(line => line.Contains(" InternalMethod(", StringComparison.Ordinal))).IsFalse();
        await Assert.That(fixtureLines.Any(line => line.Contains(" PrivateProtectedMethod(", StringComparison.Ordinal))).IsFalse();
        await Assert.That(fixtureLines.Any(line => line.Contains("PrivateNested", StringComparison.Ordinal))).IsFalse();
        await Assert.That(fixtureLines.Any(line => line.Contains("InternalNested", StringComparison.Ordinal))).IsFalse();
        await Assert.That(fixtureLines.Any(line => line.Contains("PrivateProtectedNested", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task SnapshotAssembly_DistinguishesOverloadsAndPreservesParameterShape()
    {
        var snapshot = SnapshotTestAssembly();
        var echoLines = GetFixtureLines(snapshot)
            .Where(line => line.Contains(" Echo(", StringComparison.Ordinal))
            .ToArray();
        var transform = GetFixtureLines(snapshot)
            .Single(line => line.Contains(" Transform<", StringComparison.Ordinal));

        await Assert.That(echoLines.Length).IsEqualTo(2);
        await Assert.That(echoLines.Any(line => line.Contains("int32 value", StringComparison.Ordinal))).IsTrue();
        await Assert.That(echoLines.Any(line =>
                line.Contains("string value", StringComparison.Ordinal) &&
                line.Contains("optional", StringComparison.Ordinal) &&
                line.Contains("default=\"fallback\"", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(transform).Contains("in ").And.Contains(" input");
        await Assert.That(transform).Contains("out int32 count");
        await Assert.That(transform).Contains("ref string text");
        await Assert.That(transform).Contains("generic=[").And.Contains("new()");
    }

    [Test]
    public async Task SnapshotAssembly_RecordsEnumUnderlyingTypeAndExactValues()
    {
        var snapshot = SnapshotTestAssembly();
        var enumLines = snapshot.ApiLines
            .Where(line => line.Contains("SnapshotChoice", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(enumLines.Any(line =>
                line.StartsWith("type public ", StringComparison.Ordinal) &&
                line.Contains(" enum ", StringComparison.Ordinal) &&
                line.Contains("underlying=int16", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(enumLines.Any(line =>
                line.Contains(" Negative ", StringComparison.Ordinal) &&
                line.Contains("value=-2", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(enumLines.Any(line =>
                line.Contains(" Positive ", StringComparison.Ordinal) &&
                line.Contains("value=7", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task SnapshotAssembly_IncludesNestedProtectedAndProtectedInternalSurface()
    {
        var snapshot = SnapshotTestAssembly();
        var fixtureLines = GetFixtureLines(snapshot);

        await Assert.That(fixtureLines.Any(line =>
                line.StartsWith("type protected ", StringComparison.Ordinal) &&
                line.Contains("ProtectedNested", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(fixtureLines.Any(line =>
                line.Contains("method protected ", StringComparison.Ordinal) &&
                line.Contains("ProtectedMethod", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(fixtureLines.Any(line =>
                line.Contains("method protected internal ", StringComparison.Ordinal) &&
                line.Contains("ProtectedInternalMethod", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(fixtureLines.Any(line =>
                line.Contains("property public ", StringComparison.Ordinal) &&
                line.Contains("Name", StringComparison.Ordinal) &&
                line.Contains("get:public", StringComparison.Ordinal) &&
                line.Contains("set:protected", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(fixtureLines.Any(line =>
                line.Contains("event protected internal ", StringComparison.Ordinal) &&
                line.Contains("Changed", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task SnapshotAssembly_RetainsPublicCustomAttributeIdentityAndBlob()
    {
        var snapshot = SnapshotTestAssembly();
        var fixtureTypeLine = snapshot.ApiLines.Single(line =>
            line.StartsWith("type public ", StringComparison.Ordinal) &&
            line.Contains($"{FixtureTypeName} generic=[", StringComparison.Ordinal));

        await Assert.That(fixtureTypeLine).Contains("SnapshotMarkerAttribute::.ctor(string)");
        await Assert.That(fixtureTypeLine).Contains("=0x");
    }

    [Test]
    public async Task SnapshotPackageAsset_ReadsOneExactManagedEntry()
    {
        var assemblyPath = typeof(PublicApiSnapshotterTests).Assembly.Location;
        using var directStream = File.OpenRead(assemblyPath);
        var direct = PublicApiSnapshotter.SnapshotAssembly(directStream, PackageAssetPath);
        using var package = CreatePackage();
        var fromPackage = PublicApiSnapshotter.SnapshotPackageAsset(package, PackageAssetPath);

        await Assert.That(fromPackage.AssetName).IsEqualTo(PackageAssetPath);
        await Assert.That(fromPackage.AssemblyIdentity).IsEqualTo(direct.AssemblyIdentity);
        await Assert.That(fromPackage.ModuleVersionId).IsEqualTo(direct.ModuleVersionId);
        await Assert.That(fromPackage.FileSha256).IsEqualTo(direct.FileSha256);
        await Assert.That(fromPackage.ApiLines.SequenceEqual(direct.ApiLines)).IsTrue();
    }

    [Test]
    public async Task SnapshotPackageAsset_DoesNotCopyUnrelatedPayloadFromSeekablePackage()
    {
        const int fillerBytes = 8 * 1024 * 1024;
        var assemblyBytes = new FileInfo(typeof(PublicApiSnapshotterTests).Assembly.Location).Length;
        using var package = CreatePackage(fillerBytes);
        var readBudget = assemblyBytes + 1024 * 1024;
        await Assert.That(package.Length).IsGreaterThan(readBudget);
        using var guarded = new ReadBudgetStream(package, readBudget);

        var snapshot = PublicApiSnapshotter.SnapshotPackageAsset(guarded, PackageAssetPath);

        await Assert.That(snapshot.AssetName).IsEqualTo(PackageAssetPath);
        await Assert.That(guarded.BytesRead).IsLessThanOrEqualTo(readBudget);
    }

    [Test]
    public async Task SnapshotPackageAsset_ReadsBoundedNonSeekablePackageStream()
    {
        using var package = CreatePackage();
        using var nonSeekable = new NonSeekableReadStream(package);

        var snapshot = PublicApiSnapshotter.SnapshotPackageAsset(nonSeekable, PackageAssetPath);

        await Assert.That(snapshot.AssetName).IsEqualTo(PackageAssetPath);
        await Assert.That(nonSeekable.BytesRead).IsEqualTo(package.Length);
    }

    [Test]
    public async Task SnapshotPackageAsset_RejectsOversizedSeekablePackageBeforeReading()
    {
        using var oversized = new DeclaredLengthStream(PublicApiSnapshotter.MaximumPackageBytes + 1);

        var exception = Capture<InvalidDataException>(() =>
            PublicApiSnapshotter.SnapshotPackageAsset(oversized, PackageAssetPath));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("inspection limit");
        await Assert.That(oversized.ReadCalls).IsEqualTo(0);
    }

    [Test]
    public async Task SnapshotAssembly_RejectsOversizedNonSeekableInputAtBound()
    {
        using var oversized = new RepeatingNonSeekableStream(PublicApiSnapshotter.MaximumAssemblyBytes + 1L);

        var exception = Capture<InvalidDataException>(() =>
            PublicApiSnapshotter.SnapshotAssembly(oversized, "oversized.dll"));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("inspection limit");
        await Assert.That(oversized.BytesRead).IsEqualTo(PublicApiSnapshotter.MaximumAssemblyBytes + 1L);
    }

    [Test]
    public async Task SnapshotAssembly_RejectsMalformedImage()
    {
        using var malformed = new MemoryStream([0x4d, 0x5a, 0x00, 0x01, 0x02, 0x03]);

        var exception = Capture<InvalidDataException>(() =>
            PublicApiSnapshotter.SnapshotAssembly(malformed, "malformed.dll"));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("managed");
    }

    private static string FixtureTypeName => typeof(SnapshotFixture<>).FullName!;

    private static PublicApiSnapshot SnapshotTestAssembly()
    {
        using var stream = File.OpenRead(typeof(PublicApiSnapshotterTests).Assembly.Location);
        return PublicApiSnapshotter.SnapshotAssembly(stream, "unit-test-assembly");
    }

    private static MemoryStream CreatePackage(int fillerBytes = 0)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (fillerBytes > 0)
            {
                var filler = archive.CreateEntry("payload/unrelated.bin", CompressionLevel.NoCompression);
                using var destination = filler.Open();
                var buffer = new byte[8192];
                var remaining = fillerBytes;
                while (remaining > 0)
                {
                    var count = Math.Min(buffer.Length, remaining);
                    destination.Write(buffer, 0, count);
                    remaining -= count;
                }
            }

            var entry = archive.CreateEntry(PackageAssetPath, CompressionLevel.NoCompression);
            using var assemblyDestination = entry.Open();
            using var source = File.OpenRead(typeof(PublicApiSnapshotterTests).Assembly.Location);
            source.CopyTo(assemblyDestination);
        }

        package.Position = 0;
        return package;
    }

    private static string[] GetFixtureLines(PublicApiSnapshot snapshot) => snapshot.ApiLines
        .Where(line => line.Contains(FixtureTypeName, StringComparison.Ordinal))
        .ToArray();

    private static TException? Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return null;
        }
        catch (TException exception)
        {
            return exception;
        }
    }

    private sealed class ReadBudgetStream(Stream inner, long readBudget) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = inner.Read(buffer, offset, count);
            RecordRead(bytesRead);
            return bytesRead;
        }

        public override int Read(Span<byte> buffer)
        {
            var bytesRead = inner.Read(buffer);
            RecordRead(bytesRead);
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }

        private void RecordRead(int bytesRead)
        {
            BytesRead += bytesRead;
            if (BytesRead > readBudget)
                throw new IOException($"Read budget {readBudget} bytes was exceeded.");
        }
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = inner.Read(buffer, offset, count);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override int Read(Span<byte> buffer)
        {
            var bytesRead = inner.Read(buffer);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class DeclaredLengthStream(long length) : Stream
    {
        public int ReadCalls { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCalls++;
            throw new InvalidOperationException("Oversized stream must be rejected before reading.");
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RepeatingNonSeekableStream : Stream
    {
        private readonly long length;
        private long remaining;

        public RepeatingNonSeekableStream(long length)
        {
            this.length = length;
            remaining = length;
        }

        public long BytesRead => length - remaining;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (remaining == 0)
                return 0;
            var bytesRead = (int)Math.Min(count, remaining);
            Array.Clear(buffer, offset, bytesRead);
            remaining -= bytesRead;
            return bytesRead;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class SnapshotMarkerAttribute(string name) : Attribute
    {
        public string Name { get; } = name;

        public int Number { get; set; }
    }

    [SnapshotMarker("fixture", Number = 3)]
    public class SnapshotFixture<T>
        where T : class, new()
    {
        public const int Meaning = 42;

        public SnapshotFixture()
        {
        }

        protected SnapshotFixture(int seed)
        {
            _ = seed;
        }

        public string Name { get; protected set; } = string.Empty;

        public string InitOnly { get; init; } = string.Empty;

        public string Mutable { get; set; } = string.Empty;

        protected internal event EventHandler Changed
        {
            add { }
            remove { }
        }

        public virtual int Echo(int value) => value;

        public virtual string Echo(string value = "fallback") => value;

        public Version EchoVersion(Version value) => value;

        public DateTime EchoDateTime(DateTime value) => value;

        public TResult Transform<TResult>(in T input, out int count, ref string text)
            where TResult : class, new()
        {
            _ = input;
            count = text.Length;
            return new TResult();
        }

        public void PublicMethod()
        {
        }

        protected void ProtectedMethod()
        {
        }

        protected internal void ProtectedInternalMethod()
        {
        }

        private protected void PrivateProtectedMethod()
        {
        }

        internal void InternalMethod()
        {
        }

        private void PrivateMethod()
        {
        }

        public enum SnapshotChoice : short
        {
            Negative = -2,
            Positive = 7
        }

        protected class ProtectedNested
        {
            public void VisibleNestedMethod()
            {
            }
        }

        private protected class PrivateProtectedNested;

        internal class InternalNested;

        private class PrivateNested;
    }
}
