using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public class EnumDefaultGenerationTests : GeneratorTestBase
{
    private const string ModelSource = """
        using DataLinq;
        using DataLinq.Attributes;
        using DataLinq.Instances;
        using DataLinq.Interfaces;
        using DataLinq.Mutation;

        namespace EnumDefaultCompilation;
        public enum RowStatus : int { Inactive = 1, Active = 2 }
        [Database("enum_defaults")]
        public partial class ExampleDb(DataSourceAccess source) : IDatabaseModel<ExampleDb>
        {
            public DbRead<ExampleRow> Rows { get; } = new(source);
        }
        [Table("example_rows")]
        public abstract partial class ExampleRow(IRowData data, IDataSourceAccess source)
            : Immutable<ExampleRow, ExampleDb>(data, source), ITableModel<ExampleDb>
        {
            [PrimaryKey, Column("id"), Type(DatabaseType.MariaDB, "int", 11)]
            public abstract int Id { get; }
            [Column("status"), Type(DatabaseType.MariaDB, "int", 11), Default(RowStatus.Inactive)]
            public abstract RowStatus Status { get; }
        }
        """;

    [Test]
    [Arguments("sbyte", "(sbyte)1")]
    [Arguments("byte", "(byte)1")]
    [Arguments("short", "(short)1")]
    [Arguments("ushort", "(ushort)1")]
    [Arguments("int", "1")]
    [Arguments("uint", "1U")]
    [Arguments("long", "1L")]
    [Arguments("ulong", "1UL")]
    public async Task SymbolicDefaults_CompileWithUnderlyingIntegralMetadata(string underlyingType, string literal)
    {
        var source = ModelSource.Replace("enum RowStatus : int", $"enum RowStatus : {underlyingType}");
        var (compilation, diagnostics, generated) = RunGeneratorWithDiagnostics(
            [CSharpSyntaxTree.ParseText(source, path: GeneratorTestPaths.TestModel("EnumDefaultModel.cs"))]);
        await Assert.That(diagnostics.Concat(compilation.GetDiagnostics()).Where(x => x.Severity == DiagnosticSeverity.Error)).IsEmpty();
        var code = string.Join("\n", generated.Select(x => x.ToString()));
        await Assert.That(code).Contains($"new global::DataLinq.Attributes.DefaultAttribute({literal}, \"RowStatus.Inactive\")");
        await Assert.That(code).Contains("this.Status = RowStatus.Inactive;");
    }

    [Test]
    public async Task ReferencedConstantEdits_RefreshMetadataOnTheSameGeneratorDriver()
    {
        var model = CSharpSyntaxTree.ParseText(
            ModelSource.Replace("Default(RowStatus.Inactive)", "Default((RowStatus)Defaults.State)"),
            path: GeneratorTestPaths.TestModel("EnumDefaultModel.cs"));
        var constants = ConstantTree(1);
        var compilation = CSharpCompilation.Create("EnumDefaultIncremental", [model, constants],
            GeneratorMetadataReferenceCache.GetReferences(excludedAssemblies: [typeof(ModelGenerator).Assembly],
                additionalLocations: [GetDataLinqRuntimeAssemblyPath()]),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModelGenerator().AsSourceGenerator());
        foreach (var value in new[] { 1, 2, 1 })
        {
            var replacement = ConstantTree(value);
            compilation = compilation.ReplaceSyntaxTree(constants, replacement);
            constants = replacement;
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
            await Assert.That(diagnostics.Concat(output.GetDiagnostics()).Where(x => x.Severity == DiagnosticSeverity.Error)).IsEmpty();
            var generated = driver.GetRunResult().Results.Single().GeneratedSources;
            var metadata = generated.Single(x => x.HintName.EndsWith("ExampleDb.DataLinqMetadata.cs", StringComparison.Ordinal)).SourceText.ToString();
            await Assert.That(metadata).Contains($"new global::DataLinq.Attributes.DefaultAttribute({value}, \"(RowStatus)Defaults.State\")");
            var fresh = CSharpGeneratorDriver.Create(new ModelGenerator().AsSourceGenerator()).RunGenerators(compilation);
            await Assert.That(Sources(driver)).IsEqualTo(Sources(fresh));
        }
    }

    private static SyntaxTree ConstantTree(int value) => CSharpSyntaxTree.ParseText(
        $"namespace EnumDefaultCompilation; public static class Defaults {{ public const int State = {value}; }}",
        path: GeneratorTestPaths.TestModel("EnumDefaultConstants.cs"));

    private static string Sources(GeneratorDriver driver) => string.Join("\n", driver.GetRunResult().Results.Single()
        .GeneratedSources.OrderBy(x => x.HintName, StringComparer.Ordinal).Select(x => x.SourceText.ToString()));
}
