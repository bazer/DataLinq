using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace DataLinq.Testing.CLI;

internal static class TestCliSuiteCatalog
{
    public const string GeneratorsSuite = "generators";
    public const string UnitSuite = "unit";
    public const string ComplianceSuite = "compliance";
    public const string MySqlSuite = "mysql";
    public const string AllSuites = "all";

    public static IReadOnlyList<TestCliSuite> Suites { get; } =
    [
        new(
            Name: GeneratorsSuite,
            Description: "Runs the source-generator lane once.",
            ProjectPath: Path.Combine("src", "DataLinq.Generators.Tests", "DataLinq.Generators.Tests.csproj"),
            UsesTargetBatches: false),
        new(
            Name: UnitSuite,
            Description: "Runs the pure in-process unit lane once.",
            ProjectPath: Path.Combine("src", "DataLinq.Tests.Unit", "DataLinq.Tests.Unit.csproj"),
            UsesTargetBatches: false),
        new(
            Name: ComplianceSuite,
            Description: "Runs the cross-provider compliance lane against the selected targets.",
            ProjectPath: Path.Combine("src", "DataLinq.Tests.Compliance", "DataLinq.Tests.Compliance.csproj"),
            UsesTargetBatches: true),
        new(
            Name: MySqlSuite,
            Description: "Runs the provider-specific MySQL and MariaDB lane against the selected server targets.",
            ProjectPath: Path.Combine("src", "DataLinq.Tests.MySql", "DataLinq.Tests.MySql.csproj"),
            UsesTargetBatches: true,
            IncludeSqliteTargets: false)
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
