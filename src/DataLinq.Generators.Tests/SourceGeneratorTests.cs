using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataLinq.Extensions.Helpers;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DataLinq.Tests;

public class SourceGeneratorTests
{
    private string SyntaxTreesToString(IEnumerable<SyntaxTree> syntaxTrees)
    {
        return syntaxTrees.Select(x => x.ToString()).ToJoinedString();
    }

    private IEnumerable<SyntaxTree> GenerateCodeFromFolder(string[] dirs, bool includeSubfolders)
    {
        var projectRoot = Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.Parent;

        var sources = dirs
            .SelectMany(x => Directory.EnumerateFiles(Path.Combine(projectRoot.FullName, x), "*.cs", enumerationOptions: new EnumerationOptions() { RecurseSubdirectories = includeSubfolders }))
            .Select(x => File.ReadAllText(x));

        return GenerateCode(sources);
    }

    private IEnumerable<SyntaxTree> GenerateCode(params string[] sources)
    {
        return GenerateCode(sources.Select(x => CSharpSyntaxTree.ParseText(SourceText.From(x))));
    }

    private IEnumerable<SyntaxTree> GenerateCode(IEnumerable<string> sources)
    {
        return GenerateCode(sources.Select(x => CSharpSyntaxTree.ParseText(SourceText.From(x))));
    }

    private IEnumerable<SyntaxTree> GenerateCode(IEnumerable<SyntaxTree> syntaxTrees)
    {
        // Set up compilation
        var compilation = CSharpCompilation.Create("TestAssembly",
            syntaxTrees,
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
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        if (diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).Any())
        {
            Assert.Fail(string.Join('\n', diagnostics.Select(d => d.ToString())));
        }

        return outputCompilation.SyntaxTrees;
    }

    [Fact]
    public void TestBasics()
    {
        // The source code to be used as input for the generator
        var inputCode = @"
        namespace TestNamespace;

        public interface ITestModel : ITableModel<ITestDb>
        {
            [PrimaryKey]
            [Column(""Name"")]
            public string Name { get; set; }
        }";

        var inputDbCode = @"
        using System;
        using DataLinq;
        using DataLinq.Interfaces;
        using DataLinq.Attributes;

        namespace DataLinq.Tests.Models;

        [Database(""testdb"")]
        public interface ITestDb : IDatabaseModel
        {
            DbRead<ITestModel> TestModels { get; }
        }";

        // Create the syntax tree from the source code
        //var syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(inputCode));


        var generatedCode = SyntaxTreesToString(GenerateCode(inputDbCode, inputCode));

        //var generatedCode = generatedTree.ToString();

        // Check that the generated code contains the expected proxy class
        Assert.Contains("public partial record TestModel", generatedCode);
        Assert.Contains("public partial class TestDb", generatedCode);
    }


    [Fact]
    public void TestEmployees()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models\\employees\\gen"], true);
        var code = SyntaxTreesToString(syntax);

        Assert.Contains("public partial class ImmutableEmployee", code);
    }

    [Fact]
    public void TestEmployeesOnlyDb()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models\\employees\\gen"], false);
        var code = SyntaxTreesToString(syntax);

        Assert.Contains("public partial class EmployeesDb", code);
    }

    [Fact]
    public void TestAllround()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models\\Allround"], true);
        var code = SyntaxTreesToString(syntax);

        Assert.Contains("public partial class AllroundBenchmark", code);
        Assert.Contains("public partial record Payment", code);
    }

    [Fact]
    public void TestAllroundOnlyDb()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models\\Allround"], false);
        var code = SyntaxTreesToString(syntax);

        Assert.Contains("public partial class AllroundBenchmark", code);
    }

    [Fact]
    public void TestAllModels()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models\\employees\\gen", "DataLinq.Tests.Models\\Allround"], true);
        var code = SyntaxTreesToString(syntax);

        Assert.Contains("public partial class AllroundBenchmark", code);
        Assert.Contains("public partial class EmployeesDb", code);
        Assert.Contains("public partial record Payment", code);
        Assert.Contains("public partial record Employee", code);
    }

}

//public partial class EmployeesDb(DataSourceAccess dataSource) : IEmployees, IDatabaseModelInstance
//{
//    public virtual DbRead<current_dept_emp> current_dept_emp { get; } = new DbRead<current_dept_emp>(dataSource);
//    public virtual DbRead<DepartmentEmployees> DepartmentEmployees { get; }
//    public virtual DbRead<Departments> Departments { get; }
//    public virtual DbRead<dept_emp_latest_date> dept_emp_latest_date { get; }
//    public virtual DbRead<Employees> Employees { get; }
//    public virtual DbRead<Managers> Managers { get; }
//    public virtual DbRead<salaries> salaries { get; }
//    public virtual DbRead<titles> titles { get; }
//}

//public class ConcreteDatabaseModelFactory : IDatabaseModelInstanceFactory<EmployeesDb>
//{
//    public EmployeesDb CreateInstance(DataSourceAccess dataSource)
//    {
//        return new EmployeesDb(dataSource);
//    }
//}