using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataLinq.Generators.Tests;

public abstract class GeneratorTestBase
{
    protected (Compilation outputCompilation, ImmutableArray<Diagnostic> diagnostics, IEnumerable<SyntaxTree> generatedTrees) RunGeneratorWithDiagnostics(IEnumerable<SyntaxTree> syntaxTrees, CSharpParseOptions? parseOptions = null)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        references.AddRange(
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        ]);

        var compilation = CSharpCompilation.Create("TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        IIncrementalGenerator generator = new ModelGenerator();
        var driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedTrees = outputCompilation.SyntaxTrees
            .Where(st => !syntaxTrees.Any(t => t.FilePath == st.FilePath && t.ToString() == st.ToString()));

        return (outputCompilation, diagnostics, generatedTrees);
    }

    protected IEnumerable<SyntaxTree> RunGenerator(IEnumerable<SyntaxTree> syntaxTrees, CSharpParseOptions? parseOptions = null)
    {
        var (_, diagnostics, generatedTrees) = RunGeneratorWithDiagnostics(syntaxTrees, parseOptions);

        var generatorErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (generatorErrors.Any())
            Assert.Fail($"Generator produced errors:\n{string.Join('\n', generatorErrors.Select(e => e.ToString()))}");

        return generatedTrees;
    }
}
