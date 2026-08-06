using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class ApiPackageSetInspectorTests
{
    private const string BaselineVersion = "0.8.0";
    private const string RepositoryUrl = "https://github.com/bazer/DataLinq";
    private const string RepositoryCommit = "1a156819e1567a4db3b8bd43e4e09e8da1a5572c";

    private static readonly string[] BaselinePackageIds = PackageInspectionPolicy.PublicPackageIds
        .Where(static id => !id.Equals(PackageInspectionPolicy.MemoryPackageId, StringComparison.Ordinal))
        .ToArray();

    [Test]
    public async Task Inspector_RecordsExactPrimaryAssetsAndIgnoresSymbolPackages()
    {
        using var fixture = new PackageFixture();
        fixture.WritePackages(BaselinePackageIds);
        File.WriteAllText(
            Path.Combine(fixture.PackageDirectory, "DataLinq.0.8.0.snupkg"),
            "deliberately not a ZIP archive",
            Encoding.UTF8);

        var first = ApiPackageSetInspector.Inspect(CreateOptions(fixture.PackageDirectory));
        var lockedHashes = first.Packages.ToDictionary(
            static package => package.Id,
            static package => package.Sha256,
            StringComparer.OrdinalIgnoreCase);
        var locked = ApiPackageSetInspector.Inspect(CreateOptions(
            fixture.PackageDirectory,
            lockedSha256ByPackageId: lockedHashes));

        await Assert.That(first.PackageDirectory).IsEqualTo(Path.GetFullPath(fixture.PackageDirectory));
        await Assert.That(first.Version).IsEqualTo(BaselineVersion);
        await Assert.That(first.RepositoryUrl).IsEqualTo(RepositoryUrl);
        await Assert.That(first.RepositoryCommit).IsEqualTo(RepositoryCommit);
        await Assert.That(first.Packages.Count).IsEqualTo(5);
        await Assert.That(string.Join(",", first.Packages.Select(static package => package.Id)))
            .IsEqualTo("DataLinq,DataLinq.CLI,DataLinq.MySql,DataLinq.SQLite,DataLinq.Tools");
        await Assert.That(first.Packages.All(static package => Path.IsPathFullyQualified(package.PackagePath)))
            .IsTrue();
        await Assert.That(first.Packages.All(static package =>
                package.SizeBytes > 0 &&
                package.Sha256.Length == 64 &&
                package.Sha256.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')))
            .IsTrue();
        await Assert.That(first.Packages.All(static package => package.PrimaryAssets.Count == 3)).IsTrue();
        await Assert.That(first.Packages.All(static package =>
                string.Join(",", package.PrimaryAssets.Select(static asset => asset.TargetFramework)) ==
                "net8.0,net9.0,net10.0"))
            .IsTrue();

        var cli = first.Packages.Single(static package => package.Id == "DataLinq.CLI");
        await Assert.That(cli.PrimaryAssets.All(static asset =>
                asset.ArchivePath == $"tools/{asset.TargetFramework}/any/DataLinq.CLI.dll"))
            .IsTrue();
        var core = first.Packages.Single(static package => package.Id == "DataLinq");
        await Assert.That(core.PrimaryAssets.All(static asset =>
                asset.ArchivePath == $"lib/{asset.TargetFramework}/DataLinq.dll"))
            .IsTrue();
        await Assert.That(locked).IsEquivalentTo(first);
    }

    [Test]
    public async Task Inspector_SelectsReferenceAssembliesBeforeRuntimeAssemblies()
    {
        using var fixture = new PackageFixture();
        fixture.WritePackage("DataLinq", includeReferenceAssets: true);

        var inspection = ApiPackageSetInspector.Inspect(CreateOptions(
            fixture.PackageDirectory,
            expectedPackageIds: ["DataLinq"]));

        var assets = inspection.Packages.Single().PrimaryAssets;
        await Assert.That(assets.Count).IsEqualTo(3);
        await Assert.That(assets.All(static asset =>
                asset.ArchivePath == $"ref/{asset.TargetFramework}/DataLinq.dll"))
            .IsTrue();
    }

    [Test]
    public async Task Inspector_FailsClosedWhenReferenceGroupsCannotYieldExactCompileAssets()
    {
        using var fixture = new PackageFixture();
        fixture.WritePackage(
            "DataLinq",
            additionalEntries:
            [
                "ref/net9.0/Other.dll",
                "ref/netstandard2.0/Other.dll"
            ]);

        var exception = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                fixture.PackageDirectory,
                expectedPackageIds: ["DataLinq"])));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("reference-asset group that takes NuGet compile precedence")
            .And.Contains("ref/net9.0/DataLinq.dll")
            .And.Contains("unsupported reference-asset group 'ref/netstandard2.0'");
    }

    [Test]
    public async Task Inspector_RejectsNonManagedAndWrongIdentityPrimaryAssemblies()
    {
        using var fixture = new PackageFixture();
        fixture.WritePackage(
            "DataLinq",
            invalidPrimaryAssets: ["lib/net8.0/DataLinq.dll"],
            primaryAssemblyIdsByPath: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["lib/net9.0/DataLinq.dll"] = "DataLinq.SQLite"
            });

        var exception = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                fixture.PackageDirectory,
                expectedPackageIds: ["DataLinq"])));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("lib/net8.0/DataLinq.dll' is not a valid managed PE assembly")
            .And.Contains("lib/net9.0/DataLinq.dll' has assembly identity 'DataLinq.SQLite'")
            .And.Contains("expected exact simple name 'DataLinq'");
    }

    [Test]
    public async Task Inspector_RejectsMissingDuplicateAndUnexpectedPackagesTogether()
    {
        using var fixture = new PackageFixture();
        fixture.WritePackages(BaselinePackageIds.Where(static id => id != "DataLinq.Tools"));
        fixture.WritePackage(
            "DataLinq.CLI",
            fileName: "DataLinq.CLI.copy.0.8.0.nupkg");
        var expectedPackageIds = BaselinePackageIds
            .Where(static id => id != "DataLinq.SQLite")
            .ToArray();

        var exception = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                fixture.PackageDirectory,
                expectedPackageIds: expectedPackageIds)));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("Missing required API package 'DataLinq.Tools'")
            .And.Contains("Duplicate API package 'DataLinq.CLI'")
            .And.Contains("Unexpected API package id 'DataLinq.SQLite'");
    }

    [Test]
    public async Task Inspector_RejectsFilenameNuspecIdentityPathAndVersionMismatch()
    {
        using var fixture = new PackageFixture();
        fixture.WritePackage(
            "DataLinq.Tools",
            version: "0.8.1",
            fileName: "DataLinq.0.8.0.nupkg",
            nuspecEntryPath: "metadata/DataLinq.Tools.nuspec");

        var exception = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                fixture.PackageDirectory,
                expectedPackageIds: ["DataLinq.Tools"])));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("does not match nuspec identity 'DataLinq.Tools' and version '0.8.1'")
            .And.Contains("expected exact path 'DataLinq.Tools.nuspec'")
            .And.Contains("expected exact version '0.8.0'");
    }

    [Test]
    public async Task Inspector_RejectsAbsentAndMixedRepositoryProvenance()
    {
        using var fixture = new PackageFixture();
        fixture.WritePackage("DataLinq");
        fixture.WritePackage(
            "DataLinq.SQLite",
            repositoryUrl: "https://example.invalid/DataLinq",
            repositoryCommit: "fedcba9876543210fedcba9876543210fedcba98");
        fixture.WritePackage(
            "DataLinq.MySql",
            repositoryUrl: null,
            repositoryCommit: null);

        var exception = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                fixture.PackageDirectory,
                expectedPackageIds: ["DataLinq", "DataLinq.SQLite", "DataLinq.MySql"],
                expectedRepositoryUrl: null,
                expectedRepositoryCommit: null)));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("DataLinq.MySql' is missing its nuspec repository URL")
            .And.Contains("DataLinq.MySql' is missing its nuspec repository commit")
            .And.Contains("2 different repository URLs")
            .And.Contains("2 different repository commits");
    }

    [Test]
    public async Task Inspector_AcceptsLegacySupportedNuspecNamespaceWhenUsedConsistently()
    {
        using var fixture = new PackageFixture();
        fixture.WritePackage(
            "DataLinq.CLI",
            nuspecNamespace: "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd");

        var inspection = ApiPackageSetInspector.Inspect(CreateOptions(
            fixture.PackageDirectory,
            expectedPackageIds: ["DataLinq.CLI"]));

        await Assert.That(inspection.Packages.Single().PrimaryAssets.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Inspector_RequiresSupportedNuspecNamespaceForEverySecurityRelevantElement()
    {
        using var unsupportedRoot = new PackageFixture();
        unsupportedRoot.WritePackage(
            "DataLinq",
            nuspecNamespace: "http://schemas.example.invalid/nuspec.xsd");

        var rootException = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                unsupportedRoot.PackageDirectory,
                expectedPackageIds: ["DataLinq"])));

        using var mixedId = new PackageFixture();
        mixedId.WritePackage(
            "DataLinq",
            idNamespace: "http://schemas.example.invalid/nuspec.xsd");

        var idException = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                mixedId.PackageDirectory,
                expectedPackageIds: ["DataLinq"])));

        await Assert.That(rootException).IsNotNull();
        await Assert.That(rootException!.Message).Contains("root element must be package in a supported namespace");
        await Assert.That(idException).IsNotNull();
        await Assert.That(idException!.Message).Contains("id element must use its package namespace");
    }

    [Test]
    public async Task Inspector_RequiresTextOnlyIdentityAndGitRepositoryMetadata()
    {
        using var nestedId = new PackageFixture();
        nestedId.WritePackage("DataLinq", idContainsElement: true);
        var idException = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                nestedId.PackageDirectory,
                expectedPackageIds: ["DataLinq"])));

        using var wrongRepositoryType = new PackageFixture();
        wrongRepositoryType.WritePackage("DataLinq", repositoryType: "svn");
        var typeException = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                wrongRepositoryType.PackageDirectory,
                expectedPackageIds: ["DataLinq"])));

        await Assert.That(idException).IsNotNull();
        await Assert.That(idException!.Message).Contains("id must be a text-only scalar element");
        await Assert.That(typeException).IsNotNull();
        await Assert.That(typeException!.Message).Contains("repository type must be exact value 'git'");
    }

    [Test]
    public async Task Inspector_UsesCanonicalCaseSensitiveRepositoryUris()
    {
        using var caseMismatch = new PackageFixture();
        caseMismatch.WritePackage(
            "DataLinq",
            repositoryUrl: "https://github.com/bazer/datalinq");
        var mismatchException = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                caseMismatch.PackageDirectory,
                expectedPackageIds: ["DataLinq"])));

        using var nonCanonical = new PackageFixture();
        nonCanonical.WritePackage(
            "DataLinq",
            repositoryUrl: "HTTPS://GITHUB.COM:443/bazer/DataLinq");
        var canonicalException = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                nonCanonical.PackageDirectory,
                expectedPackageIds: ["DataLinq"])));

        await Assert.That(mismatchException).IsNotNull();
        await Assert.That(mismatchException!.Message)
            .Contains("repository URL 'https://github.com/bazer/datalinq'")
            .And.Contains("expected 'https://github.com/bazer/DataLinq'");
        await Assert.That(canonicalException).IsNotNull();
        await Assert.That(canonicalException!.Message)
            .Contains("is not canonical")
            .And.Contains("https://github.com/bazer/DataLinq");
    }

    [Test]
    public async Task Inspector_RejectsLockedHashUrlAndCommitMismatch()
    {
        using var fixture = new PackageFixture();
        fixture.WritePackage("DataLinq");
        var lockedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DataLinq"] = new string('0', 64)
        };

        var exception = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                fixture.PackageDirectory,
                expectedPackageIds: ["DataLinq"],
                lockedSha256ByPackageId: lockedHashes,
                expectedRepositoryUrl: "https://example.invalid/DataLinq",
                expectedRepositoryCommit: "fedcba9876543210fedcba9876543210fedcba98")));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("expected locked SHA-256")
            .And.Contains("expected 'fedcba9876543210fedcba9876543210fedcba98'")
            .And.Contains("expected 'https://example.invalid/DataLinq'");
    }

    [Test]
    public async Task Inspector_RejectsInvalidAndDuplicateNormalizedZipPaths()
    {
        using var fixture = new PackageFixture();
        fixture.WritePackage(
            "DataLinq",
            additionalEntries: ["../escape.txt", "README.md", "README.md"]);

        var exception = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                fixture.PackageDirectory,
                expectedPackageIds: ["DataLinq"])));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("Archive entry path '../escape.txt' is invalid")
            .And.Contains("duplicate normalized ZIP path 'README.md'");
    }

    [Test]
    public async Task Inspector_RejectsPathAndEntryCountsBeyondInspectionLimits()
    {
        using var longPath = new PackageFixture();
        longPath.WritePackage(
            "DataLinq",
            additionalEntries: [$"content/{new string('a', 1024)}"]);
        var pathException = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                longPath.PackageDirectory,
                expectedPackageIds: ["DataLinq"])));

        using var tooManyEntries = new PackageFixture();
        tooManyEntries.WritePackage(
            "DataLinq",
            additionalEntries: Enumerable.Range(0, 4096)
                .Select(static index => $"content/{index}.txt")
                .ToArray());
        var entryException = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                tooManyEntries.PackageDirectory,
                expectedPackageIds: ["DataLinq"])));

        await Assert.That(pathException).IsNotNull();
        await Assert.That(pathException!.Message).Contains("path exceeds the 1024 character inspection limit");
        await Assert.That(entryException).IsNotNull();
        await Assert.That(entryException!.Message).Contains("the inspection limit is 4096");
    }

    [Test]
    public async Task Inspector_RequiresExactNonDuplicatedPrimaryFrameworkAssets()
    {
        using var fixture = new PackageFixture();
        fixture.WritePackage(
            "DataLinq",
            omittedPrimaryAssets: ["lib/net9.0/DataLinq.dll"],
            additionalEntries: ["lib/net7.0/DataLinq.dll"],
            duplicatedEntries: ["lib/net8.0/DataLinq.dll"]);

        var exception = Capture<InvalidDataException>(() =>
            ApiPackageSetInspector.Inspect(CreateOptions(
                fixture.PackageDirectory,
                expectedPackageIds: ["DataLinq"])));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("duplicate normalized ZIP path 'lib/net8.0/DataLinq.dll'")
            .And.Contains("2 copies of primary managed asset 'lib/net8.0/DataLinq.dll'")
            .And.Contains("missing primary managed asset 'lib/net9.0/DataLinq.dll'")
            .And.Contains("unexpected primary managed asset 'lib/net7.0/DataLinq.dll'");
    }

    [Test]
    public async Task Inspector_RejectsReparseTraversedPackageDirectory()
    {
        using var fixture = new PackageFixture();
        fixture.WritePackage("DataLinq");
        var linkRoot = Path.Combine(
            AppContext.BaseDirectory,
            nameof(ApiPackageSetInspectorTests),
            Guid.NewGuid().ToString("N"));
        var alias = Path.Combine(linkRoot, "package-alias");
        Directory.CreateDirectory(linkRoot);

        try
        {
            CreateDirectoryLink(alias, fixture.PackageDirectory);

            var exception = Capture<InvalidOperationException>(() =>
                ApiPackageSetInspector.Inspect(CreateOptions(alias, expectedPackageIds: ["DataLinq"])));

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
    public async Task TrackedBaselineLockContainsAuditedExactPackageSet()
    {
        var path = Path.Combine(
            RepositoryRootLocator.Find(),
            "test-infra",
            "api-compatibility",
            "v0.8.0-packages.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = document.RootElement;
        var packages = root.GetProperty("packages")
            .EnumerateArray()
            .ToDictionary(
                static package => package.GetProperty("id").GetString()!,
                static package => package.GetProperty("sha256").GetString()!,
                StringComparer.OrdinalIgnoreCase);

        await Assert.That(root.GetProperty("schemaVersion").GetString())
            .IsEqualTo("v0.9.api-package-baseline-lock.v1");
        await Assert.That(root.GetProperty("baselineVersion").GetString()).IsEqualTo(BaselineVersion);
        await Assert.That(root.GetProperty("repositoryUrl").GetString()).IsEqualTo(RepositoryUrl);
        await Assert.That(root.GetProperty("repositoryCommit").GetString()).IsEqualTo(RepositoryCommit);
        await Assert.That(root.GetProperty("repositoryTag").GetString()).IsEqualTo("0.8.0");
        await Assert.That(root.GetProperty("repositoryTagObjectType").GetString()).IsEqualTo("commit");
        await Assert.That(packages.Count).IsEqualTo(5);
        await Assert.That(packages["DataLinq"])
            .IsEqualTo("6af51acf9c45cbd0682ce91a660afe669e26ac383c889bb4375370e526f318d1");
        await Assert.That(packages["DataLinq.SQLite"])
            .IsEqualTo("9e07120795ca5385a74a9f9c69e7186036c103201f22c934157ff5fd1e639765");
        await Assert.That(packages["DataLinq.MySql"])
            .IsEqualTo("0f7ec8fb89fdc536d6f82bdc15cfeb77c63bb0ed93ef26b874e2d0544ede5422");
        await Assert.That(packages["DataLinq.CLI"])
            .IsEqualTo("f64d5a14c009435ee3c06c3530c7b37050d406df97900608415d31a7be523495");
        await Assert.That(packages["DataLinq.Tools"])
            .IsEqualTo("bcfbed905fbddb793fb9eaf4f9a6e601c1b0745644f6dfdd36cb97f03236bddf");
    }

    private static ApiPackageSetInspectionOptions CreateOptions(
        string packageDirectory,
        IReadOnlyCollection<string>? expectedPackageIds = null,
        IReadOnlyDictionary<string, string>? lockedSha256ByPackageId = null,
        string? expectedRepositoryCommit = RepositoryCommit,
        string? expectedRepositoryUrl = RepositoryUrl) =>
        new(
            packageDirectory,
            BaselineVersion,
            expectedPackageIds ?? BaselinePackageIds,
            lockedSha256ByPackageId,
            expectedRepositoryCommit,
            expectedRepositoryUrl);

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

    private sealed class PackageFixture : IDisposable
    {
        private const string SupportedNuspecNamespace = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

        private static readonly ConcurrentDictionary<string, byte[]> ManagedAssemblyPayloads =
            new(StringComparer.Ordinal);

        private readonly string root = Path.Combine(
            AppContext.BaseDirectory,
            nameof(ApiPackageSetInspectorTests),
            Guid.NewGuid().ToString("N"));

        public PackageFixture()
        {
            PackageDirectory = Path.Combine(root, "packages");
            Directory.CreateDirectory(PackageDirectory);
        }

        public string PackageDirectory { get; }

        public void WritePackages(IEnumerable<string> packageIds)
        {
            foreach (var packageId in packageIds)
                WritePackage(packageId);
        }

        public void WritePackage(
            string packageId,
            string version = BaselineVersion,
            string? fileName = null,
            string? nuspecEntryPath = null,
            string? repositoryUrl = RepositoryUrl,
            string? repositoryCommit = RepositoryCommit,
            IReadOnlyCollection<string>? omittedPrimaryAssets = null,
            IReadOnlyCollection<string>? additionalEntries = null,
            IReadOnlyCollection<string>? duplicatedEntries = null,
            bool includeReferenceAssets = false,
            IReadOnlyCollection<string>? invalidPrimaryAssets = null,
            IReadOnlyDictionary<string, string>? primaryAssemblyIdsByPath = null,
            string nuspecNamespace = SupportedNuspecNamespace,
            string? repositoryType = "git",
            string? idNamespace = null,
            bool idContainsElement = false)
        {
            var packagePath = Path.Combine(
                PackageDirectory,
                fileName ?? $"{packageId}.{version}.nupkg");
            File.Delete(packagePath);
            using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
            WriteEntry(
                archive,
                nuspecEntryPath ?? $"{packageId}.nuspec",
                CreateNuspec(
                    packageId,
                    version,
                    repositoryUrl,
                    repositoryCommit,
                    nuspecNamespace,
                    repositoryType,
                    idNamespace,
                    idContainsElement));

            var omitted = (omittedPrimaryAssets ?? [])
                .ToHashSet(StringComparer.Ordinal);
            var invalid = (invalidPrimaryAssets ?? [])
                .ToHashSet(StringComparer.Ordinal);
            foreach (var targetFramework in new[] { "net8.0", "net9.0", "net10.0" })
            {
                var primaryPath = PrimaryAssetPath(packageId, targetFramework);
                if (!omitted.Contains(primaryPath))
                {
                    WritePrimaryAsset(
                        archive,
                        primaryPath,
                        packageId,
                        invalid,
                        primaryAssemblyIdsByPath);
                }

                if (includeReferenceAssets && !packageId.Equals("DataLinq.CLI", StringComparison.OrdinalIgnoreCase))
                {
                    var referencePath = $"ref/{targetFramework}/{packageId}.dll";
                    if (!omitted.Contains(referencePath))
                    {
                        WritePrimaryAsset(
                            archive,
                            referencePath,
                            packageId,
                            invalid,
                            primaryAssemblyIdsByPath);
                    }
                }
            }

            foreach (var entry in additionalEntries ?? [])
                WriteEntry(archive, entry, $"additional-{entry}");
            foreach (var entry in duplicatedEntries ?? [])
                WriteEntry(archive, entry, $"duplicate-{entry}");
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        private static string PrimaryAssetPath(string packageId, string targetFramework) =>
            packageId.Equals("DataLinq.CLI", StringComparison.OrdinalIgnoreCase)
                ? $"tools/{targetFramework}/any/DataLinq.CLI.dll"
                : $"lib/{targetFramework}/{packageId}.dll";

        private static string CreateNuspec(
            string packageId,
            string version,
            string? repositoryUrl,
            string? repositoryCommit,
            string nuspecNamespace,
            string? repositoryType,
            string? idNamespace,
            bool idContainsElement)
        {
            XNamespace ns = nuspecNamespace;
            XNamespace idNs = idNamespace ?? nuspecNamespace;
            var id = idContainsElement
                ? new XElement(idNs + "id", new XElement(idNs + "part", packageId))
                : new XElement(idNs + "id", packageId);
            var metadata = new XElement(
                ns + "metadata",
                id,
                new XElement(ns + "version", version),
                new XElement(ns + "authors", "DataLinq"),
                new XElement(ns + "description", "API package fixture."));
            if (repositoryUrl is not null || repositoryCommit is not null)
            {
                var repository = new XElement(ns + "repository");
                if (repositoryType is not null)
                    repository.Add(new XAttribute("type", repositoryType));
                if (repositoryUrl is not null)
                    repository.Add(new XAttribute("url", repositoryUrl));
                if (repositoryCommit is not null)
                    repository.Add(new XAttribute("commit", repositoryCommit));
                metadata.Add(repository);
            }

            return new XDocument(new XElement(ns + "package", metadata)).ToString(SaveOptions.DisableFormatting);
        }

        private static void WritePrimaryAsset(
            ZipArchive archive,
            string path,
            string packageId,
            IReadOnlySet<string> invalidPrimaryAssets,
            IReadOnlyDictionary<string, string>? primaryAssemblyIdsByPath)
        {
            if (invalidPrimaryAssets.Contains(path))
            {
                WriteEntry(archive, path, $"not-managed-{packageId}");
                return;
            }

            var assemblyId = primaryAssemblyIdsByPath is not null &&
                             primaryAssemblyIdsByPath.TryGetValue(path, out var overrideId)
                ? overrideId
                : packageId;
            var payload = ManagedAssemblyPayloads.GetOrAdd(
                assemblyId,
                static id => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, $"{id}.dll")));
            WriteEntry(archive, path, payload);
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        private static void WriteEntry(ZipArchive archive, string path, byte[] content)
        {
            var entry = archive.CreateEntry(path);
            using var stream = entry.Open();
            stream.Write(content);
        }
    }
}
