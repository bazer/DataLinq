using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace DataLinq.Testing.CLI;

internal static class TestCliSuiteCatalog
{
    public const string UnitSuite = "unit";
    public const string ComplianceSuite = "compliance";
    public const string AllSuites = "all";

    public static IReadOnlyList<TestCliSuite> Suites { get; } =
    [
        new(
            Name: UnitSuite,
            Description: "Runs the pure in-process unit lane once.",
            ProjectPath: Path.Combine("src", "DataLinq.Tests.Unit", "DataLinq.Tests.Unit.csproj"),
            UsesTargetBatches: false),
        new(
            Name: ComplianceSuite,
            Description: "Runs the cross-provider compliance lane against the selected targets.",
            ProjectPath: Path.Combine("src", "DataLinq.Tests.Compliance", "DataLinq.Tests.Compliance.csproj"),
            UsesTargetBatches: true)
    ];

    public static IReadOnlyList<TestCliSuite> Resolve(string suiteName)
    {
        if (string.Equals(suiteName, AllSuites, StringComparison.OrdinalIgnoreCase))
            return Suites;

        return [GetSuite(suiteName)];
    }

    public static TestCliSuite GetSuite(string name) =>
        Suites.Single(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
}
