using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public class IncrementalGeneratorBehaviorTests
{
    private static readonly string SourcePath = GeneratorTestPaths.TestModel("IncrementalModel.cs");

    private const string InitialSource = """
        using DataLinq;
        using DataLinq.Attributes;
        using DataLinq.Instances;
        using DataLinq.Interfaces;
        using DataLinq.Mutation;

        namespace IncrementalTests;

        public partial class IncrementalDb : IDatabaseModel
        {
            public IncrementalDb(DataSourceAccess dsa){}
            public DbRead<IncrementalRow> Rows { get; }
        }

        [Table("rows")]
        public abstract partial class IncrementalRow(IRowData rowData, IDataSourceAccess dataSource)
            : Immutable<IncrementalRow, IncrementalDb>(rowData, dataSource), ITableModel<IncrementalDb>
        {
            [PrimaryKey, AutoIncrement, Column("id")]
            public abstract int? Id { get; }
        }
        """;

    private const string TriviaOnlySource = """
        using DataLinq;
        using DataLinq.Attributes;
        using DataLinq.Instances;
        using DataLinq.Interfaces;
        using DataLinq.Mutation;

        namespace IncrementalTests;

        // This comment intentionally changes source text without changing model structure.
        public partial class IncrementalDb
            : IDatabaseModel
        {
            public IncrementalDb(DataSourceAccess dsa){}
            public DbRead<IncrementalRow> Rows { get; }
        }

        [Table("rows")]
        public    abstract    partial    class    IncrementalRow(IRowData rowData, IDataSourceAccess dataSource)
            : Immutable<IncrementalRow, IncrementalDb>(rowData, dataSource),
              ITableModel<IncrementalDb>
        {
            [PrimaryKey, AutoIncrement, Column("id")] public abstract int? Id { get; }
        }
        """;

    [Test]
    public async Task TriviaOnlyModelChanges_DoNotModifyStructuralDeclarationSteps()
    {
        var driver = CreateTrackedDriver();

        driver = driver.RunGenerators(CreateCompilation(InitialSource));
        driver = driver.RunGenerators(CreateCompilation(TriviaOnlySource));

        var generatorResult = driver.GetRunResult().Results.Single();

        AssertStepOutputsWereReused(generatorResult, ModelGeneratorTrackingNames.ModelDeclarations);
        AssertStepOutputsWereReused(generatorResult, ModelGeneratorTrackingNames.CollectedModelDeclarations);
        AssertStepOutputsWereReused(generatorResult, ModelGeneratorTrackingNames.MetadataResults);

        await Assert.That(generatorResult.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    private static GeneratorDriver CreateTrackedDriver()
    {
        IIncrementalGenerator generator = new ModelGenerator();
        return CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));
    }

    private static Compilation CreateCompilation(string source)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.AddRange(
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        ]);

        return CSharpCompilation.Create(
            "IncrementalTestAssembly",
            [CSharpSyntaxTree.ParseText(source, path: SourcePath)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static void AssertStepOutputsWereReused(GeneratorRunResult generatorResult, string trackingName)
    {
        if (!generatorResult.TrackedSteps.TryGetValue(trackingName, out var steps))
            throw new InvalidOperationException($"Tracked step '{trackingName}' was not recorded.");

        var outputReasons = steps
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason)
            .ToArray();

        if (outputReasons.Length == 0)
            throw new InvalidOperationException($"Tracked step '{trackingName}' did not record outputs.");

        var unexpectedReasons = outputReasons
            .Where(static reason => reason is not IncrementalStepRunReason.Cached and not IncrementalStepRunReason.Unchanged)
            .Distinct()
            .ToArray();

        if (unexpectedReasons.Length > 0)
            throw new InvalidOperationException(
                $"Tracked step '{trackingName}' produced unexpected run reasons: {string.Join(", ", unexpectedReasons)}.");
    }
}
