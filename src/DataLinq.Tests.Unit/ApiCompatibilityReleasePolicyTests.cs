using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class ApiCompatibilityReleasePolicyTests
{
    [Test]
    [Arguments("0.8.0", true)]
    [Arguments("0.9.0", false)]
    [Arguments("0.9.2", false)]
    public async Task Report_RoutesMemoryThroughTheCorrectCompatibilityLane(string version, bool memoryIsNew)
    {
        using var baseline = new ApiPackageSetInspectorTests.PackageFixture();
        using var candidate = new ApiPackageSetInspectorTests.PackageFixture();
        var policy = ApiCompatibilityReleasePolicy.ForBaseline(version);
        foreach (var id in policy.BaselinePackageIds)
            baseline.WritePackage(id, version: version);
        foreach (var id in policy.CandidatePackageIds)
            candidate.WritePackage(id, version: "0.10.0-preview.1");
        var root = Path.GetDirectoryName(candidate.PackageDirectory)!;
        var lockPath = Path.Combine(root, "test-infra", "api-compatibility", $"v{version}-packages.json");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        Directory.CreateDirectory(Path.Combine(root, ".config"));
        File.WriteAllText(Path.Combine(root, ".config", "dotnet-tools.json"), "{}");
        File.WriteAllText(lockPath, JsonSerializer.Serialize(new
        {
            schemaVersion = policy.LockSchemaVersion,
            baselineVersion = version,
            packageSource = "https://api.nuget.org/v3/index.json",
            repositoryUrl = "https://github.com/bazer/DataLinq",
            repositoryCommit = "1a156819e1567a4db3b8bd43e4e09e8da1a5572c",
            repositoryTag = version,
            repositoryTagObjectType = "commit",
            provenanceNote = "Synthetic test packages; not release evidence.",
            packages = policy.BaselinePackageIds.Select(id => new
            {
                id,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                    Path.Combine(baseline.PackageDirectory, $"{id}.{version}.nupkg")))).ToLowerInvariant()
            }),
            inheritedFrameworkDivergences = Array.Empty<object>()
        }));
        var options = new ApiCompatibilityReportOptions(root, candidate.PackageDirectory,
            "0.10.0-preview.1", baseline.PackageDirectory, version, lockPath,
            Path.Combine(root, "report"), ToolingProfile.Repo);
        var report = new ApiCompatibilityReporter(DevToolPaths.Create(root), options, new MemoryBreakRunner()).CreateReport();
        var memoryComparison = report.Comparisons.Single(comparison => comparison.PackageId == "DataLinq.Memory");

        await Assert.That(report.Invocation.ReleasePolicy).IsEqualTo(memoryIsNew ? "v0.9" : "v0.10");
        await Assert.That(report.SchemaVersion).IsEqualTo(policy.ReportSchemaVersion);
        await Assert.That(report.BaselinePackages!.Packages.Count).IsEqualTo(memoryIsNew ? 5 : 6);
        await Assert.That(memoryComparison.Kind).IsEqualTo(memoryIsNew
            ? ApiCompatibilityComparisonKind.NewPackage : ApiCompatibilityComparisonKind.PackageBaseline);
        await Assert.That(report.Findings.Any(finding => finding.PackageId == "DataLinq.Memory" &&
            finding.ChangeKind == ApiCompatibilityChangeKind.CompatibilityBreak)).IsEqualTo(!memoryIsNew);
        // A synthetic package/runner check must never claim actual clean release evidence.
        await Assert.That(report.Runner.ValidForEvidence).IsFalse();
    }

    [Test]
    [Arguments("0.7.0")]
    [Arguments("0.8.1")]
    [Arguments("0.10.0")]
    [Arguments("0.9.0-preview.1")]
    [Arguments("0.09.0")]
    [Arguments("0.9.0.0")]
    public async Task Policy_RejectsUnreviewedOrAmbiguousReleaseLines(string version)
    {
        ArgumentException? failure = null;
        try { ApiCompatibilityReleasePolicy.ForBaseline(version); }
        catch (ArgumentException exception) { failure = exception; }
        await Assert.That(failure).IsNotNull();
    }

    private sealed class MemoryBreakRunner : IApiCompatProcessRunner
    {
        public ExternalCommandResult Execute(string fileName, IReadOnlyList<string> arguments,
            string workingDirectory, IReadOnlyDictionary<string, string?> environmentVariables)
        {
            if (arguments.Contains("--version"))
                return new ExternalCommandResult(0, "10.0.400", "");
            var args = arguments.ToList();
            if (args.Contains("--baseline-package") &&
                args.Any(argument => argument.EndsWith("DataLinq.Memory.0.10.0-preview.1.nupkg", StringComparison.Ordinal)))
            {
                File.WriteAllText(args[args.IndexOf("--suppression-output-file") + 1],
                    """
                    <Suppressions><Suppression>
                      <DiagnosticId>CP0002</DiagnosticId><Target>M:DataLinq.Memory.Removed()</Target>
                      <Left>lib/net10.0/DataLinq.Memory.dll</Left><Right>lib/net10.0/DataLinq.Memory.dll</Right>
                      <IsBaselineSuppression>true</IsBaselineSuppression>
                    </Suppression></Suppressions>
                    """);
            }
            return new ExternalCommandResult(0, "", "");
        }
    }
}
