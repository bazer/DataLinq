using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using DataLinq.DevTools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Tests.Unit;

public class PackageInspectorTests
{
    private const string CandidateVersion = "0.9.0-preview.package-policy.1";
    private const string RepositoryCommit = "0123456789abcdef0123456789abcdef01234567";
    private static readonly Lazy<byte[]> ValidMemoryAssembly = new(() => EmitManagedAssembly("DataLinq.Memory"));
    private static readonly Lazy<byte[]> WrongIdentityMemoryAssembly = new(() => EmitManagedAssembly("DataLinq.Memory.Renamed"));
    private static readonly Lazy<byte[]> BannedTokenMemoryAssembly = new(() => EmitManagedAssembly(
        "DataLinq.Memory",
        "internal static class PackageMarker { internal const string Payload = \"MySqlConnector\"; }"));

    [Test]
    public async Task Policy_ExposesExactReleasePackageAndMemoryDefaults()
    {
        await Assert.That(string.Join("|", PackageInspectionPolicy.PublicPackageIds)).IsEqualTo(
            "DataLinq|DataLinq.SQLite|DataLinq.MySql|DataLinq.Memory|DataLinq.CLI|DataLinq.Tools");
        await Assert.That(string.Join("|", PackageInspectionPolicy.RuntimePackageIds)).IsEqualTo(
            "DataLinq|DataLinq.SQLite|DataLinq.MySql|DataLinq.Memory");
        await Assert.That(string.Join("|", PackageInspectionPolicy.MemoryTargetFrameworks)).IsEqualTo(
            "net8.0|net9.0|net10.0");
        await Assert.That(PackageInspectionPolicy.CorePackageId).IsEqualTo("DataLinq");
        await Assert.That(PackageInspectionPolicy.MemoryPackageId).IsEqualTo("DataLinq.Memory");
        await Assert.That(PackageInspectionPolicy.MemoryDescription).IsEqualTo(
            "Experimental read-only in-memory backend for generated DataLinq models.");
        await Assert.That(PackageInspectionPolicy.RepositoryUrl).IsEqualTo("https://github.com/bazer/DataLinq");
        await Assert.That(PackageInspectionPolicy.LicenseFile).IsEqualTo("LICENSE.md");
        await Assert.That(PackageInspectionPolicy.ReadmeFile).IsEqualTo("README.md");
    }

    [Test]
    public async Task Inspector_AcceptsCleanAlignedCoreAndMemoryPackages()
    {
        using var fixture = new PackageFixture();
        fixture.Write(CreateCorePackage(), CreateMemoryPackage());

        var report = fixture.Inspect();
        var memory = report.Packages.Single(static package => package.Id == PackageInspectionPolicy.MemoryPackageId);

        await Assert.That(report.SchemaVersion).IsEqualTo("v0.9.package-inspection-report.v3");
        await Assert.That(report.Findings).IsEmpty();
        await Assert.That(report.Summary.HasHardFailures).IsFalse();
        await Assert.That(memory.Metadata.Description).IsEqualTo(PackageInspectionPolicy.MemoryDescription);
        await Assert.That(memory.Metadata.RepositoryCommit).IsEqualTo(RepositoryCommit);
        await Assert.That(memory.SymbolPackageId).IsEqualTo(PackageInspectionPolicy.MemoryPackageId);
        await Assert.That(memory.SymbolPackageVersion).IsEqualTo(CandidateVersion);
        await Assert.That(memory.PayloadTokenMatches).IsEmpty();
        await Assert.That(memory.ManagedAssemblies.Count).IsEqualTo(3);
        await Assert.That(memory.ManagedAssemblies.All(static assembly =>
            assembly.AssemblyName == PackageInspectionPolicy.MemoryPackageId && assembly.Error is null)).IsTrue();
    }

    [Test]
    public async Task Inspector_FlagsUnalignedPublicPackageVersions()
    {
        using var fixture = new PackageFixture();
        fixture.Write(
            CreateCorePackage(),
            CreateMemoryPackage("0.9.0-preview.package-policy.2"));

        var report = fixture.Inspect();

        await Assert.That(Findings(report, PackageInspectionFindingKind.PackageVersionMismatch).Count)
            .IsEqualTo(2);
        await Assert.That(report.Summary.HasHardFailures).IsTrue();
    }

    [Test]
    public async Task Inspector_FlagsIdentityMetadataAndRootAssetFailures()
    {
        using var fixture = new PackageFixture();
        var memory = CreateMemoryPackage() with
        {
            ArchiveId = "DataLinq.Memory.Candidate",
            Description = null,
            LicenseType = null,
            License = null,
            Readme = null,
            RepositoryType = null,
            RepositoryUrl = null,
            RepositoryCommit = null,
            PackageFiles = MemoryAssemblyFiles()
        };
        fixture.Write(CreateCorePackage(), memory);

        var report = fixture.Inspect();

        await Assert.That(Findings(report, PackageInspectionFindingKind.PackageIdentityMismatch).Count)
            .IsEqualTo(2);
        await Assert.That(Findings(report, PackageInspectionFindingKind.MissingPackageMetadata).Count)
            .IsGreaterThanOrEqualTo(6);
        await Assert.That(Findings(report, PackageInspectionFindingKind.MissingRequiredPackageAsset).Count)
            .IsEqualTo(2);
        await Assert.That(report.Summary.HasHardFailures).IsTrue();
    }

    [Test]
    public async Task Inspector_FlagsInvalidExactMetadataValues()
    {
        using var fixture = new PackageFixture();
        var memory = CreateMemoryPackage() with
        {
            Description = "A generic in-memory provider.",
            LicenseType = "expression",
            License = "MIT",
            Readme = "docs/README.md",
            RepositoryType = "svn",
            RepositoryUrl = "https://example.invalid/DataLinq"
        };
        fixture.Write(CreateCorePackage(), memory);

        var report = fixture.Inspect();

        await Assert.That(Findings(report, PackageInspectionFindingKind.InvalidPackageMetadata).Count)
            .IsEqualTo(6);
        await Assert.That(report.Summary.HasHardFailures).IsTrue();
    }

    [Test]
    public async Task Inspector_RequiresExactMemoryTargetFrameworkAssembliesAndSymbols()
    {
        using var fixture = new PackageFixture();
        var memory = CreateMemoryPackage() with
        {
            PackageFiles = Files(
                ("LICENSE.md", "license"),
                ("README.md", "readme"),
                ("lib/net8.0/DataLinq.Memory.dll", ""),
                ("lib/net10.0/DataLinq.Memory.dll", ""),
                ("lib/netstandard2.0/DataLinq.Memory.dll", "")),
            SymbolFiles =
            [
                "lib/net8.0/DataLinq.Memory.pdb",
                "lib/net10.0/DataLinq.Memory.pdb",
                "lib/netstandard2.0/DataLinq.Memory.pdb"
            ]
        };
        fixture.Write(CreateCorePackage(), memory);

        var report = fixture.Inspect();
        var missing = Findings(report, PackageInspectionFindingKind.MissingRequiredPackageAsset);
        var unexpectedRuntime = Findings(report, PackageInspectionFindingKind.UnexpectedPackageAsset);
        var unexpectedSymbols = Findings(report, PackageInspectionFindingKind.UnexpectedSymbolPackageAsset);

        await Assert.That(missing.Any(static finding =>
            finding.Message.Contains("runtime assembly", StringComparison.Ordinal) &&
            finding.Message.Contains("net9.0", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(missing.Any(static finding =>
            finding.Message.Contains("symbol", StringComparison.OrdinalIgnoreCase) &&
            finding.Message.Contains("net9.0", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(unexpectedRuntime.Any(static finding =>
            finding.Message.Contains("netstandard2.0/DataLinq.Memory.dll", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(unexpectedSymbols.Any(static finding =>
            finding.Message.Contains("netstandard2.0/DataLinq.Memory.pdb", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task Inspector_RequiresExactMemoryDependencyGroupsCoreVersionAndExclusion()
    {
        using var fixture = new PackageFixture();
        var memory = CreateMemoryPackage() with
        {
            DependencyGroups =
            [
                new DependencyGroupSpec(
                    "net8.0",
                    [new DependencySpec(PackageInspectionPolicy.CorePackageId, "0.9.0-preview.wrong", "all")]),
                new DependencyGroupSpec("net10.0", []),
                new DependencyGroupSpec(
                    "netstandard2.0",
                    [new DependencySpec(PackageInspectionPolicy.CorePackageId, CandidateVersion, "Build,Analyzers")])
            ]
        };
        fixture.Write(CreateCorePackage(), memory);

        var report = fixture.Inspect();

        await Assert.That(Findings(report, PackageInspectionFindingKind.MissingDependencyGroup).Single().TargetFramework)
            .IsEqualTo("net9.0");
        await Assert.That(Findings(report, PackageInspectionFindingKind.UnexpectedDependencyGroup).Single().TargetFramework)
            .IsEqualTo("netstandard2.0");
        await Assert.That(Findings(report, PackageInspectionFindingKind.MissingRequiredPackageDependency).Single().TargetFramework)
            .IsEqualTo("net10.0");
        await Assert.That(Findings(report, PackageInspectionFindingKind.PackageDependencyVersionMismatch).Single().TargetFramework)
            .IsEqualTo("net8.0");
        await Assert.That(Findings(report, PackageInspectionFindingKind.PackageDependencyExclusionMismatch).Single().TargetFramework)
            .IsEqualTo("net8.0");
    }

    [Test]
    public async Task Inspector_FlagsExtraProviderDependencyAsUnexpectedAndBanned()
    {
        using var fixture = new PackageFixture();
        var groups = CreateMemoryDependencyGroups()
            .Select(group => group.TargetFramework == "net9.0"
                ? group with
                {
                    Dependencies =
                    [
                        .. group.Dependencies,
                        new DependencySpec("Microsoft.Data.Sqlite", "10.0.9", "Build,Analyzers")
                    ]
                }
                : group)
            .ToArray();
        fixture.Write(CreateCorePackage(), CreateMemoryPackage() with { DependencyGroups = groups });

        var report = fixture.Inspect();

        await Assert.That(Findings(report, PackageInspectionFindingKind.UnexpectedPackageDependency).Single().TargetFramework)
            .IsEqualTo("net9.0");
        await Assert.That(Findings(report, PackageInspectionFindingKind.BannedRuntimeDependency).Single().TargetFramework)
            .IsEqualTo("net9.0");
        await Assert.That(report.Summary.HasHardFailures).IsTrue();
    }

    [Test]
    public async Task Inspector_FlagsBannedMemoryArchivePathAndManagedBinaryTokens()
    {
        using var fixture = new PackageFixture();
        var files = MemoryPackageFiles();
        files["content/DataLinq.SQLite.marker"] = [];
        files["lib/net9.0/DataLinq.Memory.dll"] = BannedTokenMemoryAssembly.Value;
        fixture.Write(CreateCorePackage(), CreateMemoryPackage() with { PackageFiles = files });

        var report = fixture.Inspect();
        var memory = report.Packages.Single(static package => package.Id == PackageInspectionPolicy.MemoryPackageId);

        await Assert.That(memory.PayloadTokenMatches.Select(static match => match.Token))
            .Contains("MySqlConnector");
        await Assert.That(memory.PayloadTokenMatches.Single(static match => match.Token == "MySqlConnector").Asset)
            .IsEqualTo("lib/net9.0/DataLinq.Memory.dll");
        await Assert.That(Findings(report, PackageInspectionFindingKind.BannedRuntimeAsset).Count)
            .IsEqualTo(2);
    }

    [Test]
    public async Task Inspector_RejectsMemoryAnalyzerRuntimeBuildToolAndNativePayload()
    {
        using var fixture = new PackageFixture();
        var files = MemoryPackageFiles();
        files["analyzers/dotnet/cs/AccidentalAnalyzer.dll"] = [];
        files["runtimes/win-x64/native/accidental.dll"] = [];
        files["build/DataLinq.Memory.targets"] = [];
        files["buildTransitive/DataLinq.Memory.targets"] = [];
        files["tools/net8.0/any/DataLinq.Memory.dll"] = [];
        files["native/accidental.so"] = [];
        fixture.Write(CreateCorePackage(), CreateMemoryPackage() with { PackageFiles = files });

        var report = fixture.Inspect();
        var findings = Findings(report, PackageInspectionFindingKind.UnexpectedPackageAsset);

        await Assert.That(findings.Count).IsEqualTo(6);
        await Assert.That(findings.Select(static finding => AssetFrom(finding.Message)))
            .IsEquivalentTo(
            [
                "analyzers/dotnet/cs/AccidentalAnalyzer.dll",
                "runtimes/win-x64/native/accidental.dll",
                "build/DataLinq.Memory.targets",
                "buildTransitive/DataLinq.Memory.targets",
                "tools/net8.0/any/DataLinq.Memory.dll",
                "native/accidental.so"
            ]);
        await Assert.That(report.Summary.HasHardFailures).IsTrue();
    }

    [Test]
    public async Task Inspector_RejectsNonPdbMemorySymbolPackageAsset()
    {
        using var fixture = new PackageFixture();
        var memory = CreateMemoryPackage();
        memory = memory with
        {
            SymbolFiles =
            [
                .. memory.SymbolFiles,
                "analyzers/dotnet/cs/AccidentalAnalyzer.dll"
            ]
        };
        fixture.Write(CreateCorePackage(), memory);

        var report = fixture.Inspect();
        var symbolPackage = report.SymbolPackages.Single(static package =>
            package.Id == PackageInspectionPolicy.MemoryPackageId);
        var finding = Findings(report, PackageInspectionFindingKind.UnexpectedSymbolPackageAsset).Single();

        await Assert.That(symbolPackage.AllFiles).Contains("analyzers/dotnet/cs/AccidentalAnalyzer.dll");
        await Assert.That(finding.Message).Contains("analyzers/dotnet/cs/AccidentalAnalyzer.dll");
        await Assert.That(Findings(report, PackageInspectionFindingKind.BannedSymbolPackageAsset)).IsEmpty();
        await Assert.That(report.Summary.HasHardFailures).IsTrue();
    }

    [Test]
    public async Task Inspector_RejectsArbitraryContentAndRenamedExecutablePayload()
    {
        using var fixture = new PackageFixture();
        var files = MemoryPackageFiles();
        files["contentFiles/any/any/settings.json"] = Bytes("{}");
        files["payload/renamed.data"] = Bytes("MZrenamed-executable");
        fixture.Write(CreateCorePackage(), CreateMemoryPackage() with { PackageFiles = files });

        var report = fixture.Inspect();
        var memory = report.Packages.Single(static package => package.Id == PackageInspectionPolicy.MemoryPackageId);
        var unexpected = Findings(report, PackageInspectionFindingKind.UnexpectedPackageAsset);
        var binaryMatch = memory.BinaryPayloadMatches.Single(static match => match.Asset == "payload/renamed.data");

        await Assert.That(unexpected.Select(static finding => AssetFrom(finding.Message)))
            .IsEquivalentTo(
            [
                "contentFiles/any/any/settings.json",
                "payload/renamed.data"
            ]);
        await Assert.That(binaryMatch.Signature).IsEqualTo("PE/MZ");
        await Assert.That(Findings(report, PackageInspectionFindingKind.BannedRuntimeAsset).Single().Message)
            .Contains("payload/renamed.data");
        await Assert.That(report.Summary.HasHardFailures).IsTrue();
    }

    [Test]
    public async Task Inspector_RejectsNativeMzPayloadAtExpectedMemoryAssemblyPath()
    {
        using var fixture = new PackageFixture();
        var files = MemoryPackageFiles();
        files["lib/net9.0/DataLinq.Memory.dll"] = Bytes("MZnative-payload-without-cli-metadata");
        fixture.Write(CreateCorePackage(), CreateMemoryPackage() with { PackageFiles = files });

        var report = fixture.Inspect();
        var memory = report.Packages.Single(static package => package.Id == PackageInspectionPolicy.MemoryPackageId);
        var inspection = memory.ManagedAssemblies.Single(static assembly =>
            assembly.Asset == "lib/net9.0/DataLinq.Memory.dll");
        var finding = Findings(report, PackageInspectionFindingKind.InvalidManagedAssembly).Single();

        await Assert.That(inspection.AssemblyName).IsNull();
        await Assert.That(inspection.Error).IsNotNull();
        await Assert.That(finding.Message).Contains("lib/net9.0/DataLinq.Memory.dll");
        await Assert.That(finding.Message).Contains("not a valid managed assembly");
        await Assert.That(report.Summary.HasHardFailures).IsTrue();
    }

    [Test]
    public async Task Inspector_RejectsWrongManagedAssemblyIdentityAtExpectedMemoryPath()
    {
        using var fixture = new PackageFixture();
        var files = MemoryPackageFiles();
        files["lib/net10.0/DataLinq.Memory.dll"] = WrongIdentityMemoryAssembly.Value;
        fixture.Write(CreateCorePackage(), CreateMemoryPackage() with { PackageFiles = files });

        var report = fixture.Inspect();
        var memory = report.Packages.Single(static package => package.Id == PackageInspectionPolicy.MemoryPackageId);
        var inspection = memory.ManagedAssemblies.Single(static assembly =>
            assembly.Asset == "lib/net10.0/DataLinq.Memory.dll");
        var finding = Findings(report, PackageInspectionFindingKind.InvalidManagedAssembly).Single();

        await Assert.That(inspection.AssemblyName).IsEqualTo("DataLinq.Memory.Renamed");
        await Assert.That(inspection.Error).IsNull();
        await Assert.That(finding.Message).Contains("DataLinq.Memory.Renamed");
        await Assert.That(finding.Message).Contains("expected exactly 'DataLinq.Memory'");
        await Assert.That(report.Summary.HasHardFailures).IsTrue();
    }

    [Test]
    public async Task Inspector_RejectsOrphanSymbolPackage()
    {
        using var fixture = new PackageFixture();
        fixture.Write(CreateCorePackage(), CreateMemoryPackage());
        fixture.WriteSymbolOnly(CreateIncompleteUnexpectedPackage("Orphan.Symbols"));

        var report = fixture.Inspect();
        var orphan = Findings(report, PackageInspectionFindingKind.OrphanSymbolPackage).Single();

        await Assert.That(report.SymbolPackages.Select(static package => package.Id)).Contains("Orphan.Symbols");
        await Assert.That(orphan.PackageId).IsEqualTo("Orphan.Symbols");
        await Assert.That(orphan.Message).Contains("no matching .nupkg");
        await Assert.That(report.Summary.HasHardFailures).IsTrue();
    }

    [Test]
    public async Task Inspector_AllowedUnexpectedPackageWithIncompleteMetadataIsNonHard()
    {
        using var fixture = new PackageFixture();
        fixture.Write(
            CreateCorePackage(),
            CreateMemoryPackage(),
            CreateIncompleteUnexpectedPackage("ThirdParty.Utility"));

        var report = fixture.Inspect(failOnUnexpectedPackage: false);
        var finding = report.Findings.Single();

        await Assert.That(finding.Kind).IsEqualTo(PackageInspectionFindingKind.UnexpectedPackage);
        await Assert.That(finding.PackageId).IsEqualTo("ThirdParty.Utility");
        await Assert.That(report.Summary.HardFailureCount).IsEqualTo(0);
        await Assert.That(report.Summary.HasHardFailures).IsFalse();
    }

    private static IReadOnlyList<PackageInspectionFinding> Findings(
        PackageInspectionReport report,
        PackageInspectionFindingKind kind) =>
        report.Findings.Where(finding => finding.Kind == kind).ToArray();

    private static string AssetFrom(string findingMessage)
    {
        var firstQuote = findingMessage.IndexOf('\'');
        var lastQuote = findingMessage.LastIndexOf('\'');
        return findingMessage[(firstQuote + 1)..lastQuote];
    }

    private static PackageDefinition CreateCorePackage(string version = CandidateVersion) =>
        new(
            PackageInspectionPolicy.CorePackageId,
            PackageInspectionPolicy.CorePackageId,
            version,
            "Core test package.",
            "file",
            PackageInspectionPolicy.LicenseFile,
            PackageInspectionPolicy.ReadmeFile,
            "git",
            PackageInspectionPolicy.RepositoryUrl,
            RepositoryCommit,
            [],
            Files(
                ("LICENSE.md", "license"),
                ("README.md", "readme"),
                ("lib/net8.0/DataLinq.dll", ""),
                ("lib/net9.0/DataLinq.dll", ""),
                ("lib/net10.0/DataLinq.dll", ""),
                ("analyzers/dotnet/cs/DataLinq.Generators.dll", "")),
            PackageInspectionPolicy.CorePackageId,
            version,
            [
                "lib/net8.0/DataLinq.pdb",
                "lib/net9.0/DataLinq.pdb",
                "lib/net10.0/DataLinq.pdb"
            ]);

    private static PackageDefinition CreateMemoryPackage(string version = CandidateVersion) =>
        new(
            PackageInspectionPolicy.MemoryPackageId,
            PackageInspectionPolicy.MemoryPackageId,
            version,
            PackageInspectionPolicy.MemoryDescription,
            "file",
            PackageInspectionPolicy.LicenseFile,
            PackageInspectionPolicy.ReadmeFile,
            "git",
            PackageInspectionPolicy.RepositoryUrl,
            RepositoryCommit,
            CreateMemoryDependencyGroups(version),
            MemoryPackageFiles(),
            PackageInspectionPolicy.MemoryPackageId,
            version,
            PackageInspectionPolicy.MemoryTargetFrameworks
                .Select(static framework => $"lib/{framework}/DataLinq.Memory.pdb")
                .ToArray());

    private static PackageDefinition CreateIncompleteUnexpectedPackage(string packageId) =>
        new(
            packageId,
            packageId,
            CandidateVersion,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            Files(),
            packageId,
            CandidateVersion,
            [$"lib/net8.0/{packageId}.pdb"]);

    private static IReadOnlyList<DependencyGroupSpec> CreateMemoryDependencyGroups(
        string version = CandidateVersion) =>
        PackageInspectionPolicy.MemoryTargetFrameworks
            .Select(framework => new DependencyGroupSpec(
                framework,
                [new DependencySpec(PackageInspectionPolicy.CorePackageId, version, "Build,Analyzers")]))
            .ToArray();

    private static Dictionary<string, byte[]> MemoryPackageFiles()
    {
        var files = Files(("LICENSE.md", "license"), ("README.md", "readme"));
        foreach (var pair in MemoryAssemblyFiles())
            files.Add(pair.Key, pair.Value);
        return files;
    }

    private static Dictionary<string, byte[]> MemoryAssemblyFiles() =>
        PackageInspectionPolicy.MemoryTargetFrameworks.ToDictionary(
            static framework => $"lib/{framework}/DataLinq.Memory.dll",
            static _ => ValidMemoryAssembly.Value,
            StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, byte[]> Files(params (string Path, string Content)[] files) =>
        files.ToDictionary(static file => file.Path, static file => Bytes(file.Content), StringComparer.OrdinalIgnoreCase);

    private static byte[] Bytes(string content) => Encoding.UTF8.GetBytes(content);

    private static byte[] EmitManagedAssembly(
        string assemblyName,
        string source = "internal static class PackageMarker { }")
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Could not emit test assembly '{assemblyName}': " +
                string.Join(Environment.NewLine, result.Diagnostics));
        }

        return stream.ToArray();
    }

    private sealed record DependencySpec(string Id, string Version, string? Exclude);

    private sealed record DependencyGroupSpec(
        string TargetFramework,
        IReadOnlyList<DependencySpec> Dependencies);

    private sealed record PackageDefinition(
        string PackageId,
        string ArchiveId,
        string Version,
        string? Description,
        string? LicenseType,
        string? License,
        string? Readme,
        string? RepositoryType,
        string? RepositoryUrl,
        string? RepositoryCommit,
        IReadOnlyList<DependencyGroupSpec> DependencyGroups,
        IReadOnlyDictionary<string, byte[]> PackageFiles,
        string? SymbolPackageId,
        string? SymbolPackageVersion,
        IReadOnlyList<string> SymbolFiles);

    private sealed class PackageFixture : IDisposable
    {
        private readonly string root = Path.Combine(
            AppContext.BaseDirectory,
            nameof(PackageInspectorTests),
            Guid.NewGuid().ToString("N"));

        public PackageFixture()
        {
            PackageDirectory = Path.Combine(root, "packages");
            Directory.CreateDirectory(PackageDirectory);
        }

        private string PackageDirectory { get; }

        public void Write(params PackageDefinition[] packages)
        {
            foreach (var package in packages)
            {
                WriteArchive(
                    Path.Combine(PackageDirectory, $"{package.ArchiveId}.{package.Version}.nupkg"),
                    $"{package.PackageId}.nuspec",
                    CreateNuspec(package),
                    package.PackageFiles);
                WriteSymbolOnly(package);
            }
        }

        public void WriteSymbolOnly(PackageDefinition package) =>
            WriteArchive(
                Path.Combine(PackageDirectory, $"{package.ArchiveId}.{package.Version}.snupkg"),
                $"{package.SymbolPackageId ?? "package"}.nuspec",
                CreateSymbolNuspec(package),
                package.SymbolFiles.ToDictionary(static file => file, static _ => Array.Empty<byte>(), StringComparer.OrdinalIgnoreCase));

        public PackageInspectionReport Inspect(bool failOnUnexpectedPackage = true)
        {
            var options = new PackageInspectionOptions(
                root,
                PackageDirectory,
                PackageSet(PackageInspectionPolicy.CorePackageId, PackageInspectionPolicy.MemoryPackageId),
                PackageSet(PackageInspectionPolicy.CorePackageId, PackageInspectionPolicy.MemoryPackageId),
                FailOnUnexpectedPackage: failOnUnexpectedPackage,
                FailOnMissingSymbolPackage: true,
                FailOnRuntimeRoslyn: true,
                FailOnRuntimeRemotion: true,
                FailOnAnalyzerAssetLeak: true);
            return new PackageInspector(DevToolPaths.Create(root), options).CreateReport();
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        private static IReadOnlySet<string> PackageSet(params string[] packageIds) =>
            packageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static XDocument CreateNuspec(PackageDefinition package)
        {
            XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
            var metadata = new XElement(ns + "metadata",
                OptionalElement(ns, "id", package.PackageId),
                OptionalElement(ns, "version", package.Version),
                new XElement(ns + "authors", "DataLinq"),
                OptionalLicense(ns, package.LicenseType, package.License),
                OptionalElement(ns, "readme", package.Readme),
                OptionalElement(ns, "description", package.Description),
                OptionalRepository(ns, package.RepositoryType, package.RepositoryUrl, package.RepositoryCommit),
                new XElement(
                    ns + "dependencies",
                    package.DependencyGroups.Select(group => new XElement(
                        ns + "group",
                        new XAttribute("targetFramework", group.TargetFramework),
                        group.Dependencies.Select(dependency => new XElement(
                            ns + "dependency",
                            new XAttribute("id", dependency.Id),
                            new XAttribute("version", dependency.Version),
                            dependency.Exclude is null ? null : new XAttribute("exclude", dependency.Exclude)))))));
            return new XDocument(new XElement(ns + "package", metadata));
        }

        private static XDocument CreateSymbolNuspec(PackageDefinition package)
        {
            XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
            return new XDocument(
                new XElement(
                    ns + "package",
                    new XElement(
                        ns + "metadata",
                        OptionalElement(ns, "id", package.SymbolPackageId),
                        OptionalElement(ns, "version", package.SymbolPackageVersion),
                        new XElement(ns + "authors", "DataLinq"),
                        new XElement(ns + "description", "Test symbol package."))));
        }

        private static XElement? OptionalElement(XNamespace ns, string name, string? value) =>
            value is null ? null : new XElement(ns + name, value);

        private static XElement? OptionalLicense(XNamespace ns, string? type, string? value)
        {
            if (type is null && value is null)
                return null;

            return new XElement(
                ns + "license",
                type is null ? null : new XAttribute("type", type),
                value);
        }

        private static XElement? OptionalRepository(
            XNamespace ns,
            string? type,
            string? url,
            string? commit)
        {
            if (type is null && url is null && commit is null)
                return null;

            return new XElement(
                ns + "repository",
                type is null ? null : new XAttribute("type", type),
                url is null ? null : new XAttribute("url", url),
                new XAttribute("branch", "refs/heads/v0.9"),
                commit is null ? null : new XAttribute("commit", commit));
        }

        private static void WriteArchive(
            string path,
            string nuspecEntryName,
            XDocument nuspec,
            IReadOnlyDictionary<string, byte[]> files)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            WriteZipEntry(archive, nuspecEntryName, nuspec.ToString(SaveOptions.DisableFormatting));
            foreach (var file in files)
                WriteZipEntry(archive, file.Key, file.Value);
        }

        private static void WriteZipEntry(ZipArchive archive, string entryName, string content)
            => WriteZipEntry(archive, entryName, Bytes(content));

        private static void WriteZipEntry(ZipArchive archive, string entryName, byte[] content)
        {
            var entry = archive.CreateEntry(entryName);
            using var stream = entry.Open();
            stream.Write(content);
        }
    }
}
