using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class CompatibilityPackageRestoreAuditorTests
{
    [Test]
    public async Task Audit_AcceptsIsolatedExactPackageGraph()
    {
        using var fixture = new AuditFixture();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsTrue();
        await Assert.That(report.Findings).IsEmpty();
        await Assert.That(report.AssetsPath).IsEqualTo(fixture.AssetsPath);
        await Assert.That(report.ProjectLibraries).IsEquivalentTo(
            ["DataLinq.Memory.PlatformCompatibility.Smoke/1.0.0"]);
        await Assert.That(report.ResolvedPackages.Select(static package => package.Id))
            .IsEquivalentTo(["DataLinq", "DataLinq.Memory"]);
        await Assert.That(report.ResolvedPackages.All(static package =>
            package.ExactVersion &&
            package.SourceMatchesPackageDirectory &&
            package.HashMatchesCandidate &&
            package.ExtractedFilesMatchArchive &&
            package.VerifiedExtractedFileCount == 2)).IsTrue();
    }

    [Test]
    public async Task Audit_RejectsDataLinqProductProjectLibrary()
    {
        using var fixture = new AuditFixture
        {
            ForbiddenProjectId = "DataLinq.Generators"
        };
        fixture.WriteAssets();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("assets-project-library-not-allowed");
        await Assert.That(report.ProjectLibraries)
            .Contains("DataLinq.Generators/1.0.0");
    }

    [Test]
    public async Task Audit_RejectsWrongDataLinqPackageVersion()
    {
        using var fixture = new AuditFixture
        {
            MemoryAssetsVersion = "0.9.0-preview.wrong"
        };
        fixture.WriteAssets();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("package-version-mismatch")
            .And.Contains("resolved-package-version-mismatch");
        await Assert.That(report.ResolvedPackages.Single(static package =>
            package.Id == "DataLinq.Memory").ExactVersion).IsFalse();
    }

    [Test]
    public async Task Audit_RejectsPackageMetadataSourceOutsideCandidateDirectory()
    {
        using var fixture = new AuditFixture();
        fixture.WriteMetadata("DataLinq.Memory", Path.Combine(fixture.Root, "other-source"));

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("package-source-mismatch");
        await Assert.That(report.ResolvedPackages.Single(static package =>
            package.Id == "DataLinq.Memory").SourceMatchesPackageDirectory).IsFalse();
    }

    [Test]
    public async Task Audit_RejectsCachedPackageWhoseHashDiffersFromCandidate()
    {
        using var fixture = new AuditFixture();
        File.WriteAllText(
            fixture.CachedPackagePath("DataLinq.Memory", AuditFixture.Version),
            "tampered-cache-content",
            Encoding.UTF8);

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("package-hash-mismatch");
        await Assert.That(report.ResolvedPackages.Single(static package =>
            package.Id == "DataLinq.Memory").HashMatchesCandidate).IsFalse();
    }

    [Test]
    public async Task Audit_RejectsRestoreFallbackFolder()
    {
        using var fixture = new AuditFixture
        {
            FallbackFolders = [Path.Combine(Path.GetTempPath(), "ambient-packages")]
        };
        fixture.WriteAssets();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("assets-fallback-folders");
    }

    [Test]
    public async Task Audit_RejectsTamperedExtractedDllWhenCachedArchiveIsUntouched()
    {
        using var fixture = new AuditFixture();
        fixture.WriteExtractedFile(
            "DataLinq.Memory",
            "lib/net10.0/DataLinq.Memory.dll",
            Encoding.UTF8.GetBytes("tampered-extracted-dll"));

        var report = fixture.Audit();

        var memory = report.ResolvedPackages.Single(static package => package.Id == "DataLinq.Memory");
        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("package-extracted-file-content-mismatch");
        await Assert.That(memory.HashMatchesCandidate).IsTrue();
        await Assert.That(memory.ExtractedFilesMatchArchive).IsFalse();
        await Assert.That(memory.VerifiedExtractedFileCount).IsEqualTo(1);
    }

    [Test]
    public async Task Audit_RejectsListedFileMissingFromArchive()
    {
        using var fixture = new AuditFixture();
        fixture.AddListedFile("DataLinq.Memory", "lib/net10.0/Missing.dll");
        fixture.WriteAssets();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("package-archive-entry-missing");
    }

    [Test]
    public async Task Audit_RejectsArchiveEntryMissingFromAssetsFileList()
    {
        using var fixture = new AuditFixture();
        fixture.AddArchiveEntry(
            "DataLinq.Memory",
            "lib/net10.0/Unlisted.dll",
            Encoding.UTF8.GetBytes("unlisted"));

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("package-archive-entry-unlisted");
    }

    [Test]
    public async Task Audit_RejectsAmbiguousArchiveEntries()
    {
        using var fixture = new AuditFixture();
        fixture.AddArchiveEntry(
            "DataLinq.Memory",
            "LIB/net10.0/DataLinq.Memory.dll",
            Encoding.UTF8.GetBytes("ambiguous-second-entry"));

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("package-archive-entry-ambiguous");
    }

    [Test]
    public async Task Audit_RejectsExtractedFilePathEscape()
    {
        using var fixture = new AuditFixture();
        fixture.AddListedFile("DataLinq.Memory", "../outside.dll");
        fixture.WriteAssets();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("package-file-path-invalid");
    }

    [Test]
    public async Task Audit_RejectsReparseTraversedExtractedFile()
    {
        using var fixture = new AuditFixture();
        fixture.ReplaceExtractedLibDirectoryWithLink("DataLinq.Memory");

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("package-extracted-file-untrusted");
    }

    [Test]
    public async Task Audit_RejectsSameNameSharedProjectSubstitution()
    {
        using var fixture = new AuditFixture();
        var substitutePath = fixture.CreateSubstituteSharedProject();
        fixture.ProjectReferencePath = substitutePath;
        fixture.ProjectLibraryRelativePath = Path.GetRelativePath(
            fixture.HostProjectDirectory,
            substitutePath);
        fixture.WriteAssets();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("assets-shared-smoke-project-reference-mismatch")
            .And.Contains("assets-shared-smoke-project-path-mismatch");
    }

    [Test]
    public async Task Audit_RejectsHostProjectIdentitySubstitution()
    {
        using var fixture = new AuditFixture();
        var substitutePath = Path.Combine(
            fixture.RepositoryRoot,
            "substitute",
            "CompatibilityHost.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(substitutePath)!);
        File.WriteAllText(substitutePath, "<Project Sdk=\"Microsoft.NET.Sdk\" />", Encoding.UTF8);
        fixture.RestoreHostProjectPath = substitutePath;
        fixture.WriteAssets();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("assets-host-project-identity-mismatch");
    }

    [Test]
    public async Task Audit_RejectsReparseSubstitutedSharedProject()
    {
        using var fixture = new AuditFixture();
        fixture.ReplaceSharedProjectDirectoryWithLink();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("assets-shared-smoke-project-reference-reparse")
            .And.Contains("assets-shared-smoke-project-path-mismatch");
    }

    [Test]
    public async Task Audit_RejectsMissingTargetsGraph()
    {
        using var fixture = new AuditFixture { OmitTargets = true };
        fixture.WriteAssets();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("assets-targets-missing");
    }

    [Test]
    public async Task Audit_RejectsEmptyActiveTargetGraph()
    {
        using var fixture = new AuditFixture { EmptyActiveTarget = true };
        fixture.WriteAssets();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("assets-active-target-empty");
    }

    [Test]
    public async Task Audit_RejectsWrongRuntimeTargetGraph()
    {
        using var fixture = new AuditFixture { ActiveTargetName = "net10.0/linux-x64" };
        fixture.WriteAssets();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("assets-active-target-count");
    }

    [Test]
    public async Task Audit_RejectsActiveTargetMissingExpectedPackage()
    {
        using var fixture = new AuditFixture { OmitActiveLibraryId = "DataLinq.Memory" };
        fixture.WriteAssets();

        var report = fixture.Audit();

        await Assert.That(report.Passed).IsFalse();
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("assets-active-target-library-missing");
    }

    private sealed class AuditFixture : IDisposable
    {
        public const string Version = "0.9.0-preview.audit.1";
        public const string RuntimeIdentifier = "win-x64";

        private readonly Dictionary<string, List<ArchiveFile>> archiveFiles =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> listedFiles =
            new(StringComparer.OrdinalIgnoreCase);
        private bool sharedProjectDirectoryIsLink;
        private string? extractedLibraryDirectoryLink;

        public AuditFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"datalinq-compat-restore-audit-{Guid.NewGuid():N}");
            RepositoryRoot = Path.Combine(Root, "repository");
            HostProjectDirectory = Path.Combine(RepositoryRoot, "src", "CompatibilityHost");
            SharedProjectPath = Path.Combine(
                RepositoryRoot,
                "src",
                "DataLinq.Memory.PlatformCompatibility.Smoke",
                "DataLinq.Memory.PlatformCompatibility.Smoke.csproj");
            BuildScratchDirectory = Path.Combine(Root, "build");
            PackagesCacheDirectory = Path.Combine(Root, "packages-cache");
            PackageDirectory = Path.Combine(Root, "candidate");
            NugetConfigPath = Path.Combine(Root, "NuGet.Config");
            AssetsPath = Path.Combine(BuildScratchDirectory, "obj", "CompatibilityHost", "project.assets.json");

            Directory.CreateDirectory(Path.GetDirectoryName(AssetsPath)!);
            Directory.CreateDirectory(PackagesCacheDirectory);
            Directory.CreateDirectory(PackageDirectory);
            Directory.CreateDirectory(HostProjectDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(SharedProjectPath)!);
            File.WriteAllText(
                Path.Combine(HostProjectDirectory, "CompatibilityHost.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />",
                Encoding.UTF8);
            File.WriteAllText(
                SharedProjectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\" />",
                Encoding.UTF8);
            File.WriteAllText(NugetConfigPath, "<configuration />", Encoding.UTF8);

            Target = new CompatibilityTargetDefinition(
                "memory-native-aot",
                CompatibilityTargetKind.NativeAot,
                CompatibilityRuntimeGraph.Memory,
                "Synthetic Memory Native AOT",
                @"src\CompatibilityHost\CompatibilityHost.csproj",
                "net10.0",
                true,
                false,
                "CompatibilityHost",
                []);
            ProjectReferencePath = SharedProjectPath;
            ProjectLibraryRelativePath = Path.GetRelativePath(HostProjectDirectory, SharedProjectPath);
            RestoreHostProjectPath = Path.Combine(HostProjectDirectory, "CompatibilityHost.csproj");

            InitializePackage("DataLinq");
            InitializePackage("DataLinq.Memory");
            RebuildInput();
            WriteAssets();
        }

        public string Root { get; }

        public string RepositoryRoot { get; }

        public string HostProjectDirectory { get; }

        public string SharedProjectPath { get; }

        public string BuildScratchDirectory { get; }

        public string PackagesCacheDirectory { get; }

        public string PackageDirectory { get; }

        public string NugetConfigPath { get; }

        public string AssetsPath { get; }

        public CompatibilityPackageInput Input { get; private set; } = null!;

        public CompatibilityTargetDefinition Target { get; }

        public string? ForbiddenProjectId { get; init; }

        public string MemoryAssetsVersion { get; init; } = Version;

        public string[] FallbackFolders { get; init; } = [];

        public string ProjectReferencePath { get; set; }

        public string ProjectLibraryRelativePath { get; set; }

        public string RestoreHostProjectPath { get; set; }

        public bool OmitTargets { get; init; }

        public bool EmptyActiveTarget { get; init; }

        public string ActiveTargetName { get; init; } = $"net10.0/{RuntimeIdentifier}";

        public string? OmitActiveLibraryId { get; init; }

        public CompatibilityPackageResolutionReport Audit() =>
            CompatibilityPackageRestoreAuditor.Audit(
                Target,
                RepositoryRoot,
                RuntimeIdentifier,
                BuildScratchDirectory,
                PackagesCacheDirectory,
                NugetConfigPath,
                Input);

        public void WriteAssets()
        {
            var libraries = new Dictionary<string, object?>
            {
                ["DataLinq.Memory.PlatformCompatibility.Smoke/1.0.0"] = new
                {
                    type = "project",
                    path = ProjectLibraryRelativePath.Replace('\\', '/')
                },
                [$"DataLinq/{Version}"] = PackageLibrary("DataLinq", Version),
                [$"DataLinq.Memory/{MemoryAssetsVersion}"] = PackageLibrary("DataLinq.Memory", MemoryAssetsVersion)
            };
            if (ForbiddenProjectId is not null)
            {
                libraries[$"{ForbiddenProjectId}/1.0.0"] = new
                {
                    type = "project",
                    path = $"../{ForbiddenProjectId}/{ForbiddenProjectId}.csproj"
                };
            }

            var projectReferences = new Dictionary<string, object?>
            {
                [ProjectReferencePath] = new { projectPath = ProjectReferencePath }
            };
            var restoreFrameworks = new Dictionary<string, object?>
            {
                [Target.TargetFramework] = new { projectReferences }
            };
            var assets = new Dictionary<string, object?>
            {
                ["version"] = 3,
                ["libraries"] = libraries,
                ["packageFolders"] = new Dictionary<string, object?>
                {
                    [EnsureTrailingSeparator(PackagesCacheDirectory)] = new { }
                },
                ["project"] = new
                {
                    restore = new
                    {
                        projectPath = RestoreHostProjectPath,
                        projectUniqueName = RestoreHostProjectPath,
                        packagesPath = PackagesCacheDirectory,
                        configFilePaths = new[] { NugetConfigPath },
                        fallbackFolders = FallbackFolders,
                        frameworks = restoreFrameworks
                    }
                }
            };

            if (!OmitTargets)
            {
                var baseTarget = CreateTargetLibraries();
                var activeTarget = EmptyActiveTarget
                    ? new Dictionary<string, object?>()
                    : CreateTargetLibraries();
                assets["targets"] = new Dictionary<string, object?>
                {
                    [Target.TargetFramework] = baseTarget,
                    [ActiveTargetName] = activeTarget
                };
            }

            File.WriteAllText(
                AssetsPath,
                JsonSerializer.Serialize(assets),
                Encoding.UTF8);
        }

        public void WriteMetadata(string id, string source)
        {
            var metadataPath = Path.Combine(PackageCacheDirectory(id, Version), ".nupkg.metadata");
            File.WriteAllText(
                metadataPath,
                JsonSerializer.Serialize(new { version = 2, source }),
                Encoding.UTF8);
        }

        public void WriteExtractedFile(string id, string relativePath, byte[] content)
        {
            var path = Path.Combine(
                PackageCacheDirectory(id, Version),
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        public void AddListedFile(string id, string relativePath) =>
            listedFiles[id].Add(relativePath);

        public void AddArchiveEntry(string id, string relativePath, byte[] content)
        {
            archiveFiles[id].Add(new ArchiveFile(relativePath, content));
            WritePackage(id);
            RebuildInput();
        }

        public string CreateSubstituteSharedProject()
        {
            var path = Path.Combine(
                RepositoryRoot,
                "substitute",
                "DataLinq.Memory.PlatformCompatibility.Smoke.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\" />", Encoding.UTF8);
            return path;
        }

        public void ReplaceSharedProjectDirectoryWithLink()
        {
            var sharedDirectory = Path.GetDirectoryName(SharedProjectPath)!;
            Directory.Delete(sharedDirectory, recursive: true);
            var linkTarget = Path.Combine(Root, "linked-shared-project");
            Directory.CreateDirectory(linkTarget);
            File.WriteAllText(
                Path.Combine(linkTarget, Path.GetFileName(SharedProjectPath)),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />",
                Encoding.UTF8);
            CreateDirectoryLink(sharedDirectory, linkTarget);
            sharedProjectDirectoryIsLink = true;
        }

        public void ReplaceExtractedLibDirectoryWithLink(string id)
        {
            var libraryDirectory = Path.Combine(PackageCacheDirectory(id, Version), "lib");
            Directory.Delete(libraryDirectory, recursive: true);
            var linkTarget = Path.Combine(Root, $"linked-{id.ToLowerInvariant()}-lib");
            var dll = archiveFiles[id].Single(file => file.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            var targetPath = Path.Combine(
                linkTarget,
                dll.Path["lib/".Length..].Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, dll.Content);
            CreateDirectoryLink(libraryDirectory, linkTarget);
            extractedLibraryDirectoryLink = libraryDirectory;
        }

        public string CachedPackagePath(string id, string version) =>
            Path.Combine(
                PackageCacheDirectory(id, version),
                $"{id.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg");

        public void Dispose()
        {
            try
            {
                if (extractedLibraryDirectoryLink is not null &&
                    Directory.Exists(extractedLibraryDirectoryLink) &&
                    (File.GetAttributes(extractedLibraryDirectoryLink) & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(extractedLibraryDirectoryLink);
                }

                var sharedDirectory = Path.GetDirectoryName(SharedProjectPath)!;
                if (sharedProjectDirectoryIsLink &&
                    Directory.Exists(sharedDirectory) &&
                    (File.GetAttributes(sharedDirectory) & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(sharedDirectory);
                }

                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // A failed cleanup must not mask the assertion that created the fixture.
            }
        }

        private object PackageLibrary(string id, string version) => new
        {
            type = "package",
            path = $"{id.ToLowerInvariant()}/{version.ToLowerInvariant()}",
            files = PackageAssetFiles(id)
        };

        private string[] PackageAssetFiles(string id) =>
            [
                ".nupkg.metadata",
                $"{id.ToLowerInvariant()}.{Version.ToLowerInvariant()}.nupkg.sha512",
                .. listedFiles[id]
            ];

        private Dictionary<string, object?> CreateTargetLibraries()
        {
            var targetLibraries = new Dictionary<string, object?>
            {
                ["DataLinq.Memory.PlatformCompatibility.Smoke/1.0.0"] = new { type = "project" },
                [$"DataLinq/{Version}"] = new { type = "package" },
                [$"DataLinq.Memory/{MemoryAssetsVersion}"] = new { type = "package" }
            };
            if (OmitActiveLibraryId is not null)
            {
                var key = targetLibraries.Keys.Single(key =>
                    key[..key.LastIndexOf('/')].Equals(OmitActiveLibraryId, StringComparison.OrdinalIgnoreCase));
                targetLibraries.Remove(key);
            }

            return targetLibraries;
        }

        private void InitializePackage(string id)
        {
            var nuspecPath = $"{id}.nuspec";
            var dllPath = $"lib/net10.0/{id}.dll";
            archiveFiles[id] =
            [
                new ArchiveFile(
                    nuspecPath,
                    Encoding.UTF8.GetBytes(
                        $"<?xml version=\"1.0\"?><package><metadata><id>{id}</id><version>{Version}</version><authors>Tests</authors><description>Audit fixture</description></metadata></package>")),
                new ArchiveFile(dllPath, Encoding.UTF8.GetBytes($"exact-{id}-assembly"))
            ];
            listedFiles[id] = [nuspecPath, dllPath];
            WritePackage(id);
        }

        private void WritePackage(string id)
        {
            byte[] packageBytes;
            using (var output = new MemoryStream())
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteArchiveEntry(archive, "_rels/.rels", Encoding.UTF8.GetBytes("<Relationships />"));
                    foreach (var file in archiveFiles[id])
                        WriteArchiveEntry(archive, file.Path, file.Content);
                    WriteArchiveEntry(archive, "[Content_Types].xml", Encoding.UTF8.GetBytes("<Types />"));
                    WriteArchiveEntry(
                        archive,
                        "package/services/metadata/core-properties/audit.psmdcp",
                        Encoding.UTF8.GetBytes("<coreProperties />"));
                }

                packageBytes = output.ToArray();
            }

            var candidatePath = Path.Combine(PackageDirectory, $"{id}.{Version}.nupkg");
            File.WriteAllBytes(candidatePath, packageBytes);
            var cacheDirectory = PackageCacheDirectory(id, Version);
            Directory.CreateDirectory(cacheDirectory);
            File.WriteAllBytes(CachedPackagePath(id, Version), packageBytes);
            File.WriteAllText(
                Path.Combine(cacheDirectory, $"{id.ToLowerInvariant()}.{Version.ToLowerInvariant()}.nupkg.sha512"),
                "synthetic-sha512",
                Encoding.UTF8);
            WriteMetadata(id, PackageDirectory);

            foreach (var file in archiveFiles[id])
                WriteExtractedFile(id, file.Path, file.Content);
        }

        private void RebuildInput()
        {
            var candidates = archiveFiles.Keys.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .Select(id =>
                {
                    var candidatePath = Path.Combine(PackageDirectory, $"{id}.{Version}.nupkg");
                    var content = File.ReadAllBytes(candidatePath);
                    return new CompatibilityCandidatePackage(
                        id,
                        Version,
                        candidatePath,
                        content.LongLength,
                        ComputeSha256(content),
                        "0123456789abcdef0123456789abcdef01234567");
                })
                .ToArray();
            Input = new CompatibilityPackageInput(
                PackageDirectory,
                Version,
                "synthetic-aggregate-identity",
                "pkg-synthetic",
                candidates);
        }

        private string PackageCacheDirectory(string id, string version) =>
            Path.Combine(PackagesCacheDirectory, id.ToLowerInvariant(), version.ToLowerInvariant());

        private static void WriteArchiveEntry(ZipArchive archive, string path, byte[] content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
            entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var stream = entry.Open();
            stream.Write(content);
        }

        private static string ComputeSha256(byte[] content) =>
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        private static string EnsureTrailingSeparator(string path) =>
            Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

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

        private sealed record ArchiveFile(string Path, byte[] Content);
    }
}
