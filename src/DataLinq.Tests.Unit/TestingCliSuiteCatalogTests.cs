using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class TestingCliSuiteCatalogTests
{
    [Test]
    public async Task AllSuites_MatchTheCanonicalFullMatrixShape()
    {
        var root = RepositoryRootLocator.Find();
        var catalog = File.ReadAllText(Path.Combine(
            root,
            "src",
            "DataLinq.Testing.CLI",
            "Selection",
            "TestCliSuiteCatalog.cs"));
        var suiteModel = File.ReadAllText(Path.Combine(
            root,
            "src",
            "DataLinq.Testing.CLI",
            "Selection",
            "TestCliSuite.cs"));
        var matches = Regex.Matches(
            catalog,
            """Name:\s*(?<name>\w+)Suite,.*?UsesTargetBatches:\s*(?<batches>true|false),\s*IncludeSqliteTargets:\s*(?<sqlite>true|false)\)""",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        var actual = matches.Cast<Match>()
            .Select(static match => (
                Name: match.Groups["name"].Value,
                UsesTargetBatches: bool.Parse(match.Groups["batches"].Value),
                IncludeSqliteTargets: bool.Parse(match.Groups["sqlite"].Value)))
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(
        [
            ("Generators", false, false),
            ("Unit", false, false),
            ("Memory", false, false),
            ("Compliance", true, true),
            ("MySql", true, false)
        ]);
        await Assert.That(suiteModel).Contains("bool IncludeSqliteTargets);");
        await Assert.That(suiteModel).DoesNotContain("IncludeSqliteTargets =");
    }
}
