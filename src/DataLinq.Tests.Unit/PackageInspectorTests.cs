using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using DataLinq.Dev.CLI;
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
        await Assert.That(string.Join("|", PackageInspectionPolicy.PublicTargetFrameworks)).IsEqualTo(
            "net8.0|net9.0|net10.0");
        await Assert.That(string.Join("|", PackageInspectionPolicy.MemoryTargetFrameworks)).IsEqualTo(
            "net8.0|net9.0|net10.0");
        await Assert.That(PackageInspectionPolicy.CorePackageId).IsEqualTo("DataLinq");
        await Assert.That(PackageInspectionPolicy.SQLitePackageId).IsEqualTo("DataLinq.SQLite");
        await Assert.That(PackageInspectionPolicy.MySqlPackageId).IsEqualTo("DataLinq.MySql");
        await Assert.That(PackageInspectionPolicy.MemoryPackageId).IsEqualTo("DataLinq.Memory");
        await Assert.That(PackageInspectionPolicy.CliPackageId).IsEqualTo("DataLinq.CLI");
        await Assert.That(PackageInspectionPolicy.ToolsPackageId).IsEqualTo("DataLinq.Tools");
        await Assert.That(PackageInspectionPolicy.MemoryDescription).IsEqualTo(
            "Experimental read-only in-memory backend for generated DataLinq models.");
        await Assert.That(PackageInspectionPolicy.RepositoryUrl).IsEqualTo("https://github.com/bazer/DataLinq");
        await Assert.That(PackageInspectionPolicy.LicenseFile).IsEqualTo("LICENSE.md");
        await Assert.That(PackageInspectionPolicy.ReadmeFile).IsEqualTo("README.md");
    }

    [Test]
    public async Task PublishScript_UsesMemoryProjectAndMinVerVersionOverride()
    {
        var scriptPath = Path.Combine(RepositoryRootLocator.Find(), "publish-nuget.ps1");
        var script = File.ReadAllText(scriptPath);

        await Assert.That(script.Contains(
            "src\\DataLinq.Memory\\DataLinq.Memory.csproj",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(script.Contains(
            "-p:MinVerVersionOverride=$Version",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(script.Contains(
            "-p:PackageVersion=$Version",
            StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Inspector_AcceptsCleanAlignedCoreAndMemoryPackages()
    {
        using var fixture = new PackageFixture();
        fixture.Write(CreateCorePackage(), CreateMemoryPackage());

        var report = fixture.Inspect();
        var memory = report.Packages.Single(static package => package.Id == PackageInspectionPolicy.MemoryPackageId);

        await Assert.That(report.SchemaVersion).IsEqualTo(PackageInspector.SchemaVersion);
        await Assert.That(report.SchemaVersion).IsEqualTo("v0.9.package-inspection-report.v4");
        await Assert.That(report.Outcome).IsEqualTo(PackageInspectionOutcome.Passed);
        await Assert.That(report.InspectionComplete).IsTrue();
        await Assert.That(report.ArtifactsComplete).IsTrue();
        await Assert.That(report.IsCanonicalReleasePolicy).IsFalse();
        await Assert.That(report.ValidForEvidence).IsFalse();
        await Assert.That(File.Exists(report.Artifacts.JsonPath)).IsTrue();
        await Assert.That(File.Exists(report.Artifacts.MarkdownPath)).IsTrue();
        await Assert.That(report.Packages.All(static package => package.SizeBytes > 0 && package.Sha256.Length == 64)).IsTrue();
        await Assert.That(report.SymbolPackages.All(static package => package.SizeBytes > 0 && package.Sha256.Length == 64)).IsTrue();
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
            .IsEqualTo(8);
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
        var markdown = PackageInspector.ToMarkdown(report);
        await Assert.That(markdown).Contains("package archives inspected: <code>3</code>");
        await Assert.That(markdown).Contains("configured expected package ids: <code>2</code>");
    }

    [Test]
    public async Task Markdown_SanitizesNuspecControlledNewlinesAndBackticks()
    {
        using var fixture = new PackageFixture();
        fixture.Write(
            CreateCorePackage() with { Description = "safe\n## injected `code`" },
            CreateMemoryPackage());

        var markdown = PackageInspector.ToMarkdown(fixture.Inspect());

        await Assert.That(markdown.Contains($"{Environment.NewLine}## injected", StringComparison.Ordinal)).IsFalse();
        await Assert.That(markdown).Contains("safe ## injected &#96;code&#96;");
    }

    [Test]
    public async Task Inspector_CanonicalStrictCandidateProducesManifestReadyEvidence()
    {
        using var fixture = new PackageFixture(packageSourceUnderArtifacts: true);
        fixture.Write(CreateCanonicalPackages());

        var report = fixture.Inspect(
            expectedVersion: CandidateVersion,
            expectedPackageIds: PackageInspectionPolicy.PublicPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            runtimePackageIds: PackageInspectionPolicy.RuntimePackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            cleanRunner: true);

        await Assert.That(report.Outcome).IsEqualTo(PackageInspectionOutcome.Passed);
        await Assert.That(report.Findings).IsEmpty();
        await Assert.That(report.IsCanonicalReleasePolicy).IsTrue();
        await Assert.That(report.PackageDirectoryIsRepositoryArtifact).IsTrue();
        await Assert.That(report.Candidate.Version).IsEqualTo(CandidateVersion);
        await Assert.That(report.Candidate.VersionConsistent).IsTrue();
        await Assert.That(report.Candidate.RepositoryCommit).IsEqualTo(RepositoryCommit);
        await Assert.That(report.Candidate.RepositoryCommitConsistent).IsTrue();
        await Assert.That(report.Candidate.ArchivesStable).IsTrue();
        await Assert.That(report.Candidate.AggregateSha256.Length).IsEqualTo(64);
        await Assert.That(report.Runner.ValidForEvidence).IsTrue();
        await Assert.That(report.ValidForEvidence).IsTrue();
        await Assert.That(report.Packages.Count).IsEqualTo(6);
        await Assert.That(report.SymbolPackages.Count).IsEqualTo(6);

        using var json = JsonDocument.Parse(File.ReadAllText(report.Artifacts.JsonPath));
        await Assert.That(json.RootElement.GetProperty("SchemaVersion").GetString()).IsEqualTo(PackageInspector.SchemaVersion);
        await Assert.That(json.RootElement.GetProperty("Outcome").GetString()).IsEqualTo("Passed");
        await Assert.That(json.RootElement.GetProperty("ValidForEvidence").GetBoolean()).IsTrue();
        await Assert.That(json.RootElement.GetProperty("Invocation").GetProperty("ExpectedPackageIds").GetArrayLength()).IsEqualTo(6);
        await Assert.That(json.RootElement.GetProperty("Invocation").GetProperty("RuntimePackageIds").GetArrayLength()).IsEqualTo(4);
        await Assert.That(json.RootElement.GetProperty("Candidate").GetProperty("AggregateSha256").GetString()!.Length).IsEqualTo(64);
        await Assert.That(json.RootElement.GetProperty("Runner").GetProperty("ValidForEvidence").GetBoolean()).IsTrue();
        await Assert.That(json.RootElement.GetProperty("Artifacts").GetProperty("JsonPath").GetString()).IsEqualTo(report.Artifacts.JsonPath);
        await Assert.That(json.RootElement.GetProperty("Packages").EnumerateArray().All(static row =>
            row.GetProperty("SizeBytes").GetInt64() > 0 && row.GetProperty("Sha256").GetString()!.Length == 64)).IsTrue();
        await Assert.That(json.RootElement.GetProperty("SymbolPackages").EnumerateArray().All(static row =>
            row.GetProperty("SizeBytes").GetInt64() > 0 && row.GetProperty("Sha256").GetString()!.Length == 64)).IsTrue();
    }

    [Test]
    public async Task Inspector_AlignedButWrongRequestedVersionIsHardFailure()
    {
        using var fixture = new PackageFixture(packageSourceUnderArtifacts: true);
        fixture.Write(CreateCanonicalPackages());

        var report = fixture.Inspect(
            expectedVersion: "0.9.0-preview.wrong.1",
            expectedPackageIds: PackageInspectionPolicy.PublicPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            runtimePackageIds: PackageInspectionPolicy.RuntimePackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            cleanRunner: true);

        await Assert.That(report.Outcome).IsEqualTo(PackageInspectionOutcome.Failed);
        await Assert.That(report.Findings.Count(static finding =>
            finding.Kind == PackageInspectionFindingKind.PackageVersionMismatch && finding.IsHardFailure)).IsEqualTo(12);
        await Assert.That(report.Candidate.VersionConsistent).IsFalse();
        await Assert.That(report.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task Inspector_RelaxedStrictnessIsRecordedAndNeverQualifiesAsEvidence()
    {
        using var fixture = new PackageFixture(packageSourceUnderArtifacts: true);
        fixture.Write(CreateCanonicalPackages());
        var expected = PackageInspectionPolicy.PublicPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runtime = PackageInspectionPolicy.RuntimePackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var reports = new[]
        {
            fixture.Inspect(false, true, true, true, true, CandidateVersion, expected, runtime, cleanRunner: true),
            fixture.Inspect(true, false, true, true, true, CandidateVersion, expected, runtime, cleanRunner: true),
            fixture.Inspect(true, true, false, true, true, CandidateVersion, expected, runtime, cleanRunner: true),
            fixture.Inspect(true, true, true, false, true, CandidateVersion, expected, runtime, cleanRunner: true),
            fixture.Inspect(true, true, true, true, false, CandidateVersion, expected, runtime, cleanRunner: true)
        };

        await Assert.That(reports.All(static report => report.Outcome == PackageInspectionOutcome.Passed)).IsTrue();
        await Assert.That(reports.All(static report => report.Findings.Count == 0)).IsTrue();
        await Assert.That(reports.All(static report => !report.IsCanonicalReleasePolicy && !report.ValidForEvidence)).IsTrue();
        await Assert.That(reports[0].Invocation.FailOnUnexpectedPackage).IsFalse();
        await Assert.That(reports[1].Invocation.FailOnMissingSymbolPackage).IsFalse();
        await Assert.That(reports[2].Invocation.FailOnRuntimeRoslyn).IsFalse();
        await Assert.That(reports[3].Invocation.FailOnRuntimeRemotion).IsFalse();
        await Assert.That(reports[4].Invocation.FailOnAnalyzerAssetLeak).IsFalse();
    }

    [Test]
    public async Task Inspector_MissingVersionAndGhostRuntimePolicyCannotSpoofEvidence()
    {
        using var fixture = new PackageFixture(packageSourceUnderArtifacts: true);
        fixture.Write(CreateCanonicalPackages());
        var expected = PackageInspectionPolicy.PublicPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canonicalRuntime = PackageInspectionPolicy.RuntimePackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingVersion = fixture.Inspect(
            expectedPackageIds: expected,
            runtimePackageIds: canonicalRuntime,
            cleanRunner: true);
        var runtimeWithGhost = canonicalRuntime.ToHashSet(StringComparer.OrdinalIgnoreCase);
        runtimeWithGhost.Add("DataLinq.Ghost.Runtime");
        var ghostRuntime = fixture.Inspect(
            expectedVersion: CandidateVersion,
            expectedPackageIds: expected,
            runtimePackageIds: runtimeWithGhost,
            cleanRunner: true);

        await Assert.That(missingVersion.Outcome).IsEqualTo(PackageInspectionOutcome.Passed);
        await Assert.That(missingVersion.IsCanonicalReleasePolicy).IsFalse();
        await Assert.That(missingVersion.Candidate.VersionConsistent).IsFalse();
        await Assert.That(missingVersion.ValidForEvidence).IsFalse();
        await Assert.That(ghostRuntime.Outcome).IsEqualTo(PackageInspectionOutcome.Passed);
        await Assert.That(ghostRuntime.Invocation.RuntimePackageIds).Contains("DataLinq.Ghost.Runtime");
        await Assert.That(ghostRuntime.IsCanonicalReleasePolicy).IsFalse();
        await Assert.That(ghostRuntime.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task Inspector_MismatchedSymbolRepositoryCommitCannotBecomeEvidence()
    {
        using var fixture = new PackageFixture(packageSourceUnderArtifacts: true);
        fixture.Write(CreateCanonicalPackages());
        fixture.WriteSymbolOnly(CreateStandardPackage(PackageInspectionPolicy.ToolsPackageId) with
        {
            RepositoryCommit = new string('a', 40)
        });

        var report = fixture.Inspect(
            expectedVersion: CandidateVersion,
            expectedPackageIds: PackageInspectionPolicy.PublicPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            runtimePackageIds: PackageInspectionPolicy.RuntimePackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            cleanRunner: true);

        await Assert.That(report.Outcome).IsEqualTo(PackageInspectionOutcome.Passed);
        await Assert.That(report.Candidate.RepositoryCommitConsistent).IsFalse();
        await Assert.That(report.Runner.CandidateMatchesCheckout).IsFalse();
        await Assert.That(report.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task Inspector_HostileSymbolRepositoryIdentityIsHardFailure()
    {
        using var fixture = new PackageFixture(packageSourceUnderArtifacts: true);
        fixture.Write(CreateCanonicalPackages());
        fixture.WriteSymbolOnly(CreateStandardPackage(PackageInspectionPolicy.ToolsPackageId) with
        {
            RepositoryType = "svn",
            RepositoryUrl = "https://attacker.invalid/DataLinq"
        });

        var report = fixture.Inspect(
            expectedVersion: CandidateVersion,
            expectedPackageIds: PackageInspectionPolicy.PublicPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            runtimePackageIds: PackageInspectionPolicy.RuntimePackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            cleanRunner: true);

        await Assert.That(report.Outcome).IsEqualTo(PackageInspectionOutcome.Failed);
        await Assert.That(report.Findings.Count(static finding =>
            finding.Kind == PackageInspectionFindingKind.InvalidPackageMetadata &&
            finding.PackageId == PackageInspectionPolicy.ToolsPackageId &&
            finding.IsHardFailure)).IsEqualTo(2);
        await Assert.That(report.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task Inspector_InvalidRootAndNestedNuspecCannotProduceGreenEvidence()
    {
        using var invalidRootFixture = new PackageFixture(packageSourceUnderArtifacts: true);
        using var nestedPathFixture = new PackageFixture(packageSourceUnderArtifacts: true);
        invalidRootFixture.Write(CreateCanonicalPackages());
        nestedPathFixture.Write(CreateCanonicalPackages());
        invalidRootFixture.RewritePackageNuspec(
            CreateStandardPackage(PackageInspectionPolicy.ToolsPackageId),
            $"{PackageInspectionPolicy.ToolsPackageId}.nuspec",
            "not-package");
        nestedPathFixture.RewriteSymbolNuspec(
            CreateStandardPackage(PackageInspectionPolicy.ToolsPackageId),
            $"nested/{PackageInspectionPolicy.ToolsPackageId}.nuspec");
        var invalidRootOutput = Path.Combine(invalidRootFixture.RepositoryRoot, "artifacts", "invalid-root");
        var nestedPathOutput = Path.Combine(nestedPathFixture.RepositoryRoot, "artifacts", "nested-nuspec");

        Exception? invalidRootException = null;
        Exception? nestedPathException = null;
        try
        {
            _ = invalidRootFixture.Inspect(outputDirectory: invalidRootOutput);
        }
        catch (Exception caught)
        {
            invalidRootException = caught;
        }
        try
        {
            _ = nestedPathFixture.Inspect(outputDirectory: nestedPathOutput);
        }
        catch (Exception caught)
        {
            nestedPathException = caught;
        }

        await Assert.That(invalidRootException).IsTypeOf<InvalidDataException>();
        await Assert.That(nestedPathException).IsTypeOf<InvalidDataException>();
        using var invalidRootJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(invalidRootOutput, "report.json")));
        using var nestedPathJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(nestedPathOutput, "report.json")));
        await Assert.That(invalidRootJson.RootElement.GetProperty("Outcome").GetString()).IsEqualTo("Error");
        await Assert.That(nestedPathJson.RootElement.GetProperty("Outcome").GetString()).IsEqualTo("Error");
        await Assert.That(invalidRootJson.RootElement.GetProperty("ValidForEvidence").GetBoolean()).IsFalse();
        await Assert.That(nestedPathJson.RootElement.GetProperty("ValidForEvidence").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task Inspector_RejectsEmptyExpectedSetBeforeWriting()
    {
        using var fixture = new PackageFixture();
        var output = Path.Combine(fixture.RepositoryRoot, "artifacts", "empty-expected");
        ArgumentException? exception = null;
        try
        {
            var options = new PackageInspectionOptions(
                fixture.RepositoryRoot,
                fixture.PackageDirectory,
                PackageFixture.PackageSet(),
                PackageFixture.PackageSet(),
                true,
                true,
                true,
                true,
                true)
            {
                OutputDirectory = output
            };
            _ = new PackageInspector(DevToolPaths.Create(fixture.RepositoryRoot), options);
        }
        catch (ArgumentException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(Directory.Exists(output)).IsFalse();
    }

    [Test]
    public async Task Inspector_CandidateAggregateIsPathIndependentAndByteSensitive()
    {
        using var first = new PackageFixture(packageSourceUnderArtifacts: true);
        using var second = new PackageFixture(packageSourceUnderArtifacts: true);
        first.Write(CreateCorePackage(), CreateMemoryPackage());
        second.CopyArchivesFrom(first);
        var expected = PackageFixture.PackageSet(PackageInspectionPolicy.CorePackageId, PackageInspectionPolicy.MemoryPackageId);

        var firstReport = first.Inspect(
            expectedVersion: CandidateVersion,
            expectedPackageIds: expected,
            runtimePackageIds: expected,
            cleanRunner: true);
        var copiedReport = second.Inspect(
            expectedVersion: CandidateVersion,
            expectedPackageIds: expected,
            runtimePackageIds: expected,
            cleanRunner: true);
        var changedCore = CreateCorePackage() with
        {
            PackageFiles = Files(
                ("LICENSE.md", "license"),
                ("README.md", "readme"),
                ("lib/net8.0/DataLinq.dll", "changed bytes"),
                ("lib/net9.0/DataLinq.dll", ""),
                ("lib/net10.0/DataLinq.dll", ""),
                ("analyzers/dotnet/cs/DataLinq.Generators.dll", ""))
        };
        second.Write(changedCore);
        var changedReport = second.Inspect(
            expectedVersion: CandidateVersion,
            expectedPackageIds: expected,
            runtimePackageIds: expected,
            cleanRunner: true);

        await Assert.That(firstReport.Candidate.AggregateSha256).IsEqualTo(copiedReport.Candidate.AggregateSha256);
        await Assert.That(changedReport.Candidate.AggregateSha256).IsNotEqualTo(copiedReport.Candidate.AggregateSha256);
    }

    [Test]
    public async Task RunnerEvidence_FailsClosedForDirtyDriftStaleRunnerAndCandidateMismatch()
    {
        var clean = new TestRunSummaryRepositoryState(true, RepositoryCommit, "v0.9", false, new string('0', 64));
        var entry = new TestRunSummaryRunnerAssembly("DataLinq.Dev.CLI", CandidateVersion, RepositoryCommit, true, "clean");
        var devTools = new TestRunSummaryRunnerAssembly("DataLinq.DevTools", CandidateVersion, RepositoryCommit, true, "clean");

        var good = PackageInspector.EvaluateRunnerEvidence(clean, clean, entry, devTools, RepositoryCommit);
        var dirty = PackageInspector.EvaluateRunnerEvidence(clean with { Dirty = true }, clean, entry, devTools, RepositoryCommit);
        var drift = PackageInspector.EvaluateRunnerEvidence(clean, clean with { StatusSha256 = new string('1', 64) }, entry, devTools, RepositoryCommit);
        var stale = PackageInspector.EvaluateRunnerEvidence(clean, clean, entry with { RepositoryCommit = new string('a', 40) }, devTools, RepositoryCommit);
        var dirtyBuild = PackageInspector.EvaluateRunnerEvidence(clean, clean, entry with { RepositoryBuildState = "dirty" }, devTools, RepositoryCommit);
        var wrongCandidate = PackageInspector.EvaluateRunnerEvidence(clean, clean, entry, devTools, new string('b', 40));

        await Assert.That(good.ValidForEvidence).IsTrue();
        await Assert.That(dirty.ValidForEvidence).IsFalse();
        await Assert.That(drift.ValidForEvidence).IsFalse();
        await Assert.That(stale.ValidForEvidence).IsFalse();
        await Assert.That(dirtyBuild.ValidForEvidence).IsFalse();
        await Assert.That(wrongCandidate.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task Inspector_UsesDeterministicInjectedBranchCommitDirtyAndFailureStates()
    {
        using var fixture = new PackageFixture();
        fixture.Write(CreateCorePackage(), CreateMemoryPackage());
        var start = new TestRunSummaryRepositoryState(
            true,
            RepositoryCommit,
            "feature/hermetic-tests",
            false,
            new string('0', 64));
        var end = start with
        {
            Commit = "fedcba9876543210fedcba9876543210fedcba98",
            Branch = "feature/changed",
            Dirty = true,
            StatusSha256 = new string('1', 64)
        };
        var capture = new SequenceRepositoryStateCapture(start, end);

        var report = fixture.Inspect(repositoryStateCapture: capture);

        await Assert.That(report.Runner.Start).IsEqualTo(start);
        await Assert.That(report.Runner.End).IsEqualTo(end);
        await Assert.That(report.Runner.StateChangedDuringRun).IsTrue();
        await Assert.That(report.Runner.ValidForEvidence).IsFalse();
        await Assert.That(capture.CaptureCount).IsEqualTo(2);

        var expected = new IOException("deterministic repository capture failure");
        Exception? actual = null;
        try
        {
            _ = fixture.Inspect(repositoryStateCapture: new StubRepositoryStateCapture(_ => throw expected));
        }
        catch (Exception exception)
        {
            actual = exception;
        }

        await Assert.That(actual).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task Inspector_UnknownFindingKindsFailClosed()
    {
        using var fixture = new PackageFixture();
        var options = new PackageInspectionOptions(
            fixture.RepositoryRoot,
            fixture.PackageDirectory,
            PackageFixture.PackageSet(PackageInspectionPolicy.CorePackageId),
            PackageFixture.PackageSet(PackageInspectionPolicy.CorePackageId),
            true,
            true,
            true,
            true,
            true);
        var inspector = new PackageInspector(DevToolPaths.Create(fixture.RepositoryRoot), options);
        var unknown = new PackageInspectionFinding((PackageInspectionFindingKind)int.MaxValue, "DataLinq", null, "future finding");

        await Assert.That(inspector.IsHardFailure(unknown)).IsTrue();
    }

    [Test]
    public async Task ReportOutput_RejectsOutsideAndNonemptyDirectoriesWithoutDeletingSentinels()
    {
        using var fixture = new PackageFixture();
        fixture.Write(CreateCorePackage(), CreateMemoryPackage());
        var outside = Path.Combine(
            Path.GetDirectoryName(fixture.RepositoryRoot)!,
            $"outside-package-report-{Guid.NewGuid():N}");
        var inside = Path.Combine(fixture.RepositoryRoot, "artifacts", "nonempty-report");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(inside);
        var outsideSentinel = Path.Combine(outside, "report.json");
        var insideSentinel = Path.Combine(inside, "sentinel.txt");
        File.WriteAllText(outsideSentinel, "outside sentinel");
        File.WriteAllText(insideSentinel, "inside sentinel");

        Exception? outsideException = null;
        Exception? insideException = null;
        try
        {
            PackageInspector.InvalidateExistingReportDirectory(
                fixture.RepositoryRoot,
                fixture.PackageDirectory,
                outside);
        }
        catch (Exception caught)
        {
            outsideException = caught;
        }
        try
        {
            _ = fixture.Inspect(outputDirectory: inside);
        }
        catch (Exception caught)
        {
            insideException = caught;
        }

        await Assert.That(outsideException).IsTypeOf<InvalidDataException>();
        await Assert.That(insideException).IsTypeOf<InvalidDataException>();
        await Assert.That(File.ReadAllText(outsideSentinel)).IsEqualTo("outside sentinel");
        await Assert.That(File.ReadAllText(insideSentinel)).IsEqualTo("inside sentinel");
        Directory.Delete(outside, recursive: true);
    }

    [Test]
    public async Task ReportOutput_RejectsSourceOverlapWithoutMutatingCandidate()
    {
        using var fixture = new PackageFixture(packageSourceUnderArtifacts: true);
        fixture.Write(CreateCorePackage(), CreateMemoryPackage());
        var archiveNames = Directory.EnumerateFiles(fixture.PackageDirectory)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var overlappingOutput = Path.Combine(fixture.PackageDirectory, "report");
        Exception? exception = null;
        try
        {
            _ = fixture.Inspect(outputDirectory: overlappingOutput);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        var remainingNames = Directory.EnumerateFiles(fixture.PackageDirectory)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(exception).IsTypeOf<InvalidDataException>();
        await Assert.That(remainingNames).IsEquivalentTo(archiveNames);
        await Assert.That(Directory.Exists(overlappingOutput)).IsFalse();
    }

    [Test]
    public async Task ReportOutput_ReplacesKnownStaleArtifactsAndLeavesNoTemporaryFiles()
    {
        using var fixture = new PackageFixture();
        fixture.Write(CreateCorePackage(), CreateMemoryPackage());
        var output = Path.Combine(fixture.RepositoryRoot, "artifacts", "replace-report");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "report.json"), "stale json");
        File.WriteAllText(Path.Combine(output, "report.md"), "stale markdown");

        var report = fixture.Inspect(outputDirectory: output);

        await Assert.That(File.ReadAllText(report.Artifacts.JsonPath)).Contains(PackageInspector.SchemaVersion);
        await Assert.That(File.ReadAllText(report.Artifacts.JsonPath)).DoesNotContain("stale json");
        await Assert.That(File.ReadAllText(report.Artifacts.MarkdownPath)).DoesNotContain("stale markdown");
        await Assert.That(Directory.EnumerateFiles(output, "*.tmp").Any()).IsFalse();
    }

    [Test]
    public async Task ReportOutput_JsonCommitFailureLeavesNoCompletionMarkerOrTemporaryFiles()
    {
        using var fixture = new PackageFixture(packageSourceUnderArtifacts: true);
        fixture.Write(CreateCorePackage(), CreateMemoryPackage());
        var output = Path.Combine(fixture.RepositoryRoot, "artifacts", "blocked-json-commit");
        var expected = PackageFixture.PackageSet(PackageInspectionPolicy.CorePackageId, PackageInspectionPolicy.MemoryPackageId);
        var options = new PackageInspectionOptions(
            fixture.RepositoryRoot,
            fixture.PackageDirectory,
            expected,
            expected,
            true,
            true,
            true,
            true,
            true)
        {
            ExpectedVersion = CandidateVersion,
            OutputDirectory = output
        };
        var state = new TestRunSummaryRepositoryState(true, RepositoryCommit, "v0.9", false, new string('0', 64));
        var captureCount = 0;
        TestRunSummaryRepositoryState Capture(string _)
        {
            captureCount++;
            if (captureCount == 2)
                Directory.CreateDirectory(Path.Combine(output, "report.json"));
            return state;
        }
        var entry = new TestRunSummaryRunnerAssembly("DataLinq.Dev.CLI", CandidateVersion, RepositoryCommit, true, "clean");
        var devTools = new TestRunSummaryRunnerAssembly("DataLinq.DevTools", CandidateVersion, RepositoryCommit, true, "clean");
        var inspector = new PackageInspector(
            DevToolPaths.Create(fixture.RepositoryRoot),
            options,
            new StubRepositoryStateCapture(Capture),
            () => (entry, devTools));
        Exception? exception = null;
        try
        {
            _ = inspector.CreateReport();
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsTypeOf<AggregateException>();
        await Assert.That(File.Exists(Path.Combine(output, "report.json"))).IsFalse();
        await Assert.That(Directory.EnumerateFiles(output, "*.tmp").Any()).IsFalse();
    }

    [Test]
    public async Task Inspector_CorruptArchiveWritesBoundedErrorReportAndRethrows()
    {
        using var fixture = new PackageFixture();
        fixture.Write(CreateCorePackage(), CreateMemoryPackage());
        fixture.WriteCorruptArchive("Broken.1.0.0.nupkg");
        var output = Path.Combine(fixture.RepositoryRoot, "artifacts", "error-report");
        Exception? exception = null;
        try
        {
            _ = fixture.Inspect(outputDirectory: output);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        var reportPath = Path.Combine(output, "report.json");
        await Assert.That(exception).IsNotNull();
        await Assert.That(File.Exists(reportPath)).IsTrue();
        using var json = JsonDocument.Parse(File.ReadAllText(reportPath));
        await Assert.That(json.RootElement.GetProperty("SchemaVersion").GetString()).IsEqualTo(PackageInspector.SchemaVersion);
        await Assert.That(json.RootElement.GetProperty("Outcome").GetString()).IsEqualTo("Error");
        await Assert.That(json.RootElement.GetProperty("InspectionComplete").GetBoolean()).IsFalse();
        await Assert.That(json.RootElement.GetProperty("ArtifactsComplete").GetBoolean()).IsTrue();
        await Assert.That(json.RootElement.GetProperty("ValidForEvidence").GetBoolean()).IsFalse();
        await Assert.That(json.RootElement.GetProperty("Failure").GetProperty("Message").GetString()!.Length)
            .IsLessThanOrEqualTo(2048);
    }

    [Test]
    public async Task Inspector_DuplicateNormalizedArchivePathsCannotProduceGreenReport()
    {
        using var fixture = new PackageFixture();
        fixture.Write(CreateCorePackage(), CreateMemoryPackage());
        fixture.WriteDuplicatePathArchive();
        var output = Path.Combine(fixture.RepositoryRoot, "artifacts", "duplicate-path-report");
        Exception? exception = null;
        try
        {
            _ = fixture.Inspect(outputDirectory: output);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsTypeOf<InvalidDataException>();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "report.json")));
        await Assert.That(json.RootElement.GetProperty("Outcome").GetString()).IsEqualTo("Error");
        await Assert.That(json.RootElement.GetProperty("Failure").GetProperty("Message").GetString()!)
            .Contains("duplicate normalized archive path");
    }

    [Test]
    public async Task PackageReportCommand_ActionValidationInvalidatesStaleReportButParserFailureDoesNot()
    {
        using var fixture = new PackageFixture();
        fixture.Write(CreateCorePackage(), CreateMemoryPackage());
        var actionOutput = Path.Combine(fixture.RepositoryRoot, "artifacts", "invalid-action");
        var parserOutput = Path.Combine(fixture.RepositoryRoot, "artifacts", "invalid-parser");
        SeedStaleReport(actionOutput);
        SeedStaleReport(parserOutput);

        var settings = new DevCliSettings(fixture.RepositoryRoot, DevToolPaths.Create(fixture.RepositoryRoot));
        var actionRoot = CreatePackageReportRoot(settings);
        var actionExitCode = await actionRoot.Parse(
            [
                "package-report",
                "--package-dir", fixture.PackageDirectory,
                "--output", actionOutput,
                "--format", "invalid"
            ]).InvokeAsync();

        var parserRoot = CreatePackageReportRoot(settings);
        var parserExitCode = await parserRoot.Parse(
            [
                "package-report",
                "--package-dir", fixture.PackageDirectory,
                "--output", parserOutput,
                "--unknown-option"
            ]).InvokeAsync();

        await Assert.That(actionExitCode).IsNotEqualTo(0);
        await Assert.That(Directory.EnumerateFileSystemEntries(actionOutput).Any()).IsFalse();
        await Assert.That(parserExitCode).IsNotEqualTo(0);
        await Assert.That(File.ReadAllText(Path.Combine(parserOutput, "report.json"))).IsEqualTo("stale json");
        await Assert.That(File.ReadAllText(Path.Combine(parserOutput, "report.md"))).IsEqualTo("stale markdown");
    }

    [Test]
    public async Task PackageReportCommand_VersionOptsIntoStrictEvidenceExit()
    {
        using var fixture = new PackageFixture(packageSourceUnderArtifacts: true);
        fixture.Write(CreateCanonicalPackages());
        var settings = new DevCliSettings(fixture.RepositoryRoot, DevToolPaths.Create(fixture.RepositoryRoot));
        var diagnosticOutput = Path.Combine(fixture.RepositoryRoot, "artifacts", "cli-diagnostic");
        var evidenceOutput = Path.Combine(fixture.RepositoryRoot, "artifacts", "cli-evidence");

        var diagnosticExitCode = await CreatePackageReportRoot(settings).Parse(
            [
                "package-report",
                "--package-dir", fixture.PackageDirectory,
                "--output", diagnosticOutput,
                "--format", "summary"
            ]).InvokeAsync();
        var evidenceExitCode = await CreatePackageReportRoot(settings).Parse(
            [
                "package-report",
                "--package-dir", fixture.PackageDirectory,
                "--version", CandidateVersion,
                "--output", evidenceOutput,
                "--format", "summary"
            ]).InvokeAsync();

        await Assert.That(diagnosticExitCode).IsEqualTo(0);
        await Assert.That(evidenceExitCode).IsEqualTo(1);
        using var evidenceJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidenceOutput, "report.json")));
        await Assert.That(evidenceJson.RootElement.GetProperty("Outcome").GetString()).IsEqualTo("Passed");
        await Assert.That(evidenceJson.RootElement.GetProperty("IsCanonicalReleasePolicy").GetBoolean()).IsTrue();
        await Assert.That(evidenceJson.RootElement.GetProperty("ValidForEvidence").GetBoolean()).IsFalse();
    }

    private static IReadOnlyList<PackageInspectionFinding> Findings(
        PackageInspectionReport report,
        PackageInspectionFindingKind kind) =>
        report.Findings.Where(finding => finding.Kind == kind).ToArray();

    private static RootCommand CreatePackageReportRoot(DevCliSettings settings)
    {
        var root = new RootCommand();
        var repositoryState = new TestRunSummaryRepositoryState(
            false,
            "unknown",
            "unknown",
            true,
            "unknown");
        root.Subcommands.Add(PackageReportCommand.Create(
            settings,
            options => new PackageInspector(
                settings.Paths,
                options,
                new StubRepositoryStateCapture(_ => repositoryState),
                TestRunSummaryReporter.CaptureRunnerAssemblies)));
        return root;
    }

    private static void SeedStaleReport(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "report.json"), "stale json");
        File.WriteAllText(Path.Combine(outputDirectory, "report.md"), "stale markdown");
    }

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

    private static PackageDefinition CreateStandardPackage(string packageId, string version = CandidateVersion) =>
        new(
            packageId,
            packageId,
            version,
            $"{packageId} test package.",
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
                ($"lib/net8.0/{packageId}.dll", packageId)),
            packageId,
            version,
            [$"lib/net8.0/{packageId}.pdb"]);

    private static PackageDefinition[] CreateCanonicalPackages() =>
    [
        CreateCorePackage(),
        CreateStandardPackage(PackageInspectionPolicy.SQLitePackageId),
        CreateStandardPackage(PackageInspectionPolicy.MySqlPackageId),
        CreateMemoryPackage(),
        CreateStandardPackage(PackageInspectionPolicy.CliPackageId),
        CreateStandardPackage(PackageInspectionPolicy.ToolsPackageId)
    ];

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

        public PackageFixture(bool packageSourceUnderArtifacts = false)
        {
            PackageDirectory = packageSourceUnderArtifacts
                ? Path.Combine(root, "artifacts", "candidate")
                : Path.Combine(root, "packages");
            Directory.CreateDirectory(PackageDirectory);
        }

        public string RepositoryRoot => root;

        public string PackageDirectory { get; }

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

        public PackageInspectionReport Inspect(
            bool failOnUnexpectedPackage = true,
            bool failOnMissingSymbolPackage = true,
            bool failOnRuntimeRoslyn = true,
            bool failOnRuntimeRemotion = true,
            bool failOnAnalyzerAssetLeak = true,
            string? expectedVersion = null,
            IReadOnlySet<string>? expectedPackageIds = null,
            IReadOnlySet<string>? runtimePackageIds = null,
            string? outputDirectory = null,
            bool cleanRunner = false,
            IRepositoryStateCapture? repositoryStateCapture = null)
        {
            var options = new PackageInspectionOptions(
                root,
                PackageDirectory,
                expectedPackageIds ?? PackageSet(PackageInspectionPolicy.CorePackageId, PackageInspectionPolicy.MemoryPackageId),
                runtimePackageIds ?? PackageSet(PackageInspectionPolicy.CorePackageId, PackageInspectionPolicy.MemoryPackageId),
                FailOnUnexpectedPackage: failOnUnexpectedPackage,
                FailOnMissingSymbolPackage: failOnMissingSymbolPackage,
                FailOnRuntimeRoslyn: failOnRuntimeRoslyn,
                FailOnRuntimeRemotion: failOnRuntimeRemotion,
                FailOnAnalyzerAssetLeak: failOnAnalyzerAssetLeak)
            {
                ExpectedVersion = expectedVersion,
                OutputDirectory = outputDirectory
            };
            var state = cleanRunner ? CleanRepositoryState() : UnavailableRepositoryState();
            return new PackageInspector(
                DevToolPaths.Create(root),
                options,
                repositoryStateCapture ?? new StubRepositoryStateCapture(_ => state),
                cleanRunner ? CleanRunnerAssemblies : TestRunSummaryReporter.CaptureRunnerAssemblies).CreateReport();
        }

        public void CopyArchivesFrom(PackageFixture source)
        {
            foreach (var sourcePath in Directory.EnumerateFiles(source.PackageDirectory))
                File.Copy(sourcePath, Path.Combine(PackageDirectory, Path.GetFileName(sourcePath)), overwrite: true);
        }

        public void WriteCorruptArchive(string fileName) =>
            File.WriteAllText(Path.Combine(PackageDirectory, fileName), "not a zip archive", Encoding.UTF8);

        public void WriteDuplicatePathArchive()
        {
            var definition = CreateStandardPackage("Duplicate.Paths");
            var path = Path.Combine(PackageDirectory, $"{definition.ArchiveId}.{definition.Version}.nupkg");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            WriteZipEntry(
                archive,
                $"{definition.PackageId}.nuspec",
                CreateNuspec(definition).ToString(SaveOptions.DisableFormatting));
            WriteZipEntry(archive, "README.md", "first");
            WriteZipEntry(archive, "readme.md", "second");
        }

        public void RewritePackageNuspec(
            PackageDefinition package,
            string nuspecEntryName,
            string rootLocalName)
        {
            var document = CreateNuspec(package);
            document.Root!.Name = document.Root.Name.Namespace + rootLocalName;
            WriteArchive(
                Path.Combine(PackageDirectory, $"{package.ArchiveId}.{package.Version}.nupkg"),
                nuspecEntryName,
                document,
                package.PackageFiles);
        }

        public void RewriteSymbolNuspec(PackageDefinition package, string nuspecEntryName) =>
            WriteArchive(
                Path.Combine(PackageDirectory, $"{package.ArchiveId}.{package.Version}.snupkg"),
                nuspecEntryName,
                CreateSymbolNuspec(package),
                package.SymbolFiles.ToDictionary(static file => file, static _ => Array.Empty<byte>(), StringComparer.OrdinalIgnoreCase));

        public void Dispose()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        public static IReadOnlySet<string> PackageSet(params string[] packageIds) =>
            packageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static TestRunSummaryRepositoryState CleanRepositoryState() =>
            new(true, RepositoryCommit, "v0.9", false, new string('0', 64));

        private static TestRunSummaryRepositoryState UnavailableRepositoryState() =>
            new(false, "unknown", "unknown", true, "unknown");

        private static (TestRunSummaryRunnerAssembly EntryAssembly, TestRunSummaryRunnerAssembly DevToolsAssembly)
            CleanRunnerAssemblies() =>
            (
                new("DataLinq.Dev.CLI", CandidateVersion, RepositoryCommit, true, "clean"),
                new("DataLinq.DevTools", CandidateVersion, RepositoryCommit, true, "clean")
            );

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
                        new XElement(ns + "description", "Test symbol package."),
                        OptionalRepository(ns, package.RepositoryType, package.RepositoryUrl, package.RepositoryCommit))));
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
            if (File.Exists(path))
                File.Delete(path);
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

    private sealed class StubRepositoryStateCapture(
        Func<string, TestRunSummaryRepositoryState> capture) : IRepositoryStateCapture
    {
        public TestRunSummaryRepositoryState Capture(string repositoryRoot) => capture(repositoryRoot);
    }

    private sealed class SequenceRepositoryStateCapture(
        params TestRunSummaryRepositoryState[] states) : IRepositoryStateCapture
    {
        private int captureCount;

        public int CaptureCount => captureCount;

        public TestRunSummaryRepositoryState Capture(string repositoryRoot)
        {
            var index = captureCount++;
            return states[Math.Min(index, states.Length - 1)];
        }
    }
}
