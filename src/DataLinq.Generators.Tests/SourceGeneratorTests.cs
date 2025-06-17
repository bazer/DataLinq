using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataLinq.Extensions.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataLinq.Generators.Tests;

public class SourceGeneratorTests : GeneratorTestBase // Inherit from the base class
{
    private string SyntaxTreesToString(IEnumerable<SyntaxTree> syntaxTrees)
    {
        return syntaxTrees.Select(x => x.ToString()).ToJoinedString();
    }

    private IEnumerable<SyntaxTree> GenerateCodeFromFolder(string[] dirs, bool includeSubfolders)
    {
        var projectRoot = Directory.GetParent(Environment.CurrentDirectory)?.Parent?.Parent?.Parent;

        Assert.NotNull(projectRoot); // Ensure we found the project root

        var sources = dirs
            .SelectMany(x => Directory.EnumerateFiles(Path.Combine(projectRoot.FullName, x), "*.cs", new EnumerationOptions { RecurseSubdirectories = includeSubfolders }))
            .Select(x => CSharpSyntaxTree.ParseText(File.ReadAllText(x), path: x)); // Pass file path

        return RunGenerator(sources); // Use the helper from the base class
    }

    [Fact]
    public void TestEmployees()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models\\employees\\gen"], true).ToList();
        var code = SyntaxTreesToString(syntax);

        Assert.Contains("public partial class ImmutableEmployee", code);
    }

    [Fact]
    public void TestEmployeesOnlyDb()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models\\employees\\gen"], false).ToList();
        var code = SyntaxTreesToString(syntax);

        Assert.Contains(code, "public partial class EmployeesDb");
    }

    [Fact]
    public void TestAllround()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models\\Allround"], true).ToList();
        var code = SyntaxTreesToString(syntax);

        Assert.Contains("public partial class ImmutablePayment", code);
        Assert.Contains("public partial class MutablePayment", code);
    }

    [Fact]
    public void TestAllroundOnlyDb()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models\\Allround"], false).ToList();
        var code = SyntaxTreesToString(syntax);

        Assert.Empty(code);
    }

    [Fact]
    public void TestAllModels()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models\\employees\\gen", "DataLinq.Tests.Models\\Allround"], true).ToList();
        var code = SyntaxTreesToString(syntax);

        Assert.Contains("public partial class ImmutableLocation", code);
        Assert.Contains("public partial class MutableLocation", code);
        Assert.Contains("public partial class ImmutablePayment", code);
        Assert.Contains("public partial class MutableEmployee", code);
    }
}