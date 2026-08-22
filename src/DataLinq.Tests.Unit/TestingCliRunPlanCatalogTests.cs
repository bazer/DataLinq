using System.IO;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class TestingCliRunPlanCatalogTests
{
    [Test]
    public async Task Catalog_SeparatesRunPlansFromProviderTargetSets()
    {
        var source = ReadCliSource("Selection", "TestCliRunPlanCatalog.cs");
        var command = ReadCliSource("Commands", "RunCommand.cs");
        var aggregate = ReadCliSource("Commands", "AggregateCommand.cs");
        var program = ReadCliSource("", "Program.cs");

        await Assert.That(source).Contains("FocusedPlan = \"focused\"");
        await Assert.That(source).Contains("SmokePlan = \"smoke\"");
        await Assert.That(source).Contains("QuickPlan = \"quick\"");
        await Assert.That(source).Contains("LatestPlan = \"latest\"");
        await Assert.That(source).Contains("FullPlan = \"full\"");
        await Assert.That(command).Contains("Provider --alias/--targets remain an independent override");
        await Assert.That(command).Contains("ResolveTargetSelection(");
        await Assert.That(command).Contains("--maximum-parallel-tests");
        await Assert.That(command).Contains("--provider-affinity-role");
        await Assert.That(source).Contains("MaximumParallelTests: 8");
        await Assert.That(aggregate)
            .Contains("--commit-sha")
            .And.Contains("--configuration")
            .And.Contains("--baseline");
        await Assert.That(program).Contains("AggregateCommand.Create(settings)");
    }

    [Test]
    public async Task Smoke_IsAnExplicitNoPodmanTestMethodAllowList()
    {
        var source = ReadCliSource("Selection", "TestCliRunPlanCatalog.cs");
        var command = ReadCliSource("Commands", "RunCommand.cs");

        await Assert.That(source).Contains("SmokeGeneratorFilter");
        await Assert.That(source).Contains("SmokeUnitFilter");
        await Assert.That(source).Contains("SmokeMemoryFilter");
        await Assert.That(source).Contains("SmokeSqliteFilter");
        await Assert.That(source).Contains("SubscribeAndNotify_NotifiesLiveSubscriber");
        await Assert.That(source).Contains("Insert_CommitsInsertedEmployeeAcrossProviders");
        await Assert.That(source).Contains("DefaultTargetIds: [TestTargetCatalog.SQLiteFileTargetId]");
        await Assert.That(command).Contains("plan is a no-Podman contract");
        await Assert.That(command).Contains("suite.Filter ?? filter");
    }

    [Test]
    public async Task Focused_RequiresSuiteAndTreeFilter()
    {
        var command = ReadCliSource("Commands", "RunCommand.cs");

        await Assert.That(command).Contains("The focused plan requires an explicit '--suite'.");
        await Assert.That(command).Contains("The focused plan requires an explicit TUnit '--filter'.");
        await Assert.That(command).Contains("filterOption.Aliases.Add(\"--treenode-filter\")");
    }

    [Test]
    public async Task Plans_RecordEvidenceAndListTheMeasuredWarmDuration()
    {
        var run = ReadCliSource("Commands", "RunCommand.cs");
        var list = ReadCliSource("Commands", "ListCommand.cs");
        var model = File.ReadAllText(Path.Combine(
            RepositoryRootLocator.Find(),
            "src",
            "DataLinq.DevTools",
            "TestRunSummaryReportModels.cs"));

        await Assert.That(run).Contains("GetLastSummaryPath(settings.RepositoryRoot, requestedPlan.Name)");
        await Assert.That(list).Contains("TestHostProcessSeconds");
        await Assert.That(list).Contains("Expected cases");
        await Assert.That(model).Contains("string? Plan = null");
        await Assert.That(model).Contains("string? ProviderAffinityRole = null");
    }

    [Test]
    public async Task CompletePlans_RunProviderInvariantTestsOnce()
    {
        var catalog = ReadCliSource("Selection", "TestCliRunPlanCatalog.cs");
        var command = ReadCliSource("Commands", "RunCommand.cs");

        await Assert.That(catalog).Contains("ComplianceSqliteAnchorMinimumCases");
        await Assert.That(catalog).Contains("ComplianceSqliteTargetMinimumCases");
        await Assert.That(catalog).Contains("ComplianceServerTargetMinimumCases");
        await Assert.That(catalog).Contains("MySqlInvariantMinimumCases");
        await Assert.That(catalog).Contains("MySqlTargetMinimumCases");
        await Assert.That(catalog).Contains("MariaDbTargetMinimumCases");
        await Assert.That(command).Contains("AnchorWithInvariant");
        await Assert.That(command).Contains("TargetSpecific");
        await Assert.That(command).Contains("DatabaseServerMatrix.DefaultProfile.MySqlTarget");
        await Assert.That(command).Contains("OrderCompletePlanTargets");
        await Assert.That(command).Contains("ProviderAffinity=EveryProvider");
        await Assert.That(command).Contains("ProviderAffinity=ServerFamily");
    }

    private static string ReadCliSource(string directory, string fileName) =>
        File.ReadAllText(Path.Combine(
            RepositoryRootLocator.Find(),
            "src",
            "DataLinq.Testing.CLI",
            directory,
            fileName));
}
