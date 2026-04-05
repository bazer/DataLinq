using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Testing;

namespace DataLinq.Testing.CLI;

internal static class TestCliCatalog
{
    public static IReadOnlyList<TestCliTarget> Targets { get; } = CreateTargets();

    public static IReadOnlyList<TestCliAlias> Aliases { get; } =
    [
        new(
            Name: TestTargetCatalog.QuickAlias,
            Description: "Runs the local SQLite quick lane only.",
            TargetIds: TestTargetCatalog.ResolveAlias(TestTargetCatalog.QuickAlias)),
        new(
            Name: TestTargetCatalog.LatestAlias,
            Description: "Runs SQLite plus the latest supported MySQL and MariaDB LTS targets.",
            TargetIds: TestTargetCatalog.ResolveAlias(TestTargetCatalog.LatestAlias)),
        new(
            Name: TestTargetCatalog.AllAlias,
            Description: "Runs every supported local test target.",
            TargetIds: TestTargetCatalog.ResolveAlias(TestTargetCatalog.AllAlias))
    ];

    public static TestCliAlias GetAlias(string name) =>
        Aliases.Single(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    public static TestCliTarget GetTarget(string id) =>
        Targets.Single(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<TestCliTarget> CreateTargets()
    {
        var targets = new List<TestCliTarget>
        {
            new(TestTargetCatalog.SQLiteFileTargetId, "SQLite File", UsesPodman: false, Category: "SQLite", ServerTarget: null),
            new(TestTargetCatalog.SQLiteMemoryTargetId, "SQLite In-Memory", UsesPodman: false, Category: "SQLite", ServerTarget: null)
        };

        targets.AddRange(DatabaseServerMatrix.Targets.Select(target =>
            new TestCliTarget(
                Id: target.Id,
                DisplayName: target.DisplayName,
                UsesPodman: true,
                Category: target.Family.ToString(),
                ServerTarget: target)));

        return targets
            .OrderBy(x => x.UsesPodman)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
