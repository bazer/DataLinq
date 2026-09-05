using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public sealed class IncrementalGeneratorScaleTests : GeneratorTestBase
{
    private const int DatabaseCount = 6;
    private const int TablesPerDatabase = 8;
    private const string EmissionsStep = "DataLinq.DatabaseEmissions";
    private const string ConverterSource = """
        using DataLinq;
        namespace Scale;
        public readonly record struct Code(int Value);
        public sealed class CodeConverter : DataLinqScalarConverter<Code, int>
        {
            public override int ToProvider(Code value, in ScalarConversionContext context) => value.Value;
            public override Code FromProvider(int value, in ScalarConversionContext context) => new(value);
        }
        """;

    [Test]
    public async Task MultiDatabaseEditWorkload()
    {
        var trees = Enumerable.Range(0, DatabaseCount).Select(CreateDatabaseTree).ToList();
        var helper = Parse("namespace Scale; public static class Helper { public static int Get() => 0; }", "Helper.cs");
        var converter = Parse(ConverterSource, "Converter.cs");
        trees.Add(helper);
        trees.Add(converter);
        var compilation = CSharpCompilation.Create("ScaleTest", trees,
            GeneratorMetadataReferenceCache.GetReferences(excludedAssemblies: [typeof(ModelGenerator).Assembly],
                additionalLocations: [GetDataLinqRuntimeAssemblyPath()]),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ModelGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);
        await Assert.That(driver.GetRunResult().Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(driver.GetRunResult().Results.Single().GeneratedSources.Length).IsEqualTo(DatabaseCount * (TablesPerDatabase + 1));
        await Assert.That(string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.ToString())))
            .Contains("new global::DataLinq.Core.Factories.MetadataScalarConverterDraft(");
        var errors = compilation.AddSyntaxTrees(driver.GetRunResult().GeneratedTrees).GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
            throw new InvalidOperationException(string.Join("\n", errors.Select(error => error.ToString())));
        var observations = new List<string>();
        var originalModel = trees[0];
        var currentModel = originalModel;
        for (var iteration = 1; iteration <= 4; iteration++)
        {
            var nextHelper = Parse($"namespace Scale; public static class Helper {{ public static int Get() => {iteration}; }}", "Helper.cs");
            compilation = compilation.ReplaceSyntaxTree(helper, nextHelper);
            helper = nextHelper;
            driver = Measure(driver, compilation, "unrelated", observations);

            var nextModel = Parse(originalModel.ToString().Replace("Column(\"value_0\")", $"Column(\"value_{iteration}_changed\")"), "Db0.cs");
            compilation = compilation.ReplaceSyntaxTree(currentModel, nextModel);
            currentModel = nextModel;
            driver = Measure(driver, compilation, "one-database", observations);

            var nextConverter = Parse(iteration % 2 == 1
                ? ConverterSource.Replace("<Code, int>", "<Code, long>").Replace("override int ToProvider", "override long ToProvider").Replace("FromProvider(int value", "FromProvider(long value").Replace("=> new(value)", "=> new((int)value)")
                : ConverterSource, "Converter.cs");
            compilation = compilation.ReplaceSyntaxTree(converter, nextConverter);
            converter = nextConverter;
            driver = Measure(driver, compilation, "converter-contract", observations);
            var metadata = driver.GetRunResult().Results.Single().GeneratedSources.Single(source => source.HintName.EndsWith("Db0.DataLinqMetadata.cs", StringComparison.Ordinal)).SourceText.ToString();
            await Assert.That(metadata).Contains(iteration % 2 == 1 ? "typeof(global::System.Int64)" : "typeof(global::System.Int32)");
            await Assert.That(driver.GetRunResult().Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
        }

        var outputPath = Environment.GetEnvironmentVariable("DATALINQ_GENERATOR_MEASUREMENT_PATH");
        if (!string.IsNullOrWhiteSpace(outputPath))
            File.WriteAllLines(outputPath, observations);
    }

    private static GeneratorDriver Measure(GeneratorDriver driver, Compilation compilation, string scenario, List<string> observations)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        driver = driver.RunGenerators(compilation);
        clock.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var result = driver.GetRunResult().Results.Single();
        var reasons = result.TrackedSteps.TryGetValue(EmissionsStep, out var steps)
            ? string.Join(",", steps.SelectMany(s => s.Outputs).Select(o => o.Reason)) : "untracked-all-databases";
        observations.Add(FormattableString.Invariant($"{scenario}: elapsed-ms={clock.Elapsed.TotalMilliseconds:F3}; allocated-bytes={allocated}; emissions={reasons}"));
        var outputs = steps.SelectMany(step => step.Outputs).ToArray();
        var modified = outputs.Count(output => output.Reason == IncrementalStepRunReason.Modified);
        var expectedModified = scenario == "unrelated" ? 0 : 1;
        if (outputs.Length != DatabaseCount || modified != expectedModified ||
            outputs.Count(output => output.Reason == IncrementalStepRunReason.Cached) != DatabaseCount - expectedModified)
            throw new InvalidOperationException($"Unexpected emission reuse for {scenario}: {reasons}");
        return driver;
    }

    [Test]
    public async Task ConverterFailureAndRecoveryMatchFreshGeneration()
    {
        var model = CreateDatabaseTree(0);
        var converter = Parse(ConverterSource, "Converter.cs");
        var compilation = CreateCompilation(model, converter);
        var driver = CreateDriver().RunGenerators(compilation);
        var broken = Parse(ConverterSource.Replace("sealed class CodeConverter", "abstract class CodeConverter"), "Converter.cs");
        compilation = compilation.ReplaceSyntaxTree(converter, broken);
        driver = driver.RunGenerators(compilation);
        await Assert.That(driver.GetRunResult().Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsTrue();
        await Assert.That(driver.GetRunResult().GeneratedTrees).IsEmpty();
        await AssertEquivalentToFresh(driver, compilation);
        compilation = compilation.ReplaceSyntaxTree(broken, converter);
        driver = driver.RunGenerators(compilation);
        await Assert.That(driver.GetRunResult().Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsFalse();
        await AssertEquivalentToFresh(driver, compilation);
    }

    [Test]
    public async Task ImportEditsAndMovedDefaultDiagnosticsUseCurrentSemanticsAndTrees()
    {
        var text = "using ValueType = System.String;\n" + CreateDatabaseTree(1).ToString()
            .Replace("[Column(\"value_0\")] public abstract int", "[Default(0), Column(\"value_0\")] public abstract ValueType");
        var model = Parse(text, "Db1.cs");
        var compilation = CreateCompilation(model);
        var driver = CreateDriver().RunGenerators(compilation);
        await Assert.That(driver.GetRunResult().Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsEqualTo(TablesPerDatabase);
        var moved = Parse("// Move every declaration without changing its structure.\n" + text, "Db1.cs");
        compilation = compilation.ReplaceSyntaxTree(model, moved);
        driver = driver.RunGenerators(compilation);
        foreach (var diagnostic in driver.GetRunResult().Diagnostics)
        {
            await Assert.That(diagnostic.Location.SourceTree).IsSameReferenceAs(moved);
            await Assert.That(moved.GetText().ToString(diagnostic.Location.SourceSpan)).IsEqualTo("0");
        }
        await AssertEquivalentToFresh(driver, compilation);

        var fixedModel = Parse(text.Replace("System.String", "System.Int32"), "Db1.cs");
        compilation = compilation.ReplaceSyntaxTree(moved, fixedModel);
        driver = driver.RunGenerators(compilation);
        await Assert.That(driver.GetRunResult().Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsFalse();
        await AssertEquivalentToFresh(driver, compilation);
    }

    private static async Task AssertEquivalentToFresh(GeneratorDriver incremental, Compilation compilation)
    {
        var fresh = CreateDriver().RunGenerators(compilation).GetRunResult();
        static string Sources(GeneratorDriverRunResult result) => string.Join("\n", result.Results.Single().GeneratedSources
            .OrderBy(source => source.HintName, StringComparer.Ordinal).Select(source => source.HintName + "\n" + source.SourceText));
        await Assert.That(Sources(incremental.GetRunResult())).IsEqualTo(Sources(fresh));
        await Assert.That(string.Join("\n", incremental.GetRunResult().Diagnostics.Select(diagnostic => diagnostic.ToString())))
            .IsEqualTo(string.Join("\n", fresh.Diagnostics.Select(diagnostic => diagnostic.ToString())));
    }

    private static GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create([new ModelGenerator().AsSourceGenerator()],
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees) => CSharpCompilation.Create("BoundaryTest", trees,
        GeneratorMetadataReferenceCache.GetReferences(excludedAssemblies: [typeof(ModelGenerator).Assembly],
            additionalLocations: [GetDataLinqRuntimeAssemblyPath()]),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable));

    private static SyntaxTree CreateDatabaseTree(int database)
    {
        var source = new StringBuilder("using DataLinq; using DataLinq.Attributes; using DataLinq.Instances; using DataLinq.Interfaces; using DataLinq.Mutation; namespace Scale;\n");
        source.Append($"public partial class Db{database}(DataSourceAccess access) : IDatabaseModel<Db{database}> {{\n");
        for (var table = 0; table < TablesPerDatabase; table++)
            source.Append($"public DbRead<Row{database}_{table}> Rows{table} {{ get; }} = new(access);\n");
        source.Append("}\n");
        for (var table = 0; table < TablesPerDatabase; table++)
        {
            source.Append($"[Table(\"row_{table}\")] public abstract partial class Row{database}_{table}(IRowData row, IDataSourceAccess access) : Immutable<Row{database}_{table}, Db{database}>(row, access), ITableModel<Db{database}> {{\n[PrimaryKey, Column(\"id\")] public abstract int Id {{ get; }}\n");
            for (var property = 0; property < 10; property++)
                source.Append($"[Column(\"value_{property}\")] public abstract int Value{property} {{ get; }}\n");
            if (database == 0 && table == 0)
                source.Append("[Column(\"code\"), ScalarConverter(typeof(CodeConverter))] public abstract Code Code { get; }\n");
            source.Append("}\n");
        }
        return Parse(source.ToString(), $"Db{database}.cs");
    }

    private static SyntaxTree Parse(string source, string file) => CSharpSyntaxTree.ParseText(source, path: GeneratorTestPaths.TestModel(file));
}
