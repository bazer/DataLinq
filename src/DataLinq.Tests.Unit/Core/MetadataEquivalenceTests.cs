using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using DataLinq.PlatformCompatibility.Smoke;
using DataLinq.Tests.Models.Allround;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class MetadataEquivalenceTests
{
    [Test]
    public async Task TypedSourceDraftAndTypedGeneratedRuntimeMetadata_AreEquivalentForRepresentativeModels()
    {
        var repositoryRoot = RepositoryLayout.FindRepositoryRoot();

        await AssertMetadataEquivalent(
            typeof(EmployeesDb),
            Path.Combine(repositoryRoot, "src", "DataLinq.Tests.Models", "employees"));
        await AssertMetadataEquivalent(
            typeof(AllroundBenchmark),
            Path.Combine(repositoryRoot, "src", "DataLinq.Tests.Models", "Allround"));
        await AssertMetadataEquivalent(
            typeof(PlatformSmokeDb),
            Path.Combine(repositoryRoot, "src", "DataLinq.PlatformCompatibility.Smoke"));
    }

    private static async Task AssertMetadataEquivalent(Type databaseType, string sourcePath)
    {
        var sourceMetadata = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions())
            .ReadFiles(databaseType.Name, [sourcePath])
            .ValueOrException()
            .Single(database => database.CsType.Name == databaseType.Name);
        var generatedMetadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(databaseType).ValueOrException();

        await Assert.That(MetadataEquivalenceDigest.CreateText(generatedMetadata))
            .IsEqualTo(MetadataEquivalenceDigest.CreateText(sourceMetadata));
    }
}
