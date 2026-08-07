using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class PackageConsumerSmokeTests
{
    private const string CandidateVersion = "0.9.0-preview.package-consumer.1";

    [Test]
    public async Task Fixture_UsesExactMultiTargetedPackageOnlyGraph()
    {
        var fixtureDirectory = GetFixtureDirectory();
        var projectPath = Path.Combine(fixtureDirectory, "DataLinq.PackageConsumer.csproj");
        var document = XDocument.Load(projectPath);
        var targetFrameworks = document.Descendants()
            .Where(static element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .SelectMany(static element => element.Value.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        var packageReferences = document.Descendants()
            .Where(static element => element.Name.LocalName == "PackageReference")
            .Select(static element => string.Join(
                "=",
                (string?)element.Attribute("Include") ?? "",
                (string?)element.Attribute("Version") ?? ""))
            .ToArray();

        await Assert.That(string.Join(";", targetFrameworks)).IsEqualTo("net8.0;net9.0;net10.0");
        await Assert.That(string.Join("|", packageReferences)).IsEqualTo(
            "DataLinq=[$(DataLinqCandidateVersion)]|" +
            "DataLinq.Memory=[$(DataLinqCandidateVersion)]|" +
            "DataLinq.SQLite=[$(DataLinqCandidateVersion)]|" +
            "DataLinq.MySql=[$(DataLinqCandidateVersion)]");
        await Assert.That(document.Descendants().Count(static element => element.Name.LocalName == "ProjectReference"))
            .IsEqualTo(0);
        await Assert.That(document.Descendants()
                .Single(static element => element.Name.LocalName == "ManagePackageVersionsCentrally")
                .Value)
            .IsEqualTo("false");
        await Assert.That(document.Descendants()
                .Single(static element => element.Name.LocalName == "Error")
                .Attribute("Condition")?.Value)
            .Contains("DataLinqCandidateVersion");
    }

    [Test]
    public async Task Fixture_ExercisesGeneratedMemorySQLiteAndMySqlPublicContracts()
    {
        var fixtureDirectory = GetFixtureDirectory();
        var model = File.ReadAllText(Path.Combine(fixtureDirectory, "PackageConsumerModel.cs"), Encoding.UTF8);
        var program = File.ReadAllText(Path.Combine(fixtureDirectory, "Program.cs"), Encoding.UTF8);

        await Assert.That(model)
            .Contains("public sealed partial class PackageConsumerDatabase")
            .And.Contains(": IDatabaseModel")
            .And.Contains("public abstract partial class PackageConsumerRow")
            .And.Contains("ITableModel<PackageConsumerDatabase>")
            .And.Contains("[PrimaryKey]");

        await Assert.That(program)
            .Contains("MutablePackageConsumerRow[] CreateRows()")
            .And.Contains("new MemoryDatabase<PackageConsumerDatabase>()")
            .And.Contains("database.Seed<PackageConsumerRow>(CreateRows())")
            .And.Contains("database.Find<PackageConsumerRow>(17)")
            .And.Contains("SQLiteProvider.RegisterProvider()")
            .And.Contains("new SQLiteDatabase<PackageConsumerDatabase>")
            .And.Contains("PluginHook.CreateDatabaseFromMetadata(")
            .And.Contains("typeof(MySqlDatabase<PackageConsumerDatabase>)")
            .And.Contains("GetConstructor([typeof(string)])")
            .And.Contains("v0.9.package-consumer-execution.v1")
            .And.Contains("queryIds.SequenceEqual([-5, 17])")
            .And.Contains("rowIds.SequenceEqual([-5, 17, 42])");
    }

    [Test]
    [Arguments(false, "candidate-directory-missing", 5)]
    [Arguments(true, "candidate-required-package-missing", 4)]
    public async Task Runner_MissingOrEmptyCandidatesFailClosedWithoutCommands(
        bool createCandidateDirectory,
        string expectedFindingCode,
        int expectedFindingCount)
    {
        using var fixture = new RunnerFixture();
        if (createCandidateDirectory)
            Directory.CreateDirectory(fixture.PackageDirectory);

        var report = fixture.Run();

        await Assert.That(report.Commands).IsEmpty();
        await Assert.That(report.CandidatePackages).IsEmpty();
        await Assert.That(report.ResolvedPackages).IsEmpty();
        await Assert.That(report.Findings.Select(static finding => finding.Code)).Contains(expectedFindingCode);
        await Assert.That(report.Summary.RequiredPackageCount).IsEqualTo(4);
        await Assert.That(report.Summary.CandidatePackageCount).IsEqualTo(0);
        await Assert.That(report.Summary.FindingCount).IsEqualTo(expectedFindingCount);
        await Assert.That(report.Summary.HardFailureCount).IsEqualTo(expectedFindingCount);
        await Assert.That(report.Summary.HasHardFailures).IsTrue();
        await Assert.That(File.Exists(Path.Combine(fixture.OutputDirectory, "report.json"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(fixture.OutputDirectory, "report.md"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(fixture.OutputDirectory, "NuGet.Config"))).IsFalse();
    }

    [Test]
    public async Task Runner_RejectsWrongVersionMinimalPackageBeforeRestore()
    {
        using var fixture = new RunnerFixture();
        Directory.CreateDirectory(fixture.PackageDirectory);
        WriteMinimalPackage(
            Path.Combine(fixture.PackageDirectory, "DataLinq.0.9.0-wrong.nupkg"),
            "DataLinq",
            "0.9.0-wrong");

        var report = fixture.Run();
        var candidate = report.CandidatePackages.Single();

        await Assert.That(report.Commands).IsEmpty();
        await Assert.That(candidate.Id).IsEqualTo("DataLinq");
        await Assert.That(candidate.Version).IsEqualTo("0.9.0-wrong");
        await Assert.That(candidate.NuspecIdentityMatches).IsFalse();
        await Assert.That(candidate.SizeBytes).IsGreaterThan(0);
        await Assert.That(candidate.Sha256.Length).IsEqualTo(64);
        await Assert.That(report.Findings.Select(static finding => finding.Code))
            .Contains("candidate-version-mismatch")
            .And.Contains("candidate-required-package-missing");
        await Assert.That(report.Summary.CandidatePackageCount).IsEqualTo(1);
        await Assert.That(report.Summary.HasHardFailures).IsTrue();
    }

    [Test]
    public async Task Runner_RejectsNonemptyOutputWithoutDeletingExistingContent()
    {
        using var fixture = new RunnerFixture();
        Directory.CreateDirectory(fixture.PackageDirectory);
        Directory.CreateDirectory(fixture.OutputDirectory);
        var sentinelPath = Path.Combine(fixture.OutputDirectory, "preserve.txt");
        File.WriteAllText(sentinelPath, "preserve", Encoding.UTF8);
        InvalidOperationException? exception = null;

        try
        {
            _ = fixture.Run();
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("must be empty");
        await Assert.That(File.ReadAllText(sentinelPath, Encoding.UTF8)).IsEqualTo("preserve");
        await Assert.That(File.Exists(Path.Combine(fixture.OutputDirectory, "report.json"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(fixture.OutputDirectory, "report.md"))).IsFalse();
    }

    [Test]
    public async Task FailedReport_SerializesVersionedJsonSummaryAndMarkdown()
    {
        using var fixture = new RunnerFixture();
        Directory.CreateDirectory(fixture.PackageDirectory);

        var report = fixture.Run();
        var jsonPath = Path.Combine(fixture.OutputDirectory, "report.json");
        var markdownPath = Path.Combine(fixture.OutputDirectory, "report.md");
        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
        var root = json.RootElement;
        var summary = root.GetProperty("summary");
        var markdown = File.ReadAllText(markdownPath, Encoding.UTF8);

        await Assert.That(report.SchemaVersion).IsEqualTo("v0.9.package-consumer-smoke-report.v2");
        await Assert.That(report.Outcome).IsEqualTo(PackageConsumerSmokeOutcome.Failed);
        await Assert.That(report.IsCompleteForInvocation).IsFalse();
        await Assert.That(report.OverallExitCode).IsEqualTo(1);
        await Assert.That(report.StartedAtUtc).IsLessThanOrEqualTo(report.CompletedAtUtc);
        await Assert.That(report.DurationSeconds).IsGreaterThanOrEqualTo(0);
        await Assert.That(report.Summary.BuildCount).IsEqualTo(0);
        await Assert.That(report.Summary.SuccessfulBuildCount).IsEqualTo(0);
        await Assert.That(report.Summary.RestoreSucceeded).IsFalse();
        await Assert.That(report.Summary.ExecutionSucceeded).IsFalse();
        await Assert.That(report.Summary.GeneratedSourceVerified).IsFalse();
        await Assert.That(root.GetProperty("schemaVersion").GetString())
            .IsEqualTo("v0.9.package-consumer-smoke-report.v2");
        await Assert.That(root.GetProperty("outcome").GetString()).IsEqualTo("Failed");
        await Assert.That(root.GetProperty("isCompleteForInvocation").GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty("overallExitCode").GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty("profile").GetString()).IsEqualTo("Sandbox");
        await Assert.That(root.GetProperty("commands").GetArrayLength()).IsEqualTo(0);
        await Assert.That(summary.GetProperty("requiredPackageCount").GetInt32()).IsEqualTo(4);
        await Assert.That(summary.GetProperty("hasHardFailures").GetBoolean()).IsTrue();
        await Assert.That(markdown).IsEqualTo(PackageConsumerSmokeRunner.ToMarkdown(report));
        await Assert.That(markdown)
            .Contains("# Package Consumer Smoke Report")
            .And.Contains("Outcome: **failed**")
            .And.Contains("| Command | Result | Exit | Log |")
            .And.Contains("| Resolved package | Version | Local source | SHA-256 match |")
            .And.Contains("## Findings")
            .And.Contains("candidate-required-package-missing");
        await Assert.That(report.ArtifactPaths).Contains(jsonPath).And.Contains(markdownPath);
        await Assert.That(Directory.EnumerateFiles(fixture.OutputDirectory, "*.tmp", SearchOption.TopDirectoryOnly))
            .IsEmpty();
    }

    [Test]
    public async Task GeneratedSourceInspection_RequiresEveryTargetFramework()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            nameof(PackageConsumerSmokeTests),
            Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var targetFramework in new[] { "net8.0", "net9.0", "net10.0" })
            {
                var targetDirectory = Path.Combine(root, targetFramework);
                Directory.CreateDirectory(targetDirectory);
                File.WriteAllText(
                    Path.Combine(targetDirectory, "Mutable.g.cs"),
                    "partial class MutablePackageConsumerRow { }",
                    Encoding.UTF8);
                if (targetFramework != "net10.0")
                {
                    File.WriteAllText(
                        Path.Combine(targetDirectory, "Database.g.cs"),
                        "partial class PackageConsumerDatabase { }",
                        Encoding.UTF8);
                }
            }

            var incomplete = PackageConsumerSmokeRunner.InspectGeneratedSource(root);

            await Assert.That(incomplete.Passed).IsFalse();
            await Assert.That(incomplete.TargetFrameworks.Count).IsEqualTo(3);
            await Assert.That(incomplete.TargetFrameworks[0].Passed).IsTrue();
            await Assert.That(incomplete.TargetFrameworks[1].Passed).IsTrue();
            await Assert.That(incomplete.TargetFrameworks[2].Passed).IsFalse();

            File.WriteAllText(
                Path.Combine(root, "net10.0", "Database.g.cs"),
                "partial class PackageConsumerDatabase { }",
                Encoding.UTF8);
            var complete = PackageConsumerSmokeRunner.InspectGeneratedSource(root);

            await Assert.That(complete.Passed).IsTrue();
            await Assert.That(complete.TargetFrameworks.All(static target => target.Passed)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task PackageVersionValidation_RejectsWhitespaceAndMsBuildPropertyInjection()
    {
        await Assert.That(CompatibilityPackageInputInspector.IsValidPackageVersion("0.9.0-preview.1")).IsTrue();
        await Assert.That(CompatibilityPackageInputInspector.IsValidPackageVersion(" 0.9.0")).IsFalse();
        await Assert.That(CompatibilityPackageInputInspector.IsValidPackageVersion(
                "0.9.0;ImportDirectoryBuildProps=true"))
            .IsFalse();
    }

    [Test]
    public async Task ExecutionContract_RejectsSelfDeclaredPassWithMismatchedPayload()
    {
        var execution = new PackageConsumerExecutionReport(
            SchemaVersion: "v0.9.package-consumer-execution.v1",
            TargetFramework: "net10.0",
            Memory: new PackageConsumerMemoryExecutionReport(
                Passed: true,
                FoundId: 17,
                Missing: true,
                QueryIds: [-5, 18]),
            Sqlite: new PackageConsumerSQLiteExecutionReport(
                Passed: true,
                RowIds: [-5, 17, 42]),
            MySqlCompilationProbe: true,
            Passed: true,
            ContractValidated: false,
            RawJson: "{}");

        await Assert.That(PackageConsumerSmokeRunner.IsExecutionContractValid(execution, exitCode: 0)).IsFalse();
        await Assert.That(execution.Passed).IsTrue();
        await Assert.That(execution.ContractValidated).IsFalse();
    }

    [Test]
    public async Task ExternalProcessRunner_NullOverrideRemovesAllCaseVariants()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PROJECTASSETSFILE"] = "poison-one",
            ["ProjectAssetsFile"] = "poison-two",
            ["PATH"] = "preserve"
        };
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["projectassetsfile"] = null,
            ["PinnedRoot"] = "isolated"
        };

        ExternalProcessRunner.ApplyEnvironmentOverrides(environment, overrides);

        await Assert.That(environment.Keys.Any(static key =>
            key.Equals("ProjectAssetsFile", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(environment["PATH"]).IsEqualTo("preserve");
        await Assert.That(environment["PinnedRoot"]).IsEqualTo("isolated");
    }

    [Test]
    public async Task IsolationContract_ClearsRedirectsAndPinsEveryBuildRoot()
    {
        using var fixture = new RunnerFixture();
        var reportDirectory = fixture.OutputDirectory;
        var buildDirectory = Path.Combine(reportDirectory, ".artifacts");
        var extensionsDirectory = Path.Combine(buildDirectory, "obj", "DataLinq.PackageConsumer");
        var assetsPath = Path.Combine(extensionsDirectory, "project.assets.json");
        var packagesDirectory = Path.Combine(reportDirectory, ".nuget", "packages");
        var httpCacheDirectory = Path.Combine(reportDirectory, ".nuget", "http-cache");
        var tempDirectory = Path.Combine(reportDirectory, ".tmp");
        var configPath = Path.Combine(reportDirectory, "NuGet.Config");

        var environment = PackageConsumerSmokeRunner.CreateIsolatedEnvironment(
            reportDirectory,
            packagesDirectory,
            httpCacheDirectory,
            tempDirectory);
        var restoreArguments = PackageConsumerSmokeRunner.CreateRestoreIsolationArguments(
            buildDirectory,
            extensionsDirectory,
            packagesDirectory,
            configPath);
        var buildArguments = PackageConsumerSmokeRunner.CreateBuildIsolationArguments(
            buildDirectory,
            extensionsDirectory,
            assetsPath,
            packagesDirectory);

        await Assert.That(environment["ProjectAssetsFile"]).IsNull();
        await Assert.That(environment["DirectoryBuildPropsPath"]).IsNull();
        await Assert.That(environment["CustomAfterMicrosoftCommonTargets"]).IsNull();
        await Assert.That(environment["RestoreSources"]).IsNull();
        await Assert.That(environment["NUGET_PACKAGES"]).IsEqualTo(packagesDirectory);
        await Assert.That(environment["NUGET_HTTP_CACHE_PATH"]).IsEqualTo(httpCacheDirectory);
        await Assert.That(environment["RestoreConfigFile"]).IsEqualTo(configPath);
        await Assert.That(environment["MSBuildUserExtensionsPath"])
            .IsEqualTo(Path.Combine(reportDirectory, ".msbuild-user"));

        var restore = string.Join("|", restoreArguments);
        await Assert.That(restore)
            .Contains("-noAutoResponse")
            .And.Contains($"--packages|{packagesDirectory}")
            .And.Contains($"--artifacts-path={buildDirectory}")
            .And.Contains($"-p:MSBuildProjectExtensionsPath={extensionsDirectory}")
            .And.Contains($"-p:RestoreOutputPath={extensionsDirectory}")
            .And.Contains($"-p:RestoreConfigFile={configPath}")
            .And.Contains("-p:ImportDirectoryBuildProps=false")
            .And.Contains("-p:ImportDirectoryBuildTargets=false")
            .And.Contains("-p:ImportDirectoryPackagesProps=false");

        var build = string.Join("|", buildArguments);
        await Assert.That(build)
            .Contains("-noAutoResponse")
            .And.Contains($"--artifacts-path={buildDirectory}")
            .And.Contains($"-p:MSBuildProjectExtensionsPath={extensionsDirectory}")
            .And.Contains($"-p:ProjectAssetsFile={assetsPath}")
            .And.Contains($"-p:NuGetPackageRoot={packagesDirectory}")
            .And.Contains($"-p:NuGetPackageFolders={packagesDirectory}")
            .And.Contains($"-p:RestorePackagesPath={packagesDirectory}");
    }

    [Test]
    public async Task RestoreState_AcceptsOnlyThePinnedCacheAndConfig()
    {
        using var fixture = new RunnerFixture();
        var packagesDirectory = Path.Combine(fixture.OutputDirectory, "packages");
        var configPath = Path.Combine(fixture.OutputDirectory, "NuGet.Config");
        using var assets = CreateRestoreState(
            packagesDirectory,
            packagesDirectory,
            [configPath],
            []);
        var findings = new List<PackageConsumerSmokeFinding>();

        PackageConsumerSmokeRunner.ValidateRestoreState(
            assets.RootElement,
            packagesDirectory,
            configPath,
            findings);

        await Assert.That(findings).IsEmpty();
    }

    [Test]
    [Arguments("package-folders", "assets-package-folders-mismatch")]
    [Arguments("packages-path", "assets-packages-path-mismatch")]
    [Arguments("config-paths", "assets-config-paths-mismatch")]
    [Arguments("fallback-folders", "assets-fallback-folders")]
    public async Task RestoreState_RejectsRedirectedOrFallbackState(string poison, string expectedCode)
    {
        using var fixture = new RunnerFixture();
        var packagesDirectory = Path.Combine(fixture.OutputDirectory, "packages");
        var otherDirectory = Path.Combine(fixture.OutputDirectory, "other");
        var configPath = Path.Combine(fixture.OutputDirectory, "NuGet.Config");
        using var assets = CreateRestoreState(
            poison == "package-folders" ? otherDirectory : packagesDirectory,
            poison == "packages-path" ? otherDirectory : packagesDirectory,
            poison == "config-paths" ? [configPath, Path.Combine(fixture.OutputDirectory, "poison.config")] : [configPath],
            poison == "fallback-folders" ? [otherDirectory] : []);
        var findings = new List<PackageConsumerSmokeFinding>();

        PackageConsumerSmokeRunner.ValidateRestoreState(
            assets.RootElement,
            packagesDirectory,
            configPath,
            findings);

        await Assert.That(findings.Select(static finding => finding.Code)).Contains(expectedCode);
    }

    [Test]
    public async Task FixtureBoundary_CopiesOnlyTheApprovedTopLevelManifest()
    {
        using var fixture = new RunnerFixture();
        var source = Path.Combine(fixture.OutputDirectory, "fixture");
        var workspace = Path.Combine(fixture.OutputDirectory, "workspace");
        CreateSyntheticFixture(source);
        var findings = new List<PackageConsumerSmokeFinding>();

        PackageConsumerSmokeRunner.ValidateFixtureAndCopy(source, workspace, CandidateVersion, findings);

        await Assert.That(findings).IsEmpty();
        await Assert.That(Directory.EnumerateFiles(workspace)
                .Select(static path => Path.GetFileName(path)!)
                .Order(StringComparer.Ordinal)
                .ToArray())
            .IsEquivalentTo(new[]
            {
                "DataLinq.PackageConsumer.csproj",
                "PackageConsumerModel.cs",
                "Program.cs",
                "README.md"
            });
        await Assert.That(Directory.EnumerateDirectories(workspace)).IsEmpty();
    }

    [Test]
    [Arguments("Directory.Build.props", false)]
    [Arguments("Directory.Build.targets", false)]
    [Arguments("Injected.cs", false)]
    [Arguments("linked-source", true)]
    public async Task FixtureBoundary_RejectsUnexpectedBuildInputs(string name, bool directory)
    {
        using var fixture = new RunnerFixture();
        var source = Path.Combine(fixture.OutputDirectory, "fixture");
        var workspace = Path.Combine(fixture.OutputDirectory, "workspace");
        CreateSyntheticFixture(source);
        if (directory)
            Directory.CreateDirectory(Path.Combine(source, name));
        else
            File.WriteAllText(Path.Combine(source, name), "poison", Encoding.UTF8);
        var findings = new List<PackageConsumerSmokeFinding>();

        PackageConsumerSmokeRunner.ValidateFixtureAndCopy(source, workspace, CandidateVersion, findings);

        await Assert.That(findings.Select(static finding => finding.Code)).Contains("fixture-unexpected-entry");
        await Assert.That(Directory.Exists(workspace)).IsFalse();
    }

    [Test]
    [Arguments("Import")]
    [Arguments("Reference")]
    [Arguments("Analyzer")]
    [Arguments("Compile")]
    [Arguments("CustomProperty")]
    [Arguments("Target")]
    [Arguments("PackageReference")]
    public async Task FixtureBoundary_RejectsProjectGraphEscape(string injection)
    {
        using var fixture = new RunnerFixture();
        var source = Path.Combine(fixture.OutputDirectory, "fixture");
        var workspace = Path.Combine(fixture.OutputDirectory, "workspace");
        CreateSyntheticFixture(source);
        InjectProjectEscape(Path.Combine(source, "DataLinq.PackageConsumer.csproj"), injection);
        var findings = new List<PackageConsumerSmokeFinding>();

        PackageConsumerSmokeRunner.ValidateFixtureAndCopy(source, workspace, CandidateVersion, findings);

        await Assert.That(findings).IsNotEmpty();
        await Assert.That(Directory.Exists(workspace)).IsFalse();
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [Arguments(false, false)]
    [Arguments(false, true)]
    public async Task OutputBoundary_RejectsEqualOrNestedSourcePath(bool fixtureSource, bool nested)
    {
        using var fixture = new RunnerFixture();
        var sourceRoot = Path.Combine(fixture.OutputDirectory, "sources");
        var fixtureDirectory = Path.Combine(sourceRoot, "fixture");
        var packageDirectory = Path.Combine(sourceRoot, "packages");
        Directory.CreateDirectory(fixtureDirectory);
        Directory.CreateDirectory(packageDirectory);
        var source = fixtureSource ? fixtureDirectory : packageDirectory;
        var output = nested ? Path.Combine(source, "report") : source;
        InvalidOperationException? caught = null;

        try
        {
            PackageConsumerSmokeRunner.ValidatePathBoundaries(fixtureDirectory, packageDirectory, output);
        }
        catch (InvalidOperationException exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains(fixtureSource ? "fixture directory" : "package directory");
        if (nested)
            await Assert.That(Directory.Exists(output)).IsFalse();
    }

    [Test]
    public async Task OutputBoundary_RejectsReparseTraversalWithoutTouchingTarget()
    {
        using var fixture = new RunnerFixture();
        var sourceRoot = Path.Combine(fixture.OutputDirectory, "sources");
        var fixtureDirectory = Path.Combine(sourceRoot, "fixture");
        var packageDirectory = Path.Combine(sourceRoot, "packages");
        var alias = Path.Combine(sourceRoot, "package-alias");
        var output = Path.Combine(alias, "report");
        Directory.CreateDirectory(fixtureDirectory);
        Directory.CreateDirectory(packageDirectory);

        try
        {
            CreateDirectoryLink(alias, packageDirectory);
            InvalidOperationException? caught = null;
            try
            {
                PackageConsumerSmokeRunner.ValidatePathBoundaries(fixtureDirectory, packageDirectory, output);
            }
            catch (InvalidOperationException exception)
            {
                caught = exception;
            }

            await Assert.That(caught).IsNotNull();
            await Assert.That(caught!.Message).Contains("reparse point");
            await Assert.That(Directory.Exists(Path.Combine(packageDirectory, "report"))).IsFalse();
        }
        finally
        {
            if (Directory.Exists(alias) &&
                (File.GetAttributes(alias) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(alias);
            }
        }
    }

    private static JsonDocument CreateRestoreState(
        string packageFolder,
        string packagesPath,
        string[] configFilePaths,
        string[] fallbackFolders)
    {
        var json = JsonSerializer.Serialize(new
        {
            packageFolders = new Dictionary<string, object?>
            {
                [packageFolder] = new { }
            },
            project = new
            {
                restore = new
                {
                    packagesPath,
                    configFilePaths,
                    fallbackFolders
                }
            }
        });
        return JsonDocument.Parse(json);
    }

    private static void CreateSyntheticFixture(string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var name in new[]
                 {
                     "DataLinq.PackageConsumer.csproj",
                     "PackageConsumerModel.cs",
                     "Program.cs",
                     "README.md"
                 })
        {
            File.Copy(Path.Combine(GetFixtureDirectory(), name), Path.Combine(destination, name));
        }
    }

    private static void InjectProjectEscape(string projectPath, string injection)
    {
        var document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var root = document.Root!;
        var propertyGroup = root.Elements().Single(static element => element.Name.LocalName == "PropertyGroup");
        var itemGroup = root.Elements().Single(static element => element.Name.LocalName == "ItemGroup");
        switch (injection)
        {
            case "Import":
                root.Add(new XElement("Import", new XAttribute("Project", "poison.targets")));
                break;
            case "Reference":
                itemGroup.Add(new XElement(
                    "Reference",
                    new XAttribute("Include", "DataLinq"),
                    new XElement("HintPath", "poison.dll")));
                break;
            case "Analyzer":
                itemGroup.Add(new XElement("Analyzer", new XAttribute("Include", "poison.dll")));
                break;
            case "Compile":
                itemGroup.Add(new XElement(
                    "Compile",
                    new XAttribute("Include", "..\\poison.cs"),
                    new XAttribute("Link", "poison.cs")));
                break;
            case "CustomProperty":
                propertyGroup.Add(new XElement("DirectoryBuildPropsPath", "poison.props"));
                break;
            case "Target":
                root.Add(new XElement(
                    "Target",
                    new XAttribute("Name", "Poison"),
                    new XAttribute("BeforeTargets", "CoreCompile"),
                    new XElement("Exec", new XAttribute("Command", "poison"))));
                break;
            case "PackageReference":
                itemGroup.Add(new XElement(
                    "PackageReference",
                    new XAttribute("Include", "Poison.Package"),
                    new XAttribute("Version", "[1.0.0]")));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(injection), injection, null);
        }

        document.Save(projectPath);
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

    private static string GetFixtureDirectory() =>
        Path.Combine(RepositoryRootLocator.Find(), "test-infra", "package-consumer");

    private static void WriteMinimalPackage(string path, string id, string version)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry($"{id}.nuspec");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{{id}}</id>
                <version>{{version}}</version>
                <authors>DataLinq</authors>
                <description>Minimal package-consumer test package.</description>
              </metadata>
            </package>
            """);
    }

    private sealed class RunnerFixture : IDisposable
    {
        private readonly string root = Path.Combine(
            AppContext.BaseDirectory,
            nameof(PackageConsumerSmokeTests),
            Guid.NewGuid().ToString("N"));

        public RunnerFixture()
        {
            PackageDirectory = Path.Combine(root, "packages");
            OutputDirectory = Path.Combine(root, "report");
        }

        public string PackageDirectory { get; }

        public string OutputDirectory { get; }

        public PackageConsumerSmokeReport Run()
        {
            var repositoryRoot = RepositoryRootLocator.Find();
            var options = new PackageConsumerSmokeOptions(
                repositoryRoot,
                PackageDirectory,
                OutputDirectory,
                CandidateVersion,
                ToolingProfile.Sandbox);
            return new PackageConsumerSmokeRunner(DevToolPaths.Create(repositoryRoot), options).CreateReport();
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
