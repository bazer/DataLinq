using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public class SourceGeneratorTests : GeneratorTestBase
{
    private static string SyntaxTreesToString(IEnumerable<SyntaxTree> syntaxTrees)
        => string.Join(Environment.NewLine, syntaxTrees.Select(x => x.ToString()));

    private IEnumerable<SyntaxTree> GenerateCodeFromFolder(string[] directories, bool includeSubfolders)
    {
        var projectRoot = Directory.GetParent(Environment.CurrentDirectory)?.Parent?.Parent?.Parent;
        if (projectRoot is null)
            throw new InvalidOperationException("Could not locate the generator test project root.");

        var sources = directories
            .SelectMany(directory => Directory.EnumerateFiles(
                Path.Combine(projectRoot.FullName, directory),
                "*.cs",
                new EnumerationOptions { RecurseSubdirectories = includeSubfolders }))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path));

        return RunGenerator(sources);
    }

    [Test]
    public async Task TestEmployees()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models/employees/gen"], true).ToList();
        var code = SyntaxTreesToString(syntax);

        await Assert.That(code.Contains("public partial class ImmutableEmployee", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public static IImmutableInstance NewDataLinqImmutableInstance(IRowData rowData, IDataSourceAccess dataSource) => new ImmutableEmployee(rowData, dataSource);", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task TestEmployeesOnlyDb()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models/employees/gen"], false).ToList();
        var code = SyntaxTreesToString(syntax);

        await Assert.That(string.IsNullOrEmpty(code)).IsTrue();
    }

    [Test]
    public async Task TestAllround()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models/Allround"], true).ToList();
        var code = SyntaxTreesToString(syntax);

        await Assert.That(code.Contains("public partial class ImmutablePayment", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public partial class MutablePayment", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task TestAllroundOnlyDb()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models/Allround"], false).ToList();
        var code = SyntaxTreesToString(syntax);

        await Assert.That(string.IsNullOrEmpty(code)).IsTrue();
    }

    [Test]
    public async Task TestAllModels()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models/employees/gen", "DataLinq.Tests.Models/Allround"], true).ToList();
        var code = SyntaxTreesToString(syntax);

        await Assert.That(code.Contains("public partial class ImmutableLocation", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public partial class MutableLocation", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public partial class ImmutablePayment", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public partial class MutableEmployee", StringComparison.Ordinal)).IsTrue();
    }
}
