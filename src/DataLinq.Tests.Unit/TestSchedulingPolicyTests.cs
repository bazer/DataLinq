using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class TestSchedulingPolicyTests
{
    [Test]
    public async Task ProcessGlobalNotInParallel_ExistsOnlyForDocumentedGlobalResources()
    {
        var src = Path.Combine(RepositoryRootLocator.Find(), "src");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains("DataLinq.Tests.", StringComparison.Ordinal))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\[NotInParallel\s*\]"))
            .Select(path => Path.GetRelativePath(src, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] allowedProcessGlobalFiles =
        [
            "DataLinq.Tests.Compliance/Infrastructure/EmployeesFixtureIsolationTests.cs",
            "DataLinq.Tests.Compliance/Infrastructure/ProviderMatrixTests.cs",
            "DataLinq.Tests.Compliance/Query/EmployeesOptimizationTests.cs",
            "DataLinq.Tests.Compliance/Query/ProviderEquivalentPrimaryKeyLookupTests.cs",
            "DataLinq.Tests.Compliance/Query/QueryPlanCapabilityExecutionTests.cs",
            "DataLinq.Tests.Compliance/State/EmployeesCacheInvalidationCharacterizationTests.cs",
            "DataLinq.Tests.Compliance/State/RelationCacheInvalidationPrecisionTests.cs",
            "DataLinq.Tests.Compliance/State/SQLiteGuidStorageRoundTripTests.cs",
            "DataLinq.Tests.Compliance/State/ServerGuidStorageRoundTripTests.cs",
            "DataLinq.Tests.Compliance/Translation/ConvertedAggregateTranslationTests.cs",
            "DataLinq.Tests.Compliance/Translation/Int64TypedIdKeyBoundaryTests.cs",
            "DataLinq.Tests.Compliance/Translation/JoinKeyCompatibilityTranslationTests.cs",
            "DataLinq.Tests.Compliance/Translation/JoinedGuidTypedIdKeyHydrationTests.cs",
            "DataLinq.Tests.Compliance/Translation/PreparedQueryTests.cs",
            "DataLinq.Tests.Compliance/Translation/TypedIdPredicateTranslationTests.cs",
            "DataLinq.Tests.Compliance/Translation/TypedIdRelationKeyNormalizationTests.cs",
            "DataLinq.Tests.Memory/MemoryBooleanPredicateTests.cs",
            "DataLinq.Tests.Memory/MemoryCanonicalGuidEqualityTests.cs",
            "DataLinq.Tests.Memory/MemoryInt32MembershipTests.cs",
            "DataLinq.Tests.Memory/MemoryModelSeedTests.cs",
            "DataLinq.Tests.Memory/MemoryNotEqualTests.cs",
            "DataLinq.Tests.Memory/MemoryOrderedInt32ComparisonTests.cs",
            "DataLinq.Tests.Memory/MemoryPrimaryKeyLookupTests.cs",
            "DataLinq.Tests.Memory/MemoryPublicApiTests.cs",
            "DataLinq.Tests.Memory/MemorySQLiteParityTests.cs",
            "DataLinq.Tests.Memory/MemoryVerticalSpikeTests.cs",
            "DataLinq.Tests.Unit/CacheNotificationManagerTests.cs",
            "DataLinq.Tests.Unit/CliDiagnosticWriterTests.cs",
            "DataLinq.Tests.Unit/Core/CacheMemoryEstimateTests.cs",
            "DataLinq.Tests.Unit/Core/DataSourceAccessSourceRowLoaderTests.cs",
            "DataLinq.Tests.Unit/Core/DatabaseDefinitionResolverTests.cs",
            "DataLinq.Tests.Unit/Core/ModelGeneratorModelDirectoryTests.cs",
            "DataLinq.Tests.Unit/Core/ReadSourceMaterializationServicesTests.cs",
            "DataLinq.Tests.Unit/Core/SchemaValidatorTests.cs",
            "DataLinq.Tests.Unit/Core/TransactionMutationGuardTests.cs",
            "DataLinq.Tests.Unit/DataLinqCliBatchCommandTests.cs",
            "DataLinq.Tests.Unit/DataLinqCliCommandSurfaceTests.cs",
            "DataLinq.Tests.Unit/DataLinqCliTargetResolverTests.cs",
            "DataLinq.Tests.Unit/DataLinqConfigInitTests.cs",
            "DataLinq.Tests.Unit/DataLinqConfigSchemaTests.cs",
            "DataLinq.Tests.Unit/DataLinqMetricsTests.cs",
            "DataLinq.Tests.Unit/SQLite/SQLiteWalConcurrencyCharacterizationTests.cs",
            "DataLinq.Tests.Unit/SQLite/TelemetryTests.cs"
        ];

        await Assert.That(offenders).IsEquivalentTo(allowedProcessGlobalFiles);
    }

    [Test]
    public async Task ComplianceProviderDataSources_DeclareTheirRunManifestAffinity()
    {
        var root = Path.Combine(RepositoryRootLocator.Find(), "src", "DataLinq.Tests.Compliance");
        var source = string.Join(
            "\n",
            Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        await AssertAffinityCount(source, "ActiveProviders", "EveryProvider");
        await AssertAffinityCount(source, "ServerProviders", "ServerFamily");
        await AssertAffinityCount(source, "SqliteProviders", "SQLiteOnly");
        await AssertAffinityCount(source, "AllLtsServerProviders", "ProviderCatalog");
        await Assert.That(Regex.IsMatch(
            source,
            @"TestProviderAffinity\.ServerFamily\)\]\s*\[MethodDataSource\(\s*typeof\(ProviderEquivalentPrimaryKeyLookupTests\),\s*nameof\(CaseInsensitiveServerProviders\)\)\]")).IsTrue();
    }

    [Test]
    public async Task MySqlProviderDataSources_DeclareTheirRunManifestAffinity()
    {
        var root = Path.Combine(RepositoryRootLocator.Find(), "src", "DataLinq.Tests.MySql");
        var source = string.Join(
            "\n",
            Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        foreach (var dataSource in new[] { "ActiveServerProviders", "MySqlProviders", "MariaDbProviders" })
            await AssertAffinityCount(source, dataSource, "ServerFamily");

        foreach (var dataSource in new[]
        {
            "IntegerTypeCases",
            "FloatingAndTemporalCases",
            "StringBinaryAndSpecialCases",
            "NullableCases",
            "QuotedNumericDefaultCases"
        })
        {
            await Assert.That(Regex.IsMatch(
                source,
                $@"TestProviderAffinity\.ServerFamily\)\]\s*\[MethodDataSource\(nameof\({dataSource}\)\)\]")).IsTrue();
        }
    }

    private static async Task AssertAffinityCount(string source, string dataSource, string affinity)
    {
        var dataSourceCount = Regex.Matches(
            source,
            $@"nameof\(TestProviderDataSources\.{dataSource}\)").Count;
        var classifiedCount = Regex.Matches(
            source,
            $@"TestProviderAffinity\.{affinity}\)\]\s*\[MethodDataSource\(typeof\(TestProviderDataSources\),\s*nameof\(TestProviderDataSources\.{dataSource}\)\)\]").Count;

        await Assert.That(classifiedCount).IsEqualTo(dataSourceCount);
        await Assert.That(dataSourceCount).IsGreaterThan(0);
    }
}
