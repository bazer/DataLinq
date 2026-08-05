using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class CompatibilitySmokeProjectDependencySourceTests
{
    private const string ProjectReferencesCondition =
        "'$(DataLinqCompatibilityDependencySource)' == 'ProjectReferences'";
    private const string PackedPackagesCondition =
        "'$(DataLinqCompatibilityDependencySource)' == 'PackedPackages'";
    private const string ExactCandidateVersion = "[$(DataLinqCandidateVersion)]";

    [Test]
    [Arguments(
        "DataLinq.PlatformCompatibility.Smoke",
        "DataLinq.SQLite",
        @"..\DataLinq.SQLite\DataLinq.SQLite.csproj")]
    [Arguments(
        "DataLinq.Memory.PlatformCompatibility.Smoke",
        "DataLinq.Memory",
        @"..\DataLinq.Memory\DataLinq.Memory.csproj")]
    public async Task SmokeProject_SeparatesProjectAndExactPackageDependencyGraphs(
        string projectName,
        string providerPackageId,
        string providerProjectPath)
    {
        var projectPath = Path.Combine(
            RepositoryRootLocator.Find(),
            "src",
            projectName,
            $"{projectName}.csproj");
        var document = XDocument.Load(projectPath);

        var dependencySource = document.Descendants()
            .Single(static element => element.Name.LocalName == "DataLinqCompatibilityDependencySource");
        await Assert.That(dependencySource.Value).IsEqualTo("ProjectReferences");
        await Assert.That(dependencySource.Attribute("Condition")?.Value)
            .IsEqualTo("'$(DataLinqCompatibilityDependencySource)' == ''");

        var centralPackageManagement = document.Descendants()
            .Single(static element => element.Name.LocalName == "ManagePackageVersionsCentrally");
        await Assert.That(centralPackageManagement.Value).IsEqualTo("false");
        await Assert.That(centralPackageManagement.Attribute("Condition")?.Value)
            .IsEqualTo(PackedPackagesCondition);

        var projectReferenceGroup = FindItemGroup(document, ProjectReferencesCondition);
        var projectReferences = projectReferenceGroup.Elements()
            .Where(static element => element.Name.LocalName == "ProjectReference")
            .ToArray();
        await Assert.That(string.Join(
                "|",
                projectReferences.Select(static reference => (string?)reference.Attribute("Include"))))
            .IsEqualTo(string.Join(
                "|",
                @"..\DataLinq\DataLinq.csproj",
                projectName.StartsWith("DataLinq.Memory.", StringComparison.Ordinal)
                    ? providerProjectPath
                    : @"..\DataLinq.Generators\DataLinq.Generators.csproj",
                projectName.StartsWith("DataLinq.Memory.", StringComparison.Ordinal)
                    ? @"..\DataLinq.Generators\DataLinq.Generators.csproj"
                    : providerProjectPath));
        await Assert.That(projectReferenceGroup.Elements()
                .Count(static element => element.Name.LocalName == "Analyzer"))
            .IsEqualTo(1);
        await Assert.That(projectReferenceGroup.Elements()
                .Count(static element => element.Name.LocalName == "PackageReference"))
            .IsEqualTo(0);

        var generatorReference = projectReferences.Single(reference =>
            ((string?)reference.Attribute("Include"))?.Contains("DataLinq.Generators", StringComparison.Ordinal) == true);
        await Assert.That(generatorReference.Attribute("OutputItemType")?.Value).IsEqualTo("Analyzer");
        await Assert.That(generatorReference.Attribute("ReferenceOutputAssembly")?.Value).IsEqualTo("false");
        await Assert.That(generatorReference.Attribute("GlobalPropertiesToRemove")?.Value)
            .IsEqualTo("PublishAot;PublishTrimmed;RuntimeIdentifier;SelfContained;PublishSingleFile;PublishReadyToRun");

        var packageReferenceGroup = FindItemGroup(document, PackedPackagesCondition);
        var packageReferences = packageReferenceGroup.Elements()
            .Where(static element => element.Name.LocalName == "PackageReference")
            .ToArray();
        await Assert.That(string.Join(
                "|",
                packageReferences.Select(static reference => string.Join(
                    "=",
                    (string?)reference.Attribute("Include"),
                    (string?)reference.Attribute("Version")))))
            .IsEqualTo($"DataLinq={ExactCandidateVersion}|{providerPackageId}={ExactCandidateVersion}");
        await Assert.That(packageReferenceGroup.Elements()
                .Count(static element => element.Name.LocalName is "ProjectReference" or "Analyzer"))
            .IsEqualTo(0);
    }

    [Test]
    [Arguments("DataLinq.PlatformCompatibility.Smoke")]
    [Arguments("DataLinq.Memory.PlatformCompatibility.Smoke")]
    public async Task SmokeProject_FailClosesInvalidSourceAndVersionCombinations(string projectName)
    {
        var projectPath = Path.Combine(
            RepositoryRootLocator.Find(),
            "src",
            projectName,
            $"{projectName}.csproj");
        var document = XDocument.Load(projectPath);
        var validationTarget = document.Descendants()
            .Single(element =>
                element.Name.LocalName == "Target" &&
                (string?)element.Attribute("Name") == "ValidateDataLinqCompatibilityDependencySource");

        await Assert.That(validationTarget.Attribute("BeforeTargets")?.Value)
            .IsEqualTo("CollectPackageReferences;PrepareForBuild");

        var errors = validationTarget.Elements()
            .Where(static element => element.Name.LocalName == "Error")
            .ToArray();
        await Assert.That(string.Join(
                "|",
                errors.Select(static error => (string?)error.Attribute("Condition"))))
            .IsEqualTo(
                "'$(DataLinqCompatibilityDependencySource)' != 'ProjectReferences' and " +
                "'$(DataLinqCompatibilityDependencySource)' != 'PackedPackages'|" +
                "'$(DataLinqCompatibilityDependencySource)' == 'PackedPackages' and " +
                "'$(DataLinqCandidateVersion)' == ''|" +
                "'$(DataLinqCompatibilityDependencySource)' == 'ProjectReferences' and " +
                "'$(DataLinqCandidateVersion)' != ''");
        await Assert.That(string.Join(
                "|",
                errors.Select(static error => (string?)error.Attribute("Text"))))
            .IsEqualTo(
                "DataLinqCompatibilityDependencySource must be ProjectReferences or PackedPackages.|" +
                "DataLinqCandidateVersion must be supplied explicitly for PackedPackages.|" +
                "DataLinqCandidateVersion is only valid with PackedPackages.");
    }

    private static XElement FindItemGroup(XDocument document, string condition) =>
        document.Descendants()
            .Single(element =>
                element.Name.LocalName == "ItemGroup" &&
                (string?)element.Attribute("Condition") == condition);
}
