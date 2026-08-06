using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class ApiCompatibilityBaselineLockTests
{
    private static readonly string[] PackageIds =
    [
        "DataLinq",
        "DataLinq.SQLite",
        "DataLinq.MySql",
        "DataLinq.Tools",
        "DataLinq.CLI"
    ];

    [Test]
    public async Task Load_RequiresAndReturnsExactLockedIdentity()
    {
        using var fixture = new LockFixture();
        fixture.Write(ValidDocument());

        var baseline = ApiCompatibilityBaselineLock.Load(fixture.Path, "0.8.0", PackageIds);

        await Assert.That(baseline.SchemaVersion).IsEqualTo(ApiCompatibilityBaselineLock.SchemaVersion);
        await Assert.That(baseline.RepositoryCommit)
            .IsEqualTo("1a156819e1567a4db3b8bd43e4e09e8da1a5572c");
        await Assert.That(baseline.PackageSha256.Count).IsEqualTo(5);
        await Assert.That(baseline.PackageSha256["DataLinq"])
            .IsEqualTo(new string('a', 64));
        await Assert.That(baseline.LockPath).IsEqualTo(System.IO.Path.GetFullPath(fixture.Path));
        await Assert.That(baseline.LockSha256.Length).IsEqualTo(64);
        await Assert.That(baseline.CanonicalTrackedPolicy).IsFalse();
    }

    [Test]
    public async Task Load_RejectsUnknownFieldsAndUnexpectedPackageSet()
    {
        using var fixture = new LockFixture();
        fixture.Write(ValidDocument().Replace(
            "\"baselineVersion\": \"0.8.0\",",
            "\"baselineVersion\": \"0.8.0\", \"unknown\": true,",
            StringComparison.Ordinal));
        var unknown = Capture<InvalidDataException>(() =>
            ApiCompatibilityBaselineLock.Load(fixture.Path, "0.8.0", PackageIds));

        fixture.Write(ValidDocument().Replace("\"DataLinq.CLI\"", "\"Unexpected\"", StringComparison.Ordinal));
        var packageSet = Capture<InvalidDataException>(() =>
            ApiCompatibilityBaselineLock.Load(fixture.Path, "0.8.0", PackageIds));

        await Assert.That(unknown).IsNotNull();
        await Assert.That(unknown!.Message).Contains("unknown");
        await Assert.That(packageSet).IsNotNull();
        await Assert.That(packageSet!.Message)
            .Contains("missing 'DataLinq.CLI'")
            .And.Contains("unexpected package 'Unexpected'");
    }

    [Test]
    public async Task Load_RejectsVersionCommitAndHashDriftTogether()
    {
        using var fixture = new LockFixture();
        fixture.Write(ValidDocument()
            .Replace("\"baselineVersion\": \"0.8.0\"", "\"baselineVersion\": \"0.7.0\"", StringComparison.Ordinal)
            .Replace("1a156819e1567a4db3b8bd43e4e09e8da1a5572c", "short", StringComparison.Ordinal)
            .Replace(new string('a', 64), "not-a-hash", StringComparison.Ordinal));

        var exception = Capture<InvalidDataException>(() =>
            ApiCompatibilityBaselineLock.Load(fixture.Path, "0.8.0", PackageIds));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("baselineVersion")
            .And.Contains("repositoryCommit")
            .And.Contains("invalid SHA-256");
    }

    [Test]
    public async Task Load_RejectsDuplicatePropertiesAtEveryDepth()
    {
        using var fixture = new LockFixture();
        fixture.Write(ValidDocument().Replace(
            "\"repositoryCommit\": \"1a156819e1567a4db3b8bd43e4e09e8da1a5572c\",",
            "\"repositoryCommit\": \"1a156819e1567a4db3b8bd43e4e09e8da1a5572c\", " +
            "\"repositoryCommit\": \"ffffffffffffffffffffffffffffffffffffffff\",",
            StringComparison.Ordinal));
        var topLevel = Capture<InvalidDataException>(() =>
            ApiCompatibilityBaselineLock.Load(fixture.Path, "0.8.0", PackageIds));

        fixture.Write(ValidDocument().Replace(
            $"\"sha256\": \"{new string('a', 64)}\"",
            $"\"sha256\": \"{new string('a', 64)}\", \"sha256\": \"{new string('f', 64)}\"",
            StringComparison.Ordinal));
        var nested = Capture<InvalidDataException>(() =>
            ApiCompatibilityBaselineLock.Load(fixture.Path, "0.8.0", PackageIds));

        await Assert.That(topLevel).IsNotNull();
        await Assert.That(topLevel!.Message).Contains("duplicate JSON property 'repositoryCommit'");
        await Assert.That(nested).IsNotNull();
        await Assert.That(nested!.Message).Contains("duplicate JSON property 'sha256'");
    }

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

    private static string ValidDocument() =>
        $$"""
        {
          "schemaVersion": "v0.9.api-package-baseline-lock.v1",
          "baselineVersion": "0.8.0",
          "packageSource": "https://api.nuget.org/v3/index.json",
          "repositoryUrl": "https://github.com/bazer/DataLinq",
          "repositoryCommit": "1a156819e1567a4db3b8bd43e4e09e8da1a5572c",
          "repositoryTag": "0.8.0",
          "repositoryTagObjectType": "commit",
          "provenanceNote": "Independently acquired package bytes.",
          "packages": [
            { "id": "DataLinq", "sha256": "{{new string('a', 64)}}" },
            { "id": "DataLinq.SQLite", "sha256": "{{new string('b', 64)}}" },
            { "id": "DataLinq.MySql", "sha256": "{{new string('c', 64)}}" },
            { "id": "DataLinq.Tools", "sha256": "{{new string('d', 64)}}" },
            { "id": "DataLinq.CLI", "sha256": "{{new string('e', 64)}}" }
          ]
        }
        """;

    private sealed class LockFixture : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"datalinq-api-lock-{Guid.NewGuid():N}");

        public LockFixture()
        {
            Directory.CreateDirectory(root);
            Path = System.IO.Path.Combine(root, "baseline.json");
        }

        public string Path { get; }

        public void Write(string value) => File.WriteAllText(Path, value, new UTF8Encoding(false));

        public void Dispose()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
