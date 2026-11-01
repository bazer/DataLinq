using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public sealed class PartialCacheGeneratorTests : GeneratorTestBase
{
    [Test]
    [Arguments(true, false, true, true)]
    [Arguments(false, true, true, true)]
    [Arguments(true, false, false, true)]
    [Arguments(false, false, false, true)]
    [Arguments(true, false, true, false)]
    [Arguments(false, true, true, false)]
    [Arguments(true, false, false, false)]
    [Arguments(false, false, false, false)]
    public async Task CacheAttributes_CompileAndMatchWhetherOnMainOrSecondaryDeclaration(
        bool databaseCache, bool tableCache, bool tableOverride, bool secondary)
    {
        var dbAttribute = databaseCache ? "[UseCache]" : "[UseCache(useCache: false)]";
        var rowAttribute = tableOverride ? $"[UseCache({tableCache.ToString().ToLowerInvariant()})]" : "";
        var main = Parse(Model("Cache", secondary ? "" : dbAttribute, secondary ? "" : rowAttribute), "Cache.cs");
        var partial = Parse(Partials(secondary ? dbAttribute : "", secondary ? rowAttribute : ""), "Cache.Partial.cs");
        var compilation = CreateCompilation(main, partial);
        var driver = CreateDriver().RunGenerators(compilation);
        await AssertNoErrors(compilation, driver);
        await AssertCache(driver, databaseCache, tableOverride ? tableCache : null);
    }

    [Test]
    public async Task SecondaryAttributeEdits_AddRemoveToggleAndMove_UpdateOnlyTheirDatabase()
    {
        var main = Parse(Model("Cache", "", ""), "Cache.cs");
        var other = Parse(Model("Other", "", ""), "Other.cs");
        var partial = Parse(Partials("", ""), "Cache.Partial.cs");
        var compilation = CreateCompilation(main, other, partial);
        var driver = CreateDriver().RunGenerators(compilation);
        await AssertCache(driver, false, null);
        var originalOther = Sources(driver).Where(pair => pair.Key.Contains("Other", StringComparison.Ordinal)).ToArray();
        foreach (var (db, row, expectedDb, expectedRow) in new (string, string, bool, bool?)[]
        {
            ("[UseCache]", "[UseCache(false)]", true, false),
            ("[UseCache(false)]", "[UseCache(true)]", false, true),
            ("", "", false, null),
            ("[UseCache]", "[UseCache(false)]", true, false)
        })
        {
            var next = Parse(Partials(db, row), "Cache.Partial.cs");
            compilation = compilation.ReplaceSyntaxTree(partial, next);
            partial = next;
            driver = driver.RunGenerators(compilation);
            await AssertCache(driver, expectedDb, expectedRow);
            await AssertNoErrors(compilation, driver);
            await Assert.That(Sources(driver).ToArray()).IsEquivalentTo(Sources(CreateDriver().RunGenerators(compilation)).ToArray());
            await Assert.That(Sources(driver).Where(pair => pair.Key.Contains("Other", StringComparison.Ordinal)).ToArray()).IsEquivalentTo(originalOther);
            var result = driver.GetRunResult().Results.Single();
            await Assert.That(result.TrackedSteps[ModelGeneratorTrackingNames.MetadataResults]
                .SelectMany(step => step.Outputs).All(output => output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsTrue();
            await Assert.That(result.TrackedSteps["DataLinq.DatabaseEmissions"].SelectMany(step => step.Outputs)
                .Count(output => output.Reason == IncrementalStepRunReason.Modified)).IsEqualTo(1);
        }
        // Move unchanged attributes to the main declaration, then remove the partial file.
        var moved = Parse(Model("Cache", "[UseCache]", "[UseCache(false)]"), "Cache.cs");
        compilation = compilation.ReplaceSyntaxTree(main, moved).RemoveSyntaxTrees(partial);
        driver = driver.RunGenerators(compilation);
        await AssertCache(driver, true, false);
        await AssertNoErrors(compilation, driver);
        await Assert.That(Sources(driver).ToArray()).IsEquivalentTo(Sources(CreateDriver().RunGenerators(compilation)).ToArray());
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExplicitFalseDatabaseAttribute_AddAndRemove_RefreshPublicMetadata(bool initiallyExplicit)
    {
        var main = Parse(Model("Cache", "", ""), "Cache.cs");
        var other = Parse(Model("Other", "", ""), "Other.cs");
        var partial = Parse(Partials(initiallyExplicit ? "[UseCache(false)]" : "", ""), "Cache.Partial.cs");
        var compilation = CreateCompilation(main, other, partial);
        var driver = CreateDriver().RunGenerators(compilation);
        await AssertNoErrors(compilation, driver);
        var originalOther = Sources(driver).Where(pair => pair.Key.Contains("Other", StringComparison.Ordinal)).ToArray();

        foreach (var explicitAttribute in new[] { !initiallyExplicit, initiallyExplicit })
        {
            var next = Parse(Partials(explicitAttribute ? "[UseCache(false)]" : "", ""), "Cache.Partial.cs");
            compilation = compilation.ReplaceSyntaxTree(partial, next);
            partial = next;
            driver = driver.RunGenerators(compilation);

            await AssertNoErrors(compilation, driver);
            await AssertCache(driver, false, null);
            var sources = Sources(driver);
            var fresh = CreateDriver().RunGenerators(compilation);
            await AssertNoErrors(compilation, fresh);
            await Assert.That(sources.ToArray()).IsEquivalentTo(Sources(fresh).ToArray());
            var metadata = sources.Single(pair => pair.Key.EndsWith("CacheDb.DataLinqMetadata.cs", StringComparison.Ordinal)).Value;
            await Assert.That(metadata.Contains("UseCacheAttribute(false)", StringComparison.Ordinal)).IsEqualTo(explicitAttribute);
            await Assert.That(sources.Where(pair => pair.Key.Contains("Other", StringComparison.Ordinal)).ToArray()).IsEquivalentTo(originalOther);
        }
    }

    [Test]
    public async Task QualifiedAttributeAndConstantBooleanOnSecondaryDeclaration_AreBoundSemantically()
    {
        var main = Parse(Model("Cache", "", ""), "Cache.cs");
        var partial = Parse(Partials("[global::DataLinq.Attributes.UseCacheAttribute(Flags.Enabled)]", "[UseCache(Flags.Disabled)]")
            + "\npublic static class Flags { public const bool Enabled = true; public const bool Disabled = false; }", "Cache.Partial.cs");
        var compilation = CreateCompilation(main, partial);
        var driver = CreateDriver().RunGenerators(compilation);
        await AssertNoErrors(compilation, driver);
        await AssertCache(driver, true, false);
    }

    [Test]
    [Arguments("[UseCache, UseCache(false)]", "multiple [UseCache]")]
    [Arguments("[UseCache(\"invalid\")]", "constant boolean")]
    public async Task InvalidSecondaryAttributes_ReportDiagnosticsInTheirOwnFile(string attribute, string expected)
    {
        var main = Parse(Model("Cache", "", ""), "Cache.cs");
        var partial = Parse(Partials(attribute, ""), "Cache.Partial.cs");
        var driver = CreateDriver().RunGenerators(CreateCompilation(main, partial));
        var error = driver.GetRunResult().Diagnostics.Single(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        await Assert.That(error.GetMessage()).Contains(expected);
        await Assert.That(error.Location.SourceTree).IsSameReferenceAs(partial);
        await Assert.That(partial.GetText().ToString(error.Location.SourceSpan)).Contains("UseCache");
    }

    private static async Task AssertCache(GeneratorDriver driver, bool databaseCache, bool? tableCache)
    {
        var emission = driver.GetRunResult().Results.Single().TrackedSteps["DataLinq.DatabaseEmissions"]
            .SelectMany(step => step.Outputs).Select(output => (DatabaseEmissionOutput)output.Value)
            .Single(output => output.Database.CsType.Name == "CacheDb");
        var database = emission.Database;
        await Assert.That(database.UseCache).IsEqualTo(databaseCache);
        await Assert.That(database.TableModels.Single().Table.UseCache).IsEqualTo(tableCache ?? databaseCache);
        await Assert.That(database.TableModels.Single().Table.explicitUseCache).IsEqualTo(tableCache);
        await Assert.That(database.IsFrozen).IsTrue();
        var metadata = Sources(driver).Single(pair => pair.Key.EndsWith("CacheDb.DataLinqMetadata.cs", StringComparison.Ordinal)).Value;
        await Assert.That(metadata).Contains($"UseCache = {databaseCache.ToString().ToLowerInvariant()},");
        await Assert.That(metadata).Contains($"UseCache = {(tableCache?.ToString().ToLowerInvariant() ?? "null")},");
    }

    private static async Task AssertNoErrors(Compilation compilation, GeneratorDriver driver)
    {
        var errors = compilation.AddSyntaxTrees(driver.GetRunResult().GeneratedTrees).GetDiagnostics()
            .Concat(driver.GetRunResult().Diagnostics).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        await Assert.That(string.Join("\n", errors.Select(error => error.ToString()))).IsEqualTo("");
    }

    private static Dictionary<string, string> Sources(GeneratorDriver driver) => driver.GetRunResult().Results.Single().GeneratedSources
        .OrderBy(source => source.HintName, StringComparer.Ordinal).ToDictionary(source => source.HintName, source => source.SourceText.ToString());
    private static GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create([new ModelGenerator().AsSourceGenerator()],
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
    private static Compilation CreateCompilation(params SyntaxTree[] trees) => CSharpCompilation.Create("PartialCacheTest", trees,
        GeneratorMetadataReferenceCache.GetReferences(excludedAssemblies: [typeof(ModelGenerator).Assembly], additionalLocations: [GetDataLinqRuntimeAssemblyPath()]),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable));
    private static SyntaxTree Parse(string source, string file) => CSharpSyntaxTree.ParseText(source, path: GeneratorTestPaths.TestModel(file));
    private static string Partials(string dbAttribute, string rowAttribute) => $$"""
        using DataLinq.Attributes;
        namespace PartialCache;
        {{dbAttribute}} public partial class CacheDb { }
        {{rowAttribute}} public abstract partial class CacheRow { }
        """;
    private static string Model(string prefix, string dbAttribute, string rowAttribute) => $$"""
        using DataLinq;
        using DataLinq.Attributes;
        using DataLinq.Instances;
        using DataLinq.Interfaces;
        using DataLinq.Mutation;
        namespace PartialCache;
        {{dbAttribute}} public partial class {{prefix}}Db(DataSourceAccess access) : IDatabaseModel<{{prefix}}Db>
        { public DbRead<{{prefix}}Row> Rows { get; } = new(access); }
        [Table("rows")] {{rowAttribute}} public abstract partial class {{prefix}}Row(IRowData row, IDataSourceAccess access)
            : Immutable<{{prefix}}Row, {{prefix}}Db>(row, access), ITableModel<{{prefix}}Db>
        { [PrimaryKey, Column("id")] public abstract int Id { get; } }
        """;
}
