using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataLinq.Testing;

namespace DataLinq.Testing.CLI;

internal static class TestCliRunPlanCatalog
{
    public const string FocusedPlan = "focused";
    public const string SmokePlan = "smoke";
    public const string QuickPlan = "quick";
    public const string LatestPlan = "latest";
    public const string FullPlan = "full";

    private const string SmokeGeneratorFilter =
        "/*/*/ModelGeneratorInputTests/ModelDeclarationInputComparer_UsesStructuralSnapshot";
    private const string SmokeUnitFilter =
        "/*/*/QueryExecutionContractTests/ScalarCount_UsesExactValidatedBackendOnceAndReturnsSemanticResult|" +
        "/*/*/TransactionMutationGuardTests/StateChange_Insert_OmitsEligibleUnsetDefaultButWritesAssignedNull|" +
        "/*/*/ModelValueConverterTests/ToCanonicalProviderValue_ConvertsOnceWithColumnContextAndOwnsBinaryResult|" +
        "/*/*/CacheNotificationManagerTests/SubscribeAndNotify_NotifiesLiveSubscriber";
    private const string SmokeMemoryFilter =
        "/*/*/MemoryVerticalSpikeTests/CanonicalSeed_PrimaryKeyLookup_UsesGeneratedMaterializationAndSeparateIdentityCache|" +
        "/*/*/MemoryVerticalSpikeTests/CapturedInt32Equality_FiltersCanonicalRowsBeforeMaterializationAndReusesIdentity";
    private const string SmokeSqliteFilter =
        "/*/*/EmployeesSqlQueryTests/SqlQuery_SimpleWhereSelectsDepartmentAcrossProviders|" +
        "/*/*/EmployeesTransactionTests/Insert_CommitsInsertedEmployeeAcrossProviders|" +
        "/*/*/SQLiteGuidStorageRoundTripTests/ScalarGuidPrimaryKeys_UseResolvedCodecsAcrossSQLiteCacheAndMutationPaths";

    public static IReadOnlyList<TestCliRunPlan> Plans { get; } =
    [
        new(
            Name: FocusedPlan,
            Description: "Runs one explicitly selected suite and TUnit tree filter for the code under change.",
            Command: "run --plan focused --suite <suite> --filter <tree-filter>",
            Prerequisites: "Only the selected suite's local/runtime prerequisites.",
            WarmBudgetSeconds: 30,
            DefaultTargetAlias: null,
            DefaultTargetIds: [TestTargetCatalog.SQLiteFileTargetId],
            Suites: Array.Empty<TestCliRunPlanSuite>(),
            RequiresExplicitSelection: true),
        new(
            Name: SmokePlan,
            Description: "Runs representative query, mutation, mapping, cache, generator, Memory, and SQLite coverage without Podman.",
            Command: "run --plan smoke",
            Prerequisites: "Warm restore/build; no Podman or server database.",
            WarmBudgetSeconds: 30,
            DefaultTargetAlias: null,
            DefaultTargetIds: [TestTargetCatalog.SQLiteFileTargetId],
            Suites:
            [
                new(TestCliSuiteCatalog.GeneratorsSuite, SmokeGeneratorFilter, 1, 1, "generator/compiler", "compiler"),
                new(TestCliSuiteCatalog.UnitSuite, SmokeUnitFilter, 29, 1, "query, mutation, mapping, cache", "in-process"),
                new(TestCliSuiteCatalog.MemorySuite, SmokeMemoryFilter, 11, 1, "memory integration", "in-process database"),
                new(TestCliSuiteCatalog.ComplianceSuite, SmokeSqliteFilter, 8, 2, "query, mutation, mapping", "SQLite file")
            ]),
        new(
            Name: QuickPlan,
            Description: "Runs all generator, unit, Memory, and provider-invariant compliance tests against one SQLite mode.",
            Command: "run --plan quick",
            Prerequisites: "Warm restore/build; no Podman or server database.",
            WarmBudgetSeconds: 60,
            DefaultTargetAlias: null,
            DefaultTargetIds: [TestTargetCatalog.SQLiteFileTargetId],
            Suites:
            [
                new(TestCliSuiteCatalog.GeneratorsSuite, null, 61, 8, "generator/compiler", "compiler", MaximumParallelTests: 8),
                new(TestCliSuiteCatalog.UnitSuite, null, 1618, 16, "core unit and tooling", "in-process, process, filesystem", MaximumParallelTests: 16),
                new(TestCliSuiteCatalog.MemorySuite, null, 142, 5, "memory integration", "in-process database", MaximumParallelTests: 8),
                new(TestCliSuiteCatalog.ComplianceSuite, null, 484, 26, "provider-invariant compliance", "SQLite file", MaximumParallelTests: 8)
            ]),
        new(
            Name: LatestPlan,
            Description: "Runs complete logical coverage against SQLite and the latest supported target in each server family.",
            Command: "run --plan latest",
            Prerequisites: "Podman and the latest MySQL/MariaDB target set.",
            WarmBudgetSeconds: 300,
            DefaultTargetAlias: TestTargetCatalog.LatestAlias,
            DefaultTargetIds: Array.Empty<string>(),
            Suites: CompleteSuites(estimatedTargets: 4, estimatedServerTargets: 2, estimatedDurationSeconds: 165)),
        new(
            Name: FullPlan,
            Description: "Runs every required suite against every supported provider target.",
            Command: "run --plan full",
            Prerequisites: "Podman and the full supported MySQL/MariaDB matrix.",
            WarmBudgetSeconds: 600,
            DefaultTargetAlias: TestTargetCatalog.AllAlias,
            DefaultTargetIds: Array.Empty<string>(),
            Suites: CompleteSuites(estimatedTargets: TestCliCatalog.Targets.Count, estimatedServerTargets: TestCliCatalog.Targets.Count(static target => target.UsesPodman), estimatedDurationSeconds: 360))
    ];

    public static TestCliRunPlan GetPlan(string name) =>
        Plans.FirstOrDefault(plan => string.Equals(plan.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"Unknown run plan '{name}'. Use one of: {string.Join(", ", Plans.Select(static plan => plan.Name))}.");

    public static IReadOnlyList<TestCliSuite> ResolveSuites(TestCliRunPlan plan) =>
        plan.Suites
            .Select(entry => TestCliSuiteCatalog.GetSuite(entry.Suite) with
            {
                Filter = entry.Filter,
                ExpectedCaseCount = entry.ExpectedCaseCount,
                EstimatedDurationSeconds = entry.EstimatedDurationSeconds,
                Purpose = entry.Purpose,
                Resource = entry.Resource,
                MaximumParallelTests = entry.MaximumParallelTests
            })
            .ToArray();

    public static string GetLastSummaryPath(string repositoryRoot, string planName) =>
        Path.Combine(repositoryRoot, "artifacts", "test-results", $"last-{planName.ToLowerInvariant()}.json");

    private static IReadOnlyList<TestCliRunPlanSuite> CompleteSuites(
        int estimatedTargets,
        int estimatedServerTargets,
        double estimatedDurationSeconds)
    {
        const int complianceAnchorCases = 484;
        const int everyProviderCases = 363;
        const int serverTargetCases = 369;
        var remainingSqliteTargets = Math.Max(0, estimatedTargets - estimatedServerTargets - 1);
        var complianceCases = complianceAnchorCases
            + (remainingSqliteTargets * everyProviderCases)
            + (estimatedServerTargets * serverTargetCases);
        const int mySqlInvariantCases = 65;
        const int mySqlTargetCases = 61;
        const int mariaDbTargetCases = 63;
        var mySqlProviderCases = estimatedServerTargets == 0
            ? 0
            : mySqlInvariantCases
                + mySqlTargetCases
                + (Math.Max(0, estimatedServerTargets - 1) * mariaDbTargetCases);
        return
        [
            new(TestCliSuiteCatalog.GeneratorsSuite, null, 61, 8, "generator/compiler", "compiler", MaximumParallelTests: 8),
            new(TestCliSuiteCatalog.UnitSuite, null, 1618, 16, "core unit and tooling", "in-process, process, filesystem", MaximumParallelTests: 16),
            new(TestCliSuiteCatalog.MemorySuite, null, 142, 5, "memory integration", "in-process database", MaximumParallelTests: 8),
            new(TestCliSuiteCatalog.ComplianceSuite, null, complianceCases, estimatedDurationSeconds * 0.75, "provider-invariant compliance", "SQLite and server databases", MaximumParallelTests: 8),
            new(TestCliSuiteCatalog.MySqlSuite, null, mySqlProviderCases, estimatedDurationSeconds * 0.25, "provider-specific compliance", "MySQL/MariaDB server", MaximumParallelTests: 8)
        ];
    }
}
