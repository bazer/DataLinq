using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class RunnerBuildProvenanceTests
{
    private const string MetadataName = "DataLinqRepositoryBuildState";

    [Test]
    [Arguments("DataLinq.DevTools", "RunnerBuildProvenance.targets")]
    [Arguments("DataLinq.Dev.CLI", @"..\DataLinq.DevTools\RunnerBuildProvenance.targets")]
    public async Task RunnerProject_ImportsBuildProvenanceTarget(
        string projectName,
        string expectedImport)
    {
        var projectPath = Path.Combine(
            RepositoryRootLocator.Find(),
            "src",
            projectName,
            $"{projectName}.csproj");
        var document = XDocument.Load(projectPath);
        var import = document.Descendants()
            .Single(element =>
                element.Name.LocalName == "Import" &&
                ((string?)element.Attribute("Project"))?.EndsWith(
                    "RunnerBuildProvenance.targets",
                    StringComparison.Ordinal) == true);

        await Assert.That((string?)import.Attribute("Project")).IsEqualTo(expectedImport);
    }

    [Test]
    public async Task BuildProvenanceTarget_FailClosesAndFeedsAssemblyInfoInputs()
    {
        var targetPath = Path.Combine(
            RepositoryRootLocator.Find(),
            "src",
            "DataLinq.DevTools",
            "RunnerBuildProvenance.targets");
        var document = XDocument.Load(targetPath);
        var disableFastUpToDateCheck = document.Descendants()
            .Single(static element => element.Name.LocalName == "DisableFastUpToDateCheck");
        await Assert.That(disableFastUpToDateCheck.Value).IsEqualTo("true");

        var target = document.Descendants()
            .Single(element =>
                element.Name.LocalName == "Target" &&
                (string?)element.Attribute("Name") ==
                "CaptureDataLinqRunnerRepositoryBuildState");

        await Assert.That((string?)target.Attribute("BeforeTargets"))
            .IsEqualTo("GetAssemblyAttributes");
        await Assert.That((string?)target.Attribute("Condition"))
            .IsEqualTo("'$(DesignTimeBuild)' != 'true'");

        var exec = target.Elements().Single(static element => element.Name.LocalName == "Exec");
        await Assert.That((string?)exec.Attribute("Command"))
            .IsEqualTo(
                "git --no-optional-locks status --porcelain=v1 --untracked-files=all " +
                "--ignore-submodules=none");
        await Assert.That((string?)exec.Attribute("WorkingDirectory"))
            .IsEqualTo("$(_DataLinqRunnerRepositoryRoot)");
        await Assert.That((string?)exec.Attribute("ConsoleToMSBuild")).IsEqualTo("true");
        await Assert.That((string?)exec.Attribute("IgnoreExitCode")).IsEqualTo("true");
        var exitCodeOutput = exec.Elements()
            .Single(element =>
                element.Name.LocalName == "Output" &&
                (string?)element.Attribute("TaskParameter") == "ExitCode");
        await Assert.That((string?)exitCodeOutput.Attribute("TaskParameter"))
            .IsEqualTo("ExitCode");
        await Assert.That((string?)exitCodeOutput.Attribute("PropertyName"))
            .IsEqualTo("_DataLinqRunnerGitStatusExitCode");

        var stateProperties = target.Descendants()
            .Where(element => element.Name.LocalName == "_DataLinqRunnerRepositoryBuildState")
            .ToArray();
        await Assert.That(string.Join(
                "|",
                stateProperties.Select(static property => property.Value)))
            .IsEqualTo("unknown|clean|dirty");
        await Assert.That(string.Join(
                "|",
                stateProperties.Skip(1).Select(static property =>
                    (string?)property.Attribute("Condition"))))
            .IsEqualTo(
                "'$(_DataLinqRunnerGitStatusExitCode)' == '0' and " +
                "'$(_DataLinqRunnerGitStatusLineCount)' == '0'|" +
                "'$(_DataLinqRunnerGitStatusExitCode)' == '0' and " +
                "'$(_DataLinqRunnerGitStatusLineCount)' != '0'");

        var metadata = target.Descendants()
            .Single(element =>
                element.Name.LocalName == "AssemblyMetadata" &&
                (string?)element.Attribute("Include") == MetadataName);
        await Assert.That((string?)metadata.Attribute("Value"))
            .IsEqualTo("$(_DataLinqRunnerRepositoryBuildState)");
    }

    [Test]
    public async Task DevToolsAssembly_ContainsOneRepositoryBuildStateAttestation()
    {
        var values = typeof(CompatibilitySizeReporter).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(static attribute => attribute.Key.Equals(MetadataName, StringComparison.Ordinal))
            .Select(static attribute => attribute.Value)
            .ToArray();

        await Assert.That(values).HasSingleItem();
        await Assert.That(new[] { "clean", "dirty", "unknown" }.Contains(values[0], StringComparer.Ordinal))
            .IsTrue();
    }
}
