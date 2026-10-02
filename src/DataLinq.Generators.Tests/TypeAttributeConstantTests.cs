using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public class TypeAttributeConstantTests : GeneratorTestBase
{
    [Test]
    [Arguments("DatabaseType.MariaDB, \"varchar\", 1000")]
    [Arguments("DatabaseType.MariaDB, \"varchar\", MaxLength")]
    [Arguments("DatabaseType.MariaDB, \"varchar\", ColumnLimits.Length")]
    [Arguments("DatabaseType.MariaDB, \"varchar\", ColumnLimits.Length / 2 + 500")]
    [Arguments("ColumnLimits.Provider, ColumnLimits.Name, ColumnLimits.Length")]
    [Arguments("length: ColumnLimits.Length, name: ColumnLimits.Name, databaseType: ColumnLimits.Provider")]
    public async Task ValidConstants_CompileAndPopulateLengthMetadata(string arguments)
    {
        var compilation = CreateCompilation(arguments);
        var (driver, output) = Run(CreateDriver(), compilation);
        await Assert.That(output.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
        var type = GetEmittedType(driver);
        await Assert.That(type.Name).IsEqualTo("varchar");
        await Assert.That(type.Length).IsEqualTo((ulong?)1000);
        await Assert.That(type.DatabaseType.ToString()).IsEqualTo("MariaDB");
    }

    [Test]
    public async Task ReferencedAssemblyConstants_CompileAndPopulateAllTypeArguments()
    {
        var compilation = CreateCompilation("ColumnLimits.Provider, ColumnLimits.Name, ColumnLimits.Length, ColumnLimits.Decimals, ColumnLimits.Signed", referencedConstants: true);
        var (driver, output) = Run(CreateDriver(), compilation);
        await Assert.That(output.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
        var type = GetEmittedType(driver);
        await Assert.That(type.Length).IsEqualTo((ulong?)1000);
        await Assert.That(type.Decimals).IsEqualTo((uint?)2);
        await Assert.That(type.Signed).IsFalse();
    }

    [Test]
    [Arguments("\"varchar\", MaxLength")]
    [Arguments("ColumnLimits.Name, ColumnLimits.Length, ColumnLimits.Signed")]
    public async Task DefaultProviderOverloads_ResolveConstants(string arguments)
    {
        var (driver, output) = Run(CreateDriver(), CreateCompilation(arguments));
        await Assert.That(output.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
        var type = GetEmittedType(driver);
        await Assert.That(type.DatabaseType.ToString()).IsEqualTo("Default");
        await Assert.That(type.Name).IsEqualTo("varchar");
        await Assert.That(type.Length).IsEqualTo((ulong?)1000);
    }

    [Test]
    [Arguments("-1")]
    [Arguments("ColumnLimits.Missing")]
    [Arguments("System.DateTime.Now.Ticks")]
    [Arguments("18446744073709551616")]
    public async Task InvalidLengths_RetainAttributeDiagnostics(string length)
    {
        var (driver, _) = Run(CreateDriver(), CreateCompilation($"DatabaseType.MariaDB, \"varchar\", {length}"));
        var diagnostic = driver.GetRunResult().Results.Single().Diagnostics.Single(x => x.Id == "DLG001");
        await Assert.That(diagnostic.Location.IsInSource).IsTrue();
        await Assert.That(diagnostic.Location.SourceTree!.FilePath).IsEqualTo("Model.cs");
        var source = diagnostic.Location.SourceTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(source).Contains("Type(");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConstantOnlyChanges_RefreshIncrementalMetadataAndEmission(bool referencedConstants)
    {
        var initial = CreateCompilation("DatabaseType.MariaDB, \"varchar\", ColumnLimits.Length", referencedConstants: referencedConstants);
        var (driver, _) = Run(CreateDriver(), initial);
        await Assert.That(GetEmittedType(driver).Length).IsEqualTo((ulong?)1000);
        foreach (var length in new[] { 2000, 1000 })
        {
            var updated = referencedConstants
                ? initial.RemoveReferences(initial.References.Last()).AddReferences(CreateConstantsReference(length))
                : initial.ReplaceSyntaxTree(initial.SyntaxTrees.Last(), CSharpSyntaxTree.ParseText(ConstantsSource(length), path: "Constants.cs"));
            (driver, var output) = Run(driver, updated);
            await Assert.That(output.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
            await Assert.That(GetEmittedType(driver).Length).IsEqualTo((ulong?)length);
            var (fresh, _) = Run(CreateDriver(), updated);
            var actualSources = driver.GetRunResult().Results.Single().GeneratedSources.Select(x => x.SourceText.ToString()).ToArray();
            var freshSources = fresh.GetRunResult().Results.Single().GeneratedSources.Select(x => x.SourceText.ToString()).ToArray();
            await Assert.That(actualSources).IsEquivalentTo(freshSources);
        }
    }

    private static DataLinq.Metadata.DatabaseColumnType GetEmittedType(GeneratorDriver driver)
        => driver.GetRunResult().Results.Single().TrackedSteps["DataLinq.DatabaseEmissions"]
            .SelectMany(x => x.Outputs).Select(x => (DatabaseEmissionOutput)x.Value).Single()
            .Database.TableModels.Single().Table.Columns.Single(x => x.DbName == "reference").DbTypes.Single();

    private static GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(
        [new ModelGenerator().AsSourceGenerator()],
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static (GeneratorDriver Driver, Compilation Output) Run(GeneratorDriver driver, Compilation compilation)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver, output);
    }

    private static CSharpCompilation CreateCompilation(string arguments, bool referencedConstants = false)
    {
        var references = GeneratorMetadataReferenceCache.GetReferences(
            excludedAssemblies: [typeof(ModelGenerator).Assembly], additionalLocations: [GetDataLinqRuntimeAssemblyPath()]);
        var source = """
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;
            namespace ConstantTests;
            [Database("constants")]
            public partial class ConstantDb(DataSourceAccess source) : IDatabaseModel<ConstantDb>
            {
                public DbRead<ConstantRow> Rows { get; } = new(source);
            }
            [Table("constant_rows")]
            public abstract partial class ConstantRow(IRowData data, IDataSourceAccess source)
                : Immutable<ConstantRow, ConstantDb>(data, source), ITableModel<ConstantDb>
            {
                public const int MaxLength = 1000;
                [PrimaryKey, Column("id")]
                public abstract int Id { get; }
                [Column("reference"), Type(ARGUMENTS)]
                public abstract string Reference { get; }
            }
            """.Replace("ARGUMENTS", arguments);
        var compilation = CSharpCompilation.Create("ConstantConsumer", [CSharpSyntaxTree.ParseText(source, path: "Model.cs")], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable));
        return referencedConstants
            ? compilation.AddReferences(CreateConstantsReference(1000))
            : compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(ConstantsSource(1000), path: "Constants.cs"));
    }

    private static string ConstantsSource(int length) => $$"""
        namespace ConstantTests;
        public static class ColumnLimits
        {
            public const int Length = {{length}};
            public const uint Decimals = 2;
            public const bool Signed = false;
            public const DataLinq.DatabaseType Provider = DataLinq.DatabaseType.MariaDB;
            public const string Name = "varchar";
        }
        """;

    private static PortableExecutableReference CreateConstantsReference(int length)
    {
        var references = GeneratorMetadataReferenceCache.GetReferences(
            excludedAssemblies: [typeof(ModelGenerator).Assembly], additionalLocations: [GetDataLinqRuntimeAssemblyPath()]);
        var compilation = CSharpCompilation.Create("ConstantsLibrary", [CSharpSyntaxTree.ParseText(ConstantsSource(length))], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
