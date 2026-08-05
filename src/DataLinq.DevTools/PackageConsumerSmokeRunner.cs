using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace DataLinq.DevTools;

public sealed class PackageConsumerSmokeRunner
{
    private const string SchemaVersion = "v0.9.package-consumer-smoke-report.v1";
    private const string ExecutionSchemaVersion = "v0.9.package-consumer-execution.v1";
    private const string FixtureRelativePath = "test-infra/package-consumer";
    private const string ProjectName = "DataLinq.PackageConsumer";

    private static readonly string[] RequiredFixtureFiles =
    [
        $"{ProjectName}.csproj",
        "PackageConsumerModel.cs",
        "Program.cs",
        "README.md"
    ];

    private static readonly IReadOnlyDictionary<string, string> RequiredFixtureProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OutputType"] = "Exe",
            ["TargetFrameworks"] = "net8.0;net9.0;net10.0",
            ["ImplicitUsings"] = "enable",
            ["Nullable"] = "enable",
            ["TreatWarningsAsErrors"] = "true",
            ["Deterministic"] = "true",
            ["IsPackable"] = "false",
            ["ManagePackageVersionsCentrally"] = "false"
        };

    private static readonly string[] RequiredPackageIds =
    [
        "DataLinq",
        "DataLinq.Memory",
        "DataLinq.SQLite",
        "DataLinq.MySql"
    ];

    private static readonly string[] RequiredTargetFrameworks =
    [
        "net8.0",
        "net9.0",
        "net10.0"
    ];

    private static readonly string[] ClearedInheritedEnvironmentVariables =
    [
        "MSBUILD_EXE_PATH",
        "MSBuildSDKsPath",
        "MSBuildExtensionsPath",
        "MSBuildExtensionsPath32",
        "MSBuildExtensionsPath64",
        "MSBuildUserExtensionsPath",
        "MSBUILDADDITIONALSDKRESOLVERSFOLDER",
        "AlternateCommonProps",
        "DirectoryBuildPropsPath",
        "DirectoryBuildTargetsPath",
        "DirectoryPackagesPropsPath",
        "CustomBeforeMicrosoftCommonProps",
        "CustomAfterMicrosoftCommonProps",
        "CustomBeforeMicrosoftCommonTargets",
        "CustomAfterMicrosoftCommonTargets",
        "CustomBeforeMicrosoftCommonCrossTargetingTargets",
        "CustomAfterMicrosoftCommonCrossTargetingTargets",
        "CustomBeforeMicrosoftCSharpTargets",
        "CustomAfterMicrosoftCSharpTargets",
        "NuGetRestoreTargets",
        "RestoreConfigFile",
        "RestorePackagesPath",
        "RestoreRepositoryPath",
        "RestoreOutputPath",
        "RestoreSources",
        "RestoreAdditionalProjectSources",
        "RestoreFallbackFolders",
        "RestoreAdditionalProjectFallbackFolders",
        "RestoreAdditionalProjectFallbackFoldersExcludes",
        "RestoreDisablePackageSourceMapping",
        "NUGET_PACKAGES",
        "NUGET_HTTP_CACHE_PATH",
        "NUGET_SCRATCH",
        "NUGET_FALLBACK_PACKAGES",
        "NUGET_PLUGIN_PATHS",
        "NUGET_CREDENTIALPROVIDERS_PATH",
        "ArtifactsPath",
        "UseArtifactsOutput",
        "BaseOutputPath",
        "BaseIntermediateOutputPath",
        "OutputPath",
        "OutDir",
        "IntermediateOutputPath",
        "MSBuildProjectExtensionsPath",
        "ProjectAssetsFile",
        "ProjectAssetsCacheFile",
        "NuGetPackageRoot",
        "NuGetPackageFolders",
        "ReferencePath",
        "AssemblySearchPaths",
        "CscToolPath",
        "CscToolExe",
        "RoslynTargetsPath",
        "CompilerGeneratedFilesOutputPath"
    ];

    private readonly DevToolPaths paths;
    private readonly PackageConsumerSmokeOptions options;

    public PackageConsumerSmokeRunner(DevToolPaths paths, PackageConsumerSmokeOptions options)
    {
        this.paths = paths;
        this.options = options;
    }

    public PackageConsumerSmokeReport CreateReport()
    {
        var repositoryRoot = Path.GetFullPath(options.RepositoryRoot);
        var packageDirectory = Path.GetFullPath(options.PackageDirectory);
        var reportDirectory = Path.GetFullPath(options.OutputDirectory);
        var fixtureDirectory = Path.Combine(repositoryRoot, "test-infra", "package-consumer");
        var workspaceDirectory = Path.Combine(reportDirectory, "workspace");
        var buildDirectory = Path.Combine(reportDirectory, ".artifacts");
        var projectExtensionsDirectory = Path.Combine(buildDirectory, "obj", ProjectName);
        var projectAssetsPath = Path.Combine(projectExtensionsDirectory, "project.assets.json");
        var generatedDirectory = Path.Combine(buildDirectory, "generated");
        var packagesCacheDirectory = Path.Combine(reportDirectory, ".nuget", "packages");
        var httpCacheDirectory = Path.Combine(reportDirectory, ".nuget", "http-cache");
        var tempDirectory = Path.Combine(reportDirectory, ".tmp");
        var logsDirectory = Path.Combine(reportDirectory, "logs");
        var nugetConfigPath = Path.Combine(reportDirectory, "NuGet.Config");
        var findings = new List<PackageConsumerSmokeFinding>();
        var candidates = new List<PackageConsumerCandidatePackage>();
        var commands = new List<PackageConsumerCommandReport>();
        var resolvedPackages = new List<PackageConsumerResolvedPackage>();
        PackageConsumerExecutionReport? execution = null;
        var generatedSource = new PackageConsumerGeneratedSourceReport(false, false, []);

        ValidatePathBoundaries(fixtureDirectory, packageDirectory, reportDirectory);
        PrepareEmptyReportDirectory(reportDirectory);
        RejectReparsePointTraversal(reportDirectory, "output directory");
        Directory.CreateDirectory(logsDirectory);

        try
        {
            candidates.AddRange(InspectCandidates(packageDirectory, options.Version, findings));
            ValidateRequiredCandidates(candidates, options.Version, findings);
            ValidateFixtureAndCopy(fixtureDirectory, workspaceDirectory, options.Version, findings);

            if (!HasErrors(findings))
            {
                Directory.CreateDirectory(packagesCacheDirectory);
                Directory.CreateDirectory(httpCacheDirectory);
                Directory.CreateDirectory(tempDirectory);
                WriteNugetConfig(nugetConfigPath, packageDirectory);

                var projectPath = Path.Combine(workspaceDirectory, $"{ProjectName}.csproj");
                var commandPaths = paths with { ArtifactRoot = logsDirectory };
                var commandRunner = new DotnetCommandRunner(commandPaths, options.Profile);
                var environment = CreateIsolatedEnvironment(
                    reportDirectory,
                    packagesCacheDirectory,
                    httpCacheDirectory,
                    tempDirectory);
                var restoreIsolationArguments = CreateRestoreIsolationArguments(
                    buildDirectory,
                    projectExtensionsDirectory,
                    packagesCacheDirectory,
                    nugetConfigPath);
                var buildIsolationArguments = CreateBuildIsolationArguments(
                    buildDirectory,
                    projectExtensionsDirectory,
                    projectAssetsPath,
                    packagesCacheDirectory);

                var restore = commandRunner.Execute(
                    DotnetCommandType.Restore,
                    [
                        "restore",
                        projectPath,
                        "--configfile",
                        nugetConfigPath,
                        "--no-http-cache",
                        "--force-evaluate",
                        "-v",
                        "minimal",
                        $"-p:DataLinqCandidateVersion={options.Version}",
                        .. restoreIsolationArguments
                    ],
                    "package-consumer-restore",
                    ProjectName,
                    workingDirectory: workspaceDirectory,
                    additionalEnvironmentVariables: environment);
                commands.Add(ToCommandReport("restore", restore, workspaceDirectory));

                if (restore.ProcessResult.ExitCode != 0)
                {
                    AddError(findings, "restore-failed", restore.Analysis.FailureSummary ??
                        $"Package consumer restore exited with code {restore.ProcessResult.ExitCode}.");
                }
                else
                {
                    InspectRestoreProvenance(
                        projectAssetsPath,
                        packagesCacheDirectory,
                        packageDirectory,
                        nugetConfigPath,
                        options.Version,
                        candidates,
                        resolvedPackages,
                        findings);
                }

                var successfulBuilds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!HasErrors(findings))
                {
                    foreach (var targetFramework in RequiredTargetFrameworks)
                    {
                        var build = commandRunner.Execute(
                            DotnetCommandType.Build,
                            [
                                "build",
                                projectPath,
                                "-f",
                                targetFramework,
                                "-c",
                                "Release",
                                "--no-restore",
                                "-v",
                                "minimal",
                                $"-p:DataLinqCandidateVersion={options.Version}",
                                $"-p:EmitCompilerGeneratedFiles=true",
                                $"-p:CompilerGeneratedFilesOutputPath={Path.Combine(generatedDirectory, targetFramework)}",
                                .. buildIsolationArguments
                            ],
                            $"package-consumer-build-{targetFramework.Replace('.', '-')}",
                            $"{ProjectName} ({targetFramework})",
                            workingDirectory: workspaceDirectory,
                            additionalEnvironmentVariables: environment);
                        commands.Add(ToCommandReport($"build-{targetFramework}", build, workspaceDirectory));

                        if (build.ProcessResult.ExitCode == 0)
                            successfulBuilds.Add(targetFramework);
                        else
                            AddError(findings, $"build-{targetFramework}-failed", build.Analysis.FailureSummary ??
                                $"Package consumer build for {targetFramework} exited with code {build.ProcessResult.ExitCode}.");
                    }

                    generatedSource = InspectGeneratedSource(generatedDirectory);
                    if (!generatedSource.Passed)
                    {
                        AddError(
                            findings,
                            "generated-source-missing",
                            "Generated C# output did not contain both MutablePackageConsumerRow and PackageConsumerDatabase.");
                    }

                    if (successfulBuilds.Contains("net10.0"))
                    {
                        var executableDll = FindExecutableDll(buildDirectory);
                        if (executableDll is null)
                        {
                            AddError(findings, "net10-executable-missing", "Could not locate the built net10.0 consumer DLL.");
                        }
                        else
                        {
                            var run = commandRunner.Execute(
                                DotnetCommandType.Exec,
                                [executableDll],
                                "package-consumer-run-net10",
                                $"{ProjectName} (net10.0)",
                                includeNoLogo: false,
                                includeNuGetAuditProperty: false,
                                includeOfflineRestoreProperty: false,
                                workingDirectory: Path.GetDirectoryName(executableDll),
                                additionalEnvironmentVariables: environment);
                            commands.Add(ToCommandReport("run-net10.0", run, Path.GetDirectoryName(executableDll)!));
                            execution = ParseAndValidateExecution(run.ProcessResult, findings);
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (IsReportable(exception))
        {
            AddError(findings, "runner-exception", $"{exception.GetType().Name}: {exception.Message}");
            TryWriteExceptionLog(logsDirectory, exception);
        }

        var report = CreateReportModel(
            repositoryRoot,
            packageDirectory,
            fixtureDirectory,
            workspaceDirectory,
            reportDirectory,
            nugetConfigPath,
            packagesCacheDirectory,
            candidates,
            commands,
            resolvedPackages,
            execution,
            generatedSource,
            findings);
        WriteReportArtifacts(report);
        return report;
    }

    public static string ToMarkdown(PackageConsumerSmokeReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Package Consumer Smoke Report");
        builder.AppendLine();
        builder.AppendLine($"Generated UTC: {report.GeneratedAtUtc:O}");
        builder.AppendLine($"Candidate: `{report.PackageDirectory}` at `{report.Version}`");
        builder.AppendLine($"Outcome: **{(report.Summary.HasHardFailures ? "failed" : "passed")}**");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- required direct packages: {report.Summary.RequiredPackageCount}");
        builder.AppendLine($"- candidate packages inspected: {report.Summary.CandidatePackageCount}");
        builder.AppendLine($"- exact packages resolved: {report.Summary.ResolvedPackageCount}");
        builder.AppendLine($"- restore: {(report.Summary.RestoreSucceeded ? "passed" : "failed")}");
        builder.AppendLine($"- builds: {report.Summary.SuccessfulBuildCount}/{report.Summary.BuildCount} passed");
        builder.AppendLine($"- generated source: {(report.Summary.GeneratedSourceVerified ? "verified" : "not verified")}");
        builder.AppendLine($"- net10 execution: {(report.Summary.ExecutionSucceeded ? "passed" : "failed")}");
        builder.AppendLine($"- hard failures: {report.Summary.HardFailureCount}");
        builder.AppendLine();
        builder.AppendLine("## Candidate Packages");
        builder.AppendLine();
        builder.AppendLine("| Package | Version | SHA-256 |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (var package in report.CandidatePackages)
            builder.AppendLine($"| `{Escape(package.Id)}` | `{Escape(package.Version)}` | `{package.Sha256}` |");

        builder.AppendLine();
        builder.AppendLine("## Commands");
        builder.AppendLine();
        builder.AppendLine("| Command | Result | Exit | Log |");
        builder.AppendLine("| --- | --- | ---: | --- |");
        foreach (var command in report.Commands)
        {
            builder.AppendLine(
                $"| `{Escape(command.Name)}` | {(command.Succeeded ? "passed" : "failed")} | " +
                $"{command.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "-"} | `{Escape(command.RawLogPath ?? "-")}` |");
        }

        builder.AppendLine();
        builder.AppendLine("## Resolved Package Provenance");
        builder.AppendLine();
        builder.AppendLine("| Resolved package | Version | Local source | SHA-256 match |");
        builder.AppendLine("| --- | --- | --- | --- |");
        foreach (var package in report.ResolvedPackages)
        {
            builder.AppendLine(
                $"| `{Escape(package.Id)}` | `{Escape(package.Version)}` | " +
                $"{(package.SourceMatchesCandidateDirectory ? "yes" : "no")} | {(package.HashMatchesCandidate ? "yes" : "no")} |");
        }

        if (report.Findings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Findings");
            builder.AppendLine();
            foreach (var finding in report.Findings)
                builder.AppendLine($"- `{finding.Severity}` `{Escape(finding.Code)}`: {finding.Message}");
        }

        return builder.ToString();
    }

    private PackageConsumerSmokeReport CreateReportModel(
        string repositoryRoot,
        string packageDirectory,
        string fixtureDirectory,
        string workspaceDirectory,
        string reportDirectory,
        string nugetConfigPath,
        string packagesCacheDirectory,
        IReadOnlyList<PackageConsumerCandidatePackage> candidates,
        IReadOnlyList<PackageConsumerCommandReport> commands,
        IReadOnlyList<PackageConsumerResolvedPackage> resolvedPackages,
        PackageConsumerExecutionReport? execution,
        PackageConsumerGeneratedSourceReport generatedSource,
        IReadOnlyList<PackageConsumerSmokeFinding> findings)
    {
        var buildCommands = commands.Where(static command => command.Name.StartsWith("build-", StringComparison.Ordinal)).ToArray();
        var hardFailureCount = findings.Count(static finding => finding.Severity == PackageConsumerSmokeFindingSeverity.Error);
        var summary = new PackageConsumerSmokeSummary(
            RequiredPackageIds.Length,
            candidates.Count,
            resolvedPackages.Count,
            buildCommands.Length,
            buildCommands.Count(static command => command.Succeeded),
            commands.Any(static command => command.Name == "restore" && command.Succeeded),
            execution?.ContractValidated == true,
            generatedSource.Passed,
            findings.Count,
            hardFailureCount,
            hardFailureCount > 0);

        return new PackageConsumerSmokeReport(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            repositoryRoot,
            packageDirectory,
            options.Version,
            options.Profile,
            fixtureDirectory,
            workspaceDirectory,
            reportDirectory,
            nugetConfigPath,
            packagesCacheDirectory,
            candidates,
            commands,
            resolvedPackages,
            execution,
            generatedSource,
            findings,
            summary);
    }

    private static IReadOnlyList<PackageConsumerCandidatePackage> InspectCandidates(
        string packageDirectory,
        string requestedVersion,
        ICollection<PackageConsumerSmokeFinding> findings)
    {
        if (!Directory.Exists(packageDirectory))
        {
            AddError(findings, "candidate-directory-missing", $"Candidate package directory '{packageDirectory}' does not exist.");
            return [];
        }

        var result = new List<PackageConsumerCandidatePackage>();
        foreach (var packagePath in Directory.EnumerateFiles(packageDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
                     .Where(static path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var (id, version) = ReadNuspecIdentity(packagePath);
                if (!id.Equals("DataLinq", StringComparison.OrdinalIgnoreCase) &&
                    !id.StartsWith("DataLinq.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var identityMatches = version.Equals(requestedVersion, StringComparison.OrdinalIgnoreCase);
                result.Add(new PackageConsumerCandidatePackage(
                    id,
                    version,
                    Path.GetFullPath(packagePath),
                    new FileInfo(packagePath).Length,
                    ComputeSha256(packagePath),
                    identityMatches));

                if (!identityMatches)
                {
                    AddError(
                        findings,
                        "candidate-version-mismatch",
                        $"Candidate package '{id}' has version '{version}', expected exact version '{requestedVersion}'.");
                }
            }
            catch (Exception exception) when (IsReportable(exception))
            {
                AddError(findings, "candidate-invalid", $"Could not inspect '{packagePath}': {exception.Message}");
            }
        }

        return result;
    }

    private static void ValidateRequiredCandidates(
        IReadOnlyList<PackageConsumerCandidatePackage> candidates,
        string version,
        ICollection<PackageConsumerSmokeFinding> findings)
    {
        foreach (var id in RequiredPackageIds)
        {
            var matches = candidates.Where(package =>
                package.Id.Equals(id, StringComparison.OrdinalIgnoreCase) &&
                package.Version.Equals(version, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
            {
                AddError(
                    findings,
                    matches.Length == 0 ? "candidate-required-package-missing" : "candidate-required-package-duplicate",
                    $"Expected exactly one '{id}' package at '{version}', found {matches.Length}.");
            }
        }
    }

    internal static void ValidateFixtureAndCopy(
        string fixtureDirectory,
        string workspaceDirectory,
        string version,
        ICollection<PackageConsumerSmokeFinding> findings)
    {
        if (!Directory.Exists(fixtureDirectory))
        {
            AddError(findings, "fixture-missing", $"Tracked fixture '{FixtureRelativePath}' does not exist.");
            return;
        }

        try
        {
            RejectReparsePointTraversal(fixtureDirectory, "fixture directory");
        }
        catch (InvalidOperationException exception)
        {
            AddError(findings, "fixture-reparse-point", exception.Message);
            return;
        }

        var ignoredDirectories = new HashSet<string>(["bin", "obj", ".vs"], StringComparer.OrdinalIgnoreCase);
        var entries = Directory.EnumerateFileSystemEntries(fixtureDirectory, "*", SearchOption.TopDirectoryOnly).ToArray();
        var unexpectedEntries = new List<string>();
        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (Directory.Exists(entry) && ignoredDirectories.Contains(name))
                continue;
            if (File.Exists(entry) && RequiredFixtureFiles.Contains(name, StringComparer.Ordinal))
                continue;
            unexpectedEntries.Add(name);
        }

        foreach (var name in RequiredFixtureFiles)
        {
            var path = Path.Combine(fixtureDirectory, name);
            if (!File.Exists(path))
            {
                AddError(findings, "fixture-file-missing", $"Tracked fixture file '{name}' is missing.");
                continue;
            }

            try
            {
                RejectReparsePointTraversal(path, $"fixture file '{name}'");
            }
            catch (InvalidOperationException exception)
            {
                AddError(findings, "fixture-reparse-point", exception.Message);
            }
        }

        foreach (var name in unexpectedEntries.Order(StringComparer.OrdinalIgnoreCase))
        {
            AddError(
                findings,
                "fixture-unexpected-entry",
                $"Tracked fixture contains unexpected top-level entry '{name}'; only the fixed build-input manifest is allowed.");
        }

        var projectPath = Path.Combine(fixtureDirectory, $"{ProjectName}.csproj");
        if (File.Exists(projectPath))
            ValidateFixtureProject(projectPath, version, findings);

        if (HasErrors(findings))
            return;

        Directory.CreateDirectory(workspaceDirectory);
        foreach (var name in RequiredFixtureFiles)
            File.Copy(Path.Combine(fixtureDirectory, name), Path.Combine(workspaceDirectory, name), overwrite: false);
    }

    private static void ValidateFixtureProject(
        string projectPath,
        string version,
        ICollection<PackageConsumerSmokeFinding> findings)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception) when (IsReportable(exception))
        {
            AddError(findings, "fixture-project-invalid", $"Could not parse the fixture project: {exception.Message}");
            return;
        }

        var project = document.Root;
        if (project is null || project.Name.LocalName != "Project" ||
            project.Attributes().Count() != 1 ||
            (string?)project.Attribute("Sdk") != "Microsoft.NET.Sdk")
        {
            AddError(findings, "fixture-project-shape", "Fixture root must be exactly <Project Sdk=\"Microsoft.NET.Sdk\">.");
            return;
        }

        var rootChildren = project.Elements().ToArray();
        if (rootChildren.Length != 3 ||
            rootChildren[0].Name.LocalName != "PropertyGroup" ||
            rootChildren[1].Name.LocalName != "ItemGroup" ||
            rootChildren[2].Name.LocalName != "Target" ||
            rootChildren[0].HasAttributes ||
            rootChildren[1].HasAttributes)
        {
            AddError(
                findings,
                "fixture-project-shape",
                "Fixture project must contain only the approved PropertyGroup, PackageReference ItemGroup, and version guard Target.");
            return;
        }

        var properties = rootChildren[0].Elements().ToArray();
        if (properties.Length != RequiredFixtureProperties.Count ||
            properties.Any(static property => property.HasAttributes || property.HasElements) ||
            properties.GroupBy(static property => property.Name.LocalName, StringComparer.Ordinal).Any(static group => group.Count() != 1))
        {
            AddError(findings, "fixture-project-properties", "Fixture project properties do not match the approved standalone consumer shape.");
        }
        else
        {
            foreach (var expected in RequiredFixtureProperties)
            {
                var actual = properties.SingleOrDefault(property => property.Name.LocalName == expected.Key)?.Value.Trim();
                if (!string.Equals(actual, expected.Value, StringComparison.Ordinal))
                {
                    AddError(
                        findings,
                        "fixture-project-properties",
                        $"Fixture property '{expected.Key}' must be exactly '{expected.Value}'.");
                }
            }
        }

        var packageReferences = rootChildren[1].Elements().ToArray();
        if (packageReferences.Length != RequiredPackageIds.Length ||
            packageReferences.Any(reference =>
                reference.Name.LocalName != "PackageReference" ||
                reference.HasElements ||
                !string.IsNullOrWhiteSpace(reference.Value) ||
                reference.Attributes().Count() != 2 ||
                reference.Attribute("Include") is null ||
                reference.Attribute("Version") is null))
        {
            AddError(
                findings,
                "fixture-package-reference",
                "Fixture must contain only the four approved direct PackageReference items with Include and Version attributes.");
        }
        else
        {
            foreach (var id in RequiredPackageIds)
            {
                var matches = packageReferences.Where(reference =>
                    string.Equals((string?)reference.Attribute("Include"), id, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1 ||
                    (string?)matches[0].Attribute("Version") != "[$(DataLinqCandidateVersion)]")
                {
                    AddError(
                        findings,
                        "fixture-package-reference",
                        $"Fixture must reference '{id}' exactly once with version '[$(DataLinqCandidateVersion)]' for candidate '{version}'.");
                }
            }
        }

        var target = rootChildren[2];
        var targetAttributes = target.Attributes().ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
        var targetChildren = target.Elements().ToArray();
        if (targetAttributes.Count != 2 ||
            !targetAttributes.TryGetValue("Name", out var targetName) || targetName != "RequireDataLinqCandidateVersion" ||
            !targetAttributes.TryGetValue("BeforeTargets", out var beforeTargets) || beforeTargets != "CollectPackageReferences;PrepareForBuild" ||
            targetChildren.Length != 1 ||
            targetChildren[0].Name.LocalName != "Error" ||
            targetChildren[0].HasElements ||
            !string.IsNullOrWhiteSpace(targetChildren[0].Value))
        {
            AddError(findings, "fixture-version-guard", "Fixture version guard Target does not match the approved fail-closed shape.");
            return;
        }

        var errorAttributes = targetChildren[0].Attributes()
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
        if (errorAttributes.Count != 2 ||
            !errorAttributes.TryGetValue("Condition", out var condition) || condition != "'$(DataLinqCandidateVersion)' == ''" ||
            !errorAttributes.TryGetValue("Text", out var text) || text != "DataLinqCandidateVersion must be supplied explicitly.")
        {
            AddError(findings, "fixture-version-guard", "Fixture version guard must require DataLinqCandidateVersion explicitly.");
        }
    }

    internal static IReadOnlyList<string> CreateRestoreIsolationArguments(
        string buildDirectory,
        string projectExtensionsDirectory,
        string packagesCacheDirectory,
        string nugetConfigPath) =>
    [
        "-noAutoResponse",
        "--packages",
        packagesCacheDirectory,
        $"--artifacts-path={buildDirectory}",
        $"-p:MSBuildProjectExtensionsPath={projectExtensionsDirectory}",
        $"-p:RestoreOutputPath={projectExtensionsDirectory}",
        $"-p:RestoreConfigFile={nugetConfigPath}",
        "-p:RestoreDisablePackageSourceMapping=false",
        "-p:ImportDirectoryBuildProps=false",
        "-p:ImportDirectoryBuildTargets=false",
        "-p:ImportDirectoryPackagesProps=false",
        "-p:ManagePackageVersionsCentrally=false"
    ];

    internal static IReadOnlyList<string> CreateBuildIsolationArguments(
        string buildDirectory,
        string projectExtensionsDirectory,
        string projectAssetsPath,
        string packagesCacheDirectory) =>
    [
        "-noAutoResponse",
        $"--artifacts-path={buildDirectory}",
        $"-p:MSBuildProjectExtensionsPath={projectExtensionsDirectory}",
        $"-p:ProjectAssetsFile={projectAssetsPath}",
        $"-p:NuGetPackageRoot={packagesCacheDirectory}",
        $"-p:NuGetPackageFolders={packagesCacheDirectory}",
        $"-p:RestorePackagesPath={packagesCacheDirectory}",
        "-p:ImportDirectoryBuildProps=false",
        "-p:ImportDirectoryBuildTargets=false",
        "-p:ImportDirectoryPackagesProps=false",
        "-p:ManagePackageVersionsCentrally=false"
    ];

    private static void InspectRestoreProvenance(
        string projectAssetsPath,
        string packagesCacheDirectory,
        string candidateDirectory,
        string nugetConfigPath,
        string version,
        IReadOnlyList<PackageConsumerCandidatePackage> candidates,
        ICollection<PackageConsumerResolvedPackage> resolvedPackages,
        ICollection<PackageConsumerSmokeFinding> findings)
    {
        if (!File.Exists(projectAssetsPath))
        {
            AddError(findings, "assets-file-missing", $"Expected isolated project assets file '{projectAssetsPath}'.");
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(projectAssetsPath, Encoding.UTF8));
        if (!document.RootElement.TryGetProperty("libraries", out var libraries))
        {
            AddError(findings, "assets-libraries-missing", "project.assets.json does not contain a libraries object.");
            return;
        }

        ValidateRestoreState(document.RootElement, packagesCacheDirectory, nugetConfigPath, findings);
        ValidateAssetsTargets(document.RootElement, version, findings);

        foreach (var library in libraries.EnumerateObject())
        {
            var libraryType = library.Value.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            if (string.Equals(libraryType, "project", StringComparison.OrdinalIgnoreCase))
            {
                AddError(
                    findings,
                    "assets-project-library",
                    $"project.assets.json contains project library '{library.Name}'; package-only evidence cannot contain project libraries.");
            }

            var separator = library.Name.LastIndexOf('/');
            if (separator <= 0)
                continue;
            var id = library.Name[..separator];
            var resolvedVersion = library.Name[(separator + 1)..];
            if (!id.Equals("DataLinq", StringComparison.OrdinalIgnoreCase) &&
                !id.StartsWith("DataLinq.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(libraryType, "package", StringComparison.OrdinalIgnoreCase))
            {
                AddError(
                    findings,
                    "datalinq-library-not-package",
                    $"DataLinq assets library '{library.Name}' has type '{libraryType ?? "missing"}', expected 'package'.");
            }

            var candidate = candidates.SingleOrDefault(package =>
                package.Id.Equals(id, StringComparison.OrdinalIgnoreCase) &&
                package.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
            var relativeCachePath = library.Value.TryGetProperty("path", out var pathElement)
                ? pathElement.GetString()
                : $"{id.ToLowerInvariant()}/{resolvedVersion.ToLowerInvariant()}";
            var cacheDirectory = Path.Combine(
                packagesCacheDirectory,
                (relativeCachePath ?? "").Replace('/', Path.DirectorySeparatorChar));
            var metadataPath = Path.Combine(cacheDirectory, ".nupkg.metadata");
            var source = ReadMetadataSource(metadataPath);
            var cachedPackagePath = Directory.Exists(cacheDirectory)
                ? Directory.EnumerateFiles(cacheDirectory, "*.nupkg", SearchOption.TopDirectoryOnly).SingleOrDefault() ??
                  Path.Combine(cacheDirectory, $"{id.ToLowerInvariant()}.{resolvedVersion.ToLowerInvariant()}.nupkg")
                : Path.Combine(cacheDirectory, $"{id.ToLowerInvariant()}.{resolvedVersion.ToLowerInvariant()}.nupkg");
            var cachedSha = File.Exists(cachedPackagePath) ? ComputeSha256(cachedPackagePath) : null;
            var exactVersion = resolvedVersion.Equals(version, StringComparison.OrdinalIgnoreCase);
            var sourceMatches = SourceMatchesDirectory(source, candidateDirectory);
            var hashMatches = candidate is not null && cachedSha is not null &&
                              cachedSha.Equals(candidate.Sha256, StringComparison.OrdinalIgnoreCase);

            resolvedPackages.Add(new PackageConsumerResolvedPackage(
                id,
                resolvedVersion,
                library.Name,
                cacheDirectory,
                metadataPath,
                source,
                cachedPackagePath,
                candidate?.PackagePath ?? "",
                candidate?.Sha256 ?? "",
                cachedSha,
                exactVersion,
                sourceMatches,
                hashMatches));

            if (!exactVersion || candidate is null || !File.Exists(metadataPath) || !sourceMatches || !hashMatches)
            {
                AddError(
                    findings,
                    "package-provenance-mismatch",
                    $"Resolved '{id}/{resolvedVersion}' was not proven to be the exact selected candidate by assets, metadata source, and cached nupkg SHA-256.");
            }
        }

        foreach (var id in RequiredPackageIds)
        {
            if (resolvedPackages.Count(package => package.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) != 1)
                AddError(findings, "resolved-required-package-count", $"Expected one resolved '{id}' package in project.assets.json.");
        }
    }

    internal static void ValidateRestoreState(
        JsonElement root,
        string packagesCacheDirectory,
        string nugetConfigPath,
        ICollection<PackageConsumerSmokeFinding> findings)
    {
        if (!root.TryGetProperty("packageFolders", out var packageFolders) ||
            packageFolders.ValueKind != JsonValueKind.Object)
        {
            AddError(findings, "assets-package-folders-missing", "project.assets.json does not contain packageFolders.");
        }
        else
        {
            var folders = packageFolders.EnumerateObject().Select(static property => property.Name).ToArray();
            if (folders.Length != 1 || !PathsEqual(folders[0], packagesCacheDirectory))
            {
                AddError(
                    findings,
                    "assets-package-folders-mismatch",
                    "project.assets.json must use exactly the isolated report-local package cache.");
            }
        }

        if (!root.TryGetProperty("project", out var project) ||
            !project.TryGetProperty("restore", out var restore))
        {
            AddError(findings, "assets-restore-state-missing", "project.assets.json does not contain project.restore state.");
            return;
        }

        var packagesPath = restore.TryGetProperty("packagesPath", out var packagesPathElement)
            ? packagesPathElement.GetString()
            : null;
        if (packagesPath is null || !PathsEqual(packagesPath, packagesCacheDirectory))
        {
            AddError(
                findings,
                "assets-packages-path-mismatch",
                "project.restore.packagesPath does not match the isolated report-local package cache.");
        }

        if (!restore.TryGetProperty("configFilePaths", out var configPaths) ||
            configPaths.ValueKind != JsonValueKind.Array)
        {
            AddError(findings, "assets-config-paths-missing", "project.restore.configFilePaths is missing.");
        }
        else
        {
            var paths = configPaths.EnumerateArray().Select(static path => path.GetString()).ToArray();
            if (paths.Length != 1 || paths[0] is null || !PathsEqual(paths[0]!, nugetConfigPath))
            {
                AddError(
                    findings,
                    "assets-config-paths-mismatch",
                    "project.restore.configFilePaths must contain only the generated report-local NuGet.Config.");
            }
        }

        if (restore.TryGetProperty("fallbackFolders", out var fallbackFolders) &&
            (fallbackFolders.ValueKind != JsonValueKind.Array || fallbackFolders.GetArrayLength() != 0))
        {
            AddError(
                findings,
                "assets-fallback-folders",
                "project.restore must not contain fallback package folders.");
        }
    }

    private static PackageConsumerGeneratedSourceReport InspectGeneratedSource(string generatedDirectory)
    {
        if (!Directory.Exists(generatedDirectory))
            return new PackageConsumerGeneratedSourceReport(false, false, []);

        var mutableFound = false;
        var databaseFound = false;
        var matches = new List<string>();
        foreach (var path in Directory.EnumerateFiles(generatedDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var mutableMatch = text.Contains("MutablePackageConsumerRow", StringComparison.Ordinal);
            var databaseMatch = text.Contains("PackageConsumerDatabase", StringComparison.Ordinal);
            if (!mutableMatch && !databaseMatch)
                continue;
            mutableFound |= mutableMatch;
            databaseFound |= databaseMatch;
            matches.Add(Path.GetFullPath(path));
        }

        return new PackageConsumerGeneratedSourceReport(
            mutableFound,
            databaseFound,
            matches.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static PackageConsumerExecutionReport? ParseAndValidateExecution(
        ExternalCommandResult result,
        ICollection<PackageConsumerSmokeFinding> findings)
    {
        var line = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(static value => value.StartsWith('{') && value.EndsWith('}'));
        if (line is null)
        {
            AddError(findings, "execution-json-missing", "Consumer execution did not emit a final JSON object.");
            return null;
        }

        PackageConsumerExecutionReport? execution;
        try
        {
            execution = JsonSerializer.Deserialize<PackageConsumerExecutionReport>(line, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException exception)
        {
            AddError(findings, "execution-json-invalid", $"Consumer execution JSON is invalid: {exception.Message}");
            return null;
        }

        if (execution is null)
        {
            AddError(findings, "execution-json-empty", "Consumer execution JSON deserialized to null.");
            return null;
        }

        var valid = IsExecutionContractValid(execution, result.ExitCode);
        execution = execution with
        {
            ContractValidated = valid,
            RawJson = line
        };
        if (!valid)
        {
            AddError(
                findings,
                "execution-contract-failed",
                "net10.0 execution did not satisfy the exact Memory, SQLite, MySQL compilation-probe, and aggregate result contract.");
        }

        return execution;
    }

    internal static bool IsExecutionContractValid(PackageConsumerExecutionReport execution, int exitCode) =>
            exitCode == 0 &&
            execution.SchemaVersion == ExecutionSchemaVersion &&
            execution.TargetFramework == "net10.0" &&
            execution.Passed &&
            execution.Memory.Passed &&
            execution.Memory.FoundId == 17 &&
            execution.Memory.Missing &&
            execution.Memory.QueryIds.SequenceEqual([-5, 17]) &&
            execution.Sqlite.Passed &&
            execution.Sqlite.RowIds.SequenceEqual([-5, 17, 42]) &&
            execution.MySqlCompilationProbe;

    internal static IReadOnlyDictionary<string, string?> CreateIsolatedEnvironment(
        string reportDirectory,
        string packagesCacheDirectory,
        string httpCacheDirectory,
        string tempDirectory)
    {
        var cliHome = Path.Combine(reportDirectory, ".dotnet-home");
        var appData = Path.Combine(reportDirectory, ".appdata");
        var localAppData = Path.Combine(reportDirectory, ".localappdata");
        var msbuildUserExtensions = Path.Combine(reportDirectory, ".msbuild-user");
        Directory.CreateDirectory(cliHome);
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(localAppData);
        Directory.CreateDirectory(msbuildUserExtensions);

        var environment = ClearedInheritedEnvironmentVariables.ToDictionary(
            static name => name,
            static _ => (string?)null,
            StringComparer.OrdinalIgnoreCase);
        environment["NUGET_PACKAGES"] = packagesCacheDirectory;
        environment["NUGET_HTTP_CACHE_PATH"] = httpCacheDirectory;
        environment["NUGET_SCRATCH"] = Path.Combine(tempDirectory, "nuget-scratch");
        environment["RestoreConfigFile"] = Path.Combine(reportDirectory, "NuGet.Config");
        environment["MSBuildUserExtensionsPath"] = msbuildUserExtensions;
        environment["TMP"] = tempDirectory;
        environment["TEMP"] = tempDirectory;
        environment["DOTNET_CLI_HOME"] = cliHome;
        environment["APPDATA"] = appData;
        environment["LOCALAPPDATA"] = localAppData;
        environment["HOME"] = cliHome;
        environment["USERPROFILE"] = cliHome;
        return environment;
    }

    private static void WriteNugetConfig(string path, string candidateDirectory)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("configuration",
                new XElement("packageSources",
                    new XElement("clear"),
                    new XElement("add", new XAttribute("key", "DataLinq candidate"), new XAttribute("value", candidateDirectory)),
                    new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"), new XAttribute("protocolVersion", "3"))),
                new XElement("packageSourceMapping",
                    new XElement("packageSource", new XAttribute("key", "DataLinq candidate"),
                        new XElement("package", new XAttribute("pattern", "DataLinq*"))),
                    new XElement("packageSource", new XAttribute("key", "nuget.org"),
                        new XElement("package", new XAttribute("pattern", "*"))))));
        document.Save(path);
    }

    private static void PrepareEmptyReportDirectory(string reportDirectory)
    {
        if (Directory.Exists(reportDirectory) && Directory.EnumerateFileSystemEntries(reportDirectory).Any())
            throw new InvalidOperationException($"Package consumer output directory '{reportDirectory}' must be empty.");
        Directory.CreateDirectory(reportDirectory);
    }

    internal static void ValidatePathBoundaries(
        string fixtureDirectory,
        string packageDirectory,
        string reportDirectory)
    {
        RejectReparsePointTraversal(fixtureDirectory, "fixture directory");
        RejectReparsePointTraversal(packageDirectory, "package directory");
        RejectReparsePointTraversal(reportDirectory, "output directory");

        if (IsPathInsideOrEqual(fixtureDirectory, reportDirectory))
            throw new InvalidOperationException("Package consumer output must not equal or be nested under the tracked fixture directory.");
        if (IsPathInsideOrEqual(packageDirectory, reportDirectory))
            throw new InvalidOperationException("Package consumer output must not equal or be nested under the candidate package directory.");
    }

    private static bool IsPathInsideOrEqual(string parentPath, string candidatePath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(parentPath), Path.GetFullPath(candidatePath));
        if (relative == ".")
            return true;
        if (Path.IsPathRooted(relative) || relative == "..")
            return false;
        return !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void RejectReparsePointTraversal(string path, string label)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException($"Could not determine the filesystem root for {label} '{fullPath}'.");
        var current = root;
        var relative = fullPath[root.Length..];
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
                break;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Package consumer {label} traverses reparse point '{current}', which is not allowed for release evidence.");
        }
    }

    private static string? FindExecutableDll(string buildDirectory) =>
        Directory.Exists(buildDirectory)
            ? Directory.EnumerateFiles(buildDirectory, $"{ProjectName}.dll", SearchOption.AllDirectories)
                .Where(path => path.Contains("net10.0", StringComparison.OrdinalIgnoreCase))
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => File.Exists(Path.ChangeExtension(path, ".runtimeconfig.json")))
                .OrderBy(path => path.Length)
                .FirstOrDefault()
            : null;

    private static void ValidateAssetsTargets(
        JsonElement root,
        string version,
        ICollection<PackageConsumerSmokeFinding> findings)
    {
        if (!root.TryGetProperty("targets", out var targets))
        {
            AddError(findings, "assets-targets-missing", "project.assets.json does not contain a targets object.");
            return;
        }

        foreach (var targetFramework in RequiredTargetFrameworks)
        {
            var target = targets.EnumerateObject().SingleOrDefault(candidate =>
                AssetsTargetMatchesFramework(candidate.Name, targetFramework));
            if (target.Equals(default(JsonProperty)))
            {
                AddError(findings, "assets-target-framework-missing", $"project.assets.json has no target for '{targetFramework}'.");
                continue;
            }

            foreach (var packageId in RequiredPackageIds)
            {
                var expectedLibrary = $"{packageId}/{version}";
                if (!target.Value.EnumerateObject().Any(library =>
                        library.Name.Equals(expectedLibrary, StringComparison.OrdinalIgnoreCase)))
                {
                    AddError(
                        findings,
                        "assets-target-package-missing",
                        $"Assets target '{target.Name}' does not resolve exact package '{expectedLibrary}'.");
                }
            }
        }
    }

    private static bool AssetsTargetMatchesFramework(string assetsTarget, string targetFramework)
    {
        if (assetsTarget.Equals(targetFramework, StringComparison.OrdinalIgnoreCase))
            return true;

        var numericVersion = targetFramework.StartsWith("net", StringComparison.OrdinalIgnoreCase)
            ? targetFramework[3..]
            : targetFramework;
        return assetsTarget.Contains($"Version=v{numericVersion}", StringComparison.OrdinalIgnoreCase) &&
               !assetsTarget.Contains('/', StringComparison.Ordinal);
    }

    private static PackageConsumerCommandReport ToCommandReport(
        string name,
        DotnetCommandResult result,
        string workingDirectory) =>
        new(
            name,
            "dotnet",
            result.Arguments,
            workingDirectory,
            result.ProcessResult.ExitCode,
            result.ProcessResult.Duration.TotalSeconds,
            result.RawLogPath,
            result.ProcessResult.ExitCode == 0,
            result.ProcessResult.ExitCode == 0 ? null : result.Analysis.FailureSummary);

    private static (string Id, string Version) ReadNuspecIdentity(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries.Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length != 1)
            throw new InvalidDataException($"Expected exactly one nuspec, found {entries.Length}.");
        using var stream = entries[0].Open();
        var document = XDocument.Load(stream);
        var metadata = document.Descendants().Single(element => element.Name.LocalName == "metadata");
        var id = metadata.Elements().Single(element => element.Name.LocalName == "id").Value.Trim();
        var version = metadata.Elements().Single(element => element.Name.LocalName == "version").Value.Trim();
        return (id, version);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string? ReadMetadataSource(string metadataPath)
    {
        if (!File.Exists(metadataPath))
            return null;
        using var document = JsonDocument.Parse(File.ReadAllText(metadataPath, Encoding.UTF8));
        return document.RootElement.TryGetProperty("source", out var source) ? source.GetString() : null;
    }

    private static bool SourceMatchesDirectory(string? source, string candidateDirectory)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;
        var sourcePath = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : source;
        try
        {
            return PathsEqual(sourcePath, candidateDirectory);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void WriteReportArtifacts(PackageConsumerSmokeReport report)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(Path.Combine(report.ReportDirectory, "report.json"), JsonSerializer.Serialize(report, jsonOptions), Encoding.UTF8);
        File.WriteAllText(Path.Combine(report.ReportDirectory, "report.md"), ToMarkdown(report), Encoding.UTF8);
    }

    private static void TryWriteExceptionLog(string logsDirectory, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(logsDirectory);
            File.WriteAllText(Path.Combine(logsDirectory, "runner-exception.log"), exception.ToString(), Encoding.UTF8);
        }
        catch (Exception writeException) when (IsReportable(writeException))
        {
        }
    }

    private static void AddError(ICollection<PackageConsumerSmokeFinding> findings, string code, string message) =>
        findings.Add(new PackageConsumerSmokeFinding(PackageConsumerSmokeFindingSeverity.Error, code, message));

    private static bool HasErrors(IEnumerable<PackageConsumerSmokeFinding> findings) =>
        findings.Any(static finding => finding.Severity == PackageConsumerSmokeFindingSeverity.Error);

    private static bool IsReportable(Exception exception) =>
        exception is not OutOfMemoryException and not AccessViolationException and not OperationCanceledException;

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
