using System.Linq;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DataLinq.Tests;

public class SourceGeneratorTests
{
    //public static DatabaseFixture fixture;

    //static SourceGeneratorTests()
    //{
    //    fixture = new DatabaseFixture();
    //}

    //public static IEnumerable<object[]> GetEmployees()
    //{
    //    foreach (var db in fixture.AllEmployeesDb)
    //        yield return new object[] { db };
    //}

    //public SourceGeneratorTests()
    //{
    //    foreach (var employeesDb in fixture.AllEmployeesDb)
    //    {
    //        employeesDb.Provider.State.ClearCache();
    //    }
    //}

    [Fact]
    public void TestGeneratedProxyClass()
    {
        // The source code to be used as input for the generator
        var inputCode = @"
        namespace TestNamespace
        {
            public partial class TestModel : ITableModel<Employees>
            {
                [PrimaryKey]
                [Column(""Name"")]
                public string Name { get; set; }
            }
        }";

        var inputDbCode = @"
        using System;
        using DataLinq;
        using DataLinq.Interfaces;
        using DataLinq.Attributes;

        namespace DataLinq.Tests.Models;

        [Database(""employees"")]
        public interface Employees : IDatabaseModel
        {
            DbRead<TestModel> TestModels { get; }
        }";

        // Create the syntax tree from the source code
        //var syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(inputCode));

        // Set up compilation
        var compilation = CSharpCompilation.Create("TestAssembly",
            new[]
            {
                CSharpSyntaxTree.ParseText(SourceText.From(inputCode)),
                CSharpSyntaxTree.ParseText(SourceText.From(inputDbCode))
            },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Create the generator driver
        var generator = new ModelGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        // Run the generator
        var driver2 = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        // Check for diagnostics
        //Assert.Empty(diagnostics);

        if (diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).Count() > 0)
        {
            //diagnostics.ToList().ForEach(d => System.Console.WriteLine(d));
            Assert.Fail(string.Join('\n', diagnostics.Select(d => d.ToString())));
        }

        // Verify the generated code
        var generatedTree = outputCompilation.SyntaxTrees.Last();
        var generatedCode = generatedTree.ToString();

        // Check that the generated code contains the expected proxy class
        Assert.Contains("public record TestModelProxy", generatedCode);
    }

}