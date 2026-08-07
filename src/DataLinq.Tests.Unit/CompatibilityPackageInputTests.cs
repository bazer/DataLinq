using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class CompatibilityPackageInputTests
{
    private const string CandidateVersion = "0.9.0-preview.package-input.1";
    private const string RepositoryCommit = "0123456789abcdef0123456789abcdef01234567";

    [Test]
    public async Task Inspector_ReturnsCanonicalSortedSixPackageIdentityAndIgnoresSymbols()
    {
        using var fixture = new CandidateFixture();
        fixture.WritePublicPackages();
        fixture.WritePackage("DataLinq.Memory.snupkg", PackageInspectionPolicy.MemoryPackageId, CandidateVersion, "symbols-a");

        var first = CompatibilityPackageInputInspector.Inspect(fixture.PackageDirectory, CandidateVersion);
        fixture.WritePackage("DataLinq.Memory.snupkg", PackageInspectionPolicy.MemoryPackageId, CandidateVersion, "symbols-b");
        var afterSymbolChange = CompatibilityPackageInputInspector.Inspect(fixture.PackageDirectory, CandidateVersion);

        var expectedIds = PackageInspectionPolicy.PublicPackageIds
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static id => id, StringComparer.Ordinal);
        await Assert.That(first.PackageDirectory).IsEqualTo(Path.GetFullPath(fixture.PackageDirectory));
        await Assert.That(first.Version).IsEqualTo(CandidateVersion);
        await Assert.That(string.Join(",", first.Packages.Select(static package => package.Id)))
            .IsEqualTo(string.Join(",", expectedIds));
        await Assert.That(first.Packages.Count).IsEqualTo(6);
        await Assert.That(first.Packages.All(static package => Path.IsPathFullyQualified(package.PackagePath))).IsTrue();
        await Assert.That(first.Packages.All(static package => package.SizeBytes > 0)).IsTrue();
        await Assert.That(first.Packages.All(static package =>
                package.Sha256.Length == 64 &&
                package.Sha256.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')))
            .IsTrue();
        await Assert.That(first.Packages.All(static package => package.RepositoryCommit == RepositoryCommit)).IsTrue();
        var memoryPackage = first.Packages.Single(static package => package.Id == PackageInspectionPolicy.MemoryPackageId);
        await Assert.That(memoryPackage.SizeBytes).IsEqualTo(new FileInfo(memoryPackage.PackagePath).Length);
        await Assert.That(memoryPackage.Sha256).IsEqualTo(ComputeSha256(memoryPackage.PackagePath));
        await Assert.That(first.AggregateIdentity.Length).IsEqualTo(64);
        await Assert.That(first.ScratchIdentity).IsEqualTo($"pkg-{first.AggregateIdentity[..16]}");
        await Assert.That(afterSymbolChange.AggregateIdentity).IsEqualTo(first.AggregateIdentity);
        await Assert.That(afterSymbolChange.Packages).IsEquivalentTo(first.Packages);
    }

    [Test]
    public async Task Inspector_RejectsWrongExactPackageVersion()
    {
        using var fixture = new CandidateFixture();
        fixture.WritePublicPackages();
        fixture.WritePackage("DataLinq.Memory.nupkg", PackageInspectionPolicy.MemoryPackageId, "0.9.0-preview.wrong");

        var exception = Capture<InvalidDataException>(() =>
            CompatibilityPackageInputInspector.Inspect(fixture.PackageDirectory, CandidateVersion));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("DataLinq.Memory")
            .And.Contains("expected exact version")
            .And.Contains(CandidateVersion);
    }

    [Test]
    public async Task Inspector_RejectsMissingRepositoryCommit()
    {
        using var fixture = new CandidateFixture();
        fixture.WritePublicPackages();
        fixture.WritePackage(
            "DataLinq.Memory.nupkg",
            PackageInspectionPolicy.MemoryPackageId,
            CandidateVersion,
            repositoryCommit: null);

        var exception = Capture<InvalidDataException>(() =>
            CompatibilityPackageInputInspector.Inspect(fixture.PackageDirectory, CandidateVersion));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("DataLinq.Memory")
            .And.Contains("missing its nuspec repository commit");
    }

    [Test]
    public async Task Inspector_RejectsMixedRepositoryCommits()
    {
        using var fixture = new CandidateFixture();
        fixture.WritePublicPackages();
        fixture.WritePackage(
            "DataLinq.Memory.nupkg",
            PackageInspectionPolicy.MemoryPackageId,
            CandidateVersion,
            repositoryCommit: "fedcba9876543210fedcba9876543210fedcba98");

        var exception = Capture<InvalidDataException>(() =>
            CompatibilityPackageInputInspector.Inspect(fixture.PackageDirectory, CandidateVersion));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("2 different repository commits")
            .And.Contains("one coherent candidate commit");
    }

    [Test]
    public async Task Inspector_RejectsMissingPublicPackage()
    {
        using var fixture = new CandidateFixture();
        fixture.WritePublicPackages(PackageInspectionPolicy.MemoryPackageId);

        var exception = Capture<InvalidDataException>(() =>
            CompatibilityPackageInputInspector.Inspect(fixture.PackageDirectory, CandidateVersion));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Missing required public package 'DataLinq.Memory'");
    }

    [Test]
    public async Task Inspector_RejectsDuplicatePublicPackageIdentity()
    {
        using var fixture = new CandidateFixture();
        fixture.WritePublicPackages();
        fixture.WritePackage("DataLinq.Memory.copy.nupkg", PackageInspectionPolicy.MemoryPackageId, CandidateVersion);

        var exception = Capture<InvalidDataException>(() =>
            CompatibilityPackageInputInspector.Inspect(fixture.PackageDirectory, CandidateVersion));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Duplicate public package 'DataLinq.Memory'");
    }

    [Test]
    public async Task Inspector_RejectsUnexpectedRuntimePackageIdentity()
    {
        using var fixture = new CandidateFixture();
        fixture.WritePublicPackages();
        fixture.WritePackage("DataLinq.Experimental.nupkg", "DataLinq.Experimental", CandidateVersion);

        var exception = Capture<InvalidDataException>(() =>
            CompatibilityPackageInputInspector.Inspect(fixture.PackageDirectory, CandidateVersion));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Unexpected public package id 'DataLinq.Experimental'");
    }

    [Test]
    public async Task Inspector_RejectsMalformedNuspecVersion()
    {
        using var fixture = new CandidateFixture();
        fixture.WritePublicPackages();
        fixture.WritePackage("DataLinq.Memory.nupkg", PackageInspectionPolicy.MemoryPackageId, "not a version");

        var exception = Capture<InvalidDataException>(() =>
            CompatibilityPackageInputInspector.Inspect(fixture.PackageDirectory, CandidateVersion));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("DataLinq.Memory.nupkg")
            .And.Contains("version 'not a version' is malformed");
    }

    [Test]
    public async Task Inspector_RejectsMissingNuspecVersion()
    {
        using var fixture = new CandidateFixture();
        fixture.WritePublicPackages();
        fixture.WritePackage(
            "DataLinq.Memory.nupkg",
            PackageInspectionPolicy.MemoryPackageId,
            CandidateVersion,
            omitVersion: true);

        var exception = Capture<InvalidDataException>(() =>
            CompatibilityPackageInputInspector.Inspect(fixture.PackageDirectory, CandidateVersion));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Expected exactly one version element, found 0");
    }

    [Test]
    public async Task Inspector_RejectsMultipleNuspecs()
    {
        using var fixture = new CandidateFixture();
        fixture.WritePublicPackages();
        fixture.WritePackage(
            "DataLinq.Memory.nupkg",
            PackageInspectionPolicy.MemoryPackageId,
            CandidateVersion,
            extraNuspec: true);

        var exception = Capture<InvalidDataException>(() =>
            CompatibilityPackageInputInspector.Inspect(fixture.PackageDirectory, CandidateVersion));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Expected exactly one nuspec, found 2");
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("not-a-version")]
    [Arguments("0.9.0-")]
    public async Task Inspector_RejectsBlankOrMalformedRequestedVersion(string version)
    {
        var exception = Capture<ArgumentException>(() =>
            CompatibilityPackageInputInspector.Inspect("unused", version));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.ParamName).IsEqualTo(nameof(version));
    }

    [Test]
    public async Task Inspector_RejectsMissingPackageDirectoryBeforeInspection()
    {
        var missingDirectory = Path.Combine(
            AppContext.BaseDirectory,
            nameof(CompatibilityPackageInputTests),
            Guid.NewGuid().ToString("N"));

        var exception = Capture<DirectoryNotFoundException>(() =>
            CompatibilityPackageInputInspector.Inspect(missingDirectory, CandidateVersion));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains(Path.GetFullPath(missingDirectory));
    }

    [Test]
    public async Task Inspector_RejectsReparseTraversedPackageDirectory()
    {
        using var fixture = new CandidateFixture();
        fixture.WritePublicPackages();
        var linkRoot = Path.Combine(
            AppContext.BaseDirectory,
            nameof(CompatibilityPackageInputTests),
            Guid.NewGuid().ToString("N"));
        var alias = Path.Combine(linkRoot, "package-alias");
        Directory.CreateDirectory(linkRoot);

        try
        {
            CreateDirectoryLink(alias, fixture.PackageDirectory);

            var exception = Capture<InvalidOperationException>(() =>
                CompatibilityPackageInputInspector.Inspect(alias, CandidateVersion));

            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.Message).Contains("reparse point");
        }
        finally
        {
            if (Directory.Exists(alias) && (File.GetAttributes(alias) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(alias);
            if (Directory.Exists(linkRoot))
                Directory.Delete(linkRoot, recursive: true);
        }
    }

    [Test]
    public async Task AggregateIdentityChangesWithCanonicalDirectoryAndPackageBytes()
    {
        using var firstFixture = new CandidateFixture();
        using var secondFixture = new CandidateFixture();
        firstFixture.WritePublicPackages();
        firstFixture.CopyPackagesTo(secondFixture.PackageDirectory);

        var first = CompatibilityPackageInputInspector.Inspect(firstFixture.PackageDirectory, CandidateVersion);
        var copied = CompatibilityPackageInputInspector.Inspect(secondFixture.PackageDirectory, CandidateVersion);
        secondFixture.WritePackage("DataLinq.Tools.nupkg", "DataLinq.Tools", CandidateVersion, "changed-payload");
        var changedBytes = CompatibilityPackageInputInspector.Inspect(secondFixture.PackageDirectory, CandidateVersion);

        await Assert.That(string.Join(",", first.Packages.Select(static package => package.Sha256)))
            .IsEqualTo(string.Join(",", copied.Packages.Select(static package => package.Sha256)));
        await Assert.That(copied.AggregateIdentity).IsNotEqualTo(first.AggregateIdentity);
        await Assert.That(copied.ContentAggregateSha256).IsEqualTo(first.ContentAggregateSha256);
        await Assert.That(changedBytes.AggregateIdentity).IsNotEqualTo(copied.AggregateIdentity);
        await Assert.That(changedBytes.ContentAggregateSha256).IsNotEqualTo(copied.ContentAggregateSha256);
        await Assert.That(changedBytes.Packages.Single(static package => package.Id == "DataLinq.Tools").Sha256)
            .IsNotEqualTo(copied.Packages.Single(static package => package.Id == "DataLinq.Tools").Sha256);
    }

    [Test]
    public async Task FinalCandidateStabilityCheck_RejectsChangedPackageBytes()
    {
        using var fixture = new CandidateFixture();
        fixture.WritePublicPackages();
        var inspected = CompatibilityPackageInputInspector.Inspect(
            fixture.PackageDirectory,
            CandidateVersion);

        await Assert.That(CompatibilitySizeReporter.CandidateInputStillMatches(inspected)).IsTrue();

        fixture.WritePackage(
            "DataLinq.Tools.nupkg",
            "DataLinq.Tools",
            CandidateVersion,
            "changed-after-report-inspection");

        await Assert.That(CompatibilitySizeReporter.CandidateInputStillMatches(inspected)).IsFalse();
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

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start cmd.exe to create a test junction.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException(
                $"Could not create test junction (exit {process.ExitCode}): {standardOutput}{standardError}");
        }
    }

    private sealed class CandidateFixture : IDisposable
    {
        private readonly string root = Path.Combine(
            AppContext.BaseDirectory,
            nameof(CompatibilityPackageInputTests),
            Guid.NewGuid().ToString("N"));

        public CandidateFixture()
        {
            PackageDirectory = Path.Combine(root, "packages");
            Directory.CreateDirectory(PackageDirectory);
        }

        public string PackageDirectory { get; }

        public void WritePublicPackages(string? omittedId = null)
        {
            foreach (var id in PackageInspectionPolicy.PublicPackageIds.Where(id =>
                         !id.Equals(omittedId, StringComparison.OrdinalIgnoreCase)))
            {
                WritePackage($"{id}.nupkg", id, CandidateVersion);
            }
        }

        public void WritePackage(
            string fileName,
            string id,
            string version,
            string payload = "payload",
            bool extraNuspec = false,
            bool omitVersion = false,
            string? repositoryCommit = RepositoryCommit)
        {
            var path = Path.Combine(PackageDirectory, fileName);
            File.Delete(path);
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            WriteZipEntry(
                archive,
                $"{id}.nuspec",
                $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{{id}}</id>
                    {{(omitVersion ? "" : $"<version>{version}</version>")}}
                    <authors>DataLinq</authors>
                    <description>Compatibility candidate fixture.</description>
                    {{(repositoryCommit is null ? "" : $"<repository type=\"git\" url=\"https://github.com/bazer/DataLinq\" commit=\"{repositoryCommit}\" />")}}
                  </metadata>
                </package>
                """);
            if (extraNuspec)
                WriteZipEntry(archive, "duplicate.nuspec", "<package />");
            WriteZipEntry(archive, "payload.txt", payload);
        }

        public void CopyPackagesTo(string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var path in Directory.EnumerateFiles(PackageDirectory, "*", SearchOption.TopDirectoryOnly))
                File.Copy(path, Path.Combine(destination, Path.GetFileName(path)), overwrite: true);
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        private static void WriteZipEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }
}
