using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public abstract class GeneratorTestBase
{
    protected (Compilation outputCompilation, ImmutableArray<Diagnostic> diagnostics, IEnumerable<SyntaxTree> generatedTrees) RunGeneratorWithDiagnostics(
        IEnumerable<SyntaxTree> syntaxTrees,
        CSharpParseOptions? parseOptions = null,
        NullableContextOptions nullableContextOptions = NullableContextOptions.Enable)
    {
        var referenceLocations = AppDomain.CurrentDomain.GetAssemblies()
            // The analyzer assembly embeds SharedCore types for generator execution. A consumer
            // compilation references DataLinq.dll, not the analyzer as a runtime library; including
            // both produces false CS0433 duplicate-type errors.
            .Where(a => a != typeof(ModelGenerator).Assembly && !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => a.Location)
            .Concat(
            [
                typeof(object).Assembly.Location,
                typeof(Enumerable).Assembly.Location,
                GetDataLinqRuntimeAssemblyPath()
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static location => MetadataReference.CreateFromFile(location))
            .ToList();

        var compilation = CSharpCompilation.Create("TestAssembly",
            syntaxTrees,
            referenceLocations,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(nullableContextOptions));

        IIncrementalGenerator generator = new ModelGenerator();
        var driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedTrees = outputCompilation.SyntaxTrees
            .Where(st => !syntaxTrees.Any(t => t.FilePath == st.FilePath && t.ToString() == st.ToString()));

        return (outputCompilation, diagnostics, generatedTrees);
    }

    protected IEnumerable<SyntaxTree> RunGenerator(
        IEnumerable<SyntaxTree> syntaxTrees,
        CSharpParseOptions? parseOptions = null,
        NullableContextOptions nullableContextOptions = NullableContextOptions.Enable)
    {
        var (_, diagnostics, generatedTrees) = RunGeneratorWithDiagnostics(syntaxTrees, parseOptions, nullableContextOptions);

        var generatorErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (generatorErrors.Any())
            throw new InvalidOperationException($"Generator produced errors:{Environment.NewLine}{string.Join(Environment.NewLine, generatorErrors.Select(e => e.ToString()))}");

        return generatedTrees;
    }

    private static string GetDataLinqRuntimeAssemblyPath()
    {
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var projectRoot = outputDirectory.Parent?.Parent?.Parent?.Parent
            ?? throw new InvalidOperationException("Could not locate the generator test project root.");
        var configuration = outputDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the generator test configuration.");
        var targetFramework = outputDirectory.Name;
        var runtimeAssemblyPath = Path.Combine(
            projectRoot.FullName,
            "DataLinq",
            "bin",
            configuration,
            targetFramework,
            "DataLinq.dll");

        if (!File.Exists(runtimeAssemblyPath))
        {
            throw new FileNotFoundException(
                "The generator consumer-compilation harness requires the built DataLinq runtime assembly.",
                runtimeAssemblyPath);
        }

        return runtimeAssemblyPath;
    }
}
