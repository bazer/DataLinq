using System.Linq;
using System.Threading.Tasks;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public sealed class GeneratorMetadataReferenceCacheTests
{
    [Test]
    public async Task CompatibleParallelRequestsReuseImmutableReferences()
    {
        var requests = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => GeneratorMetadataReferenceCache.GetReferences(
                excludedAssemblies: [typeof(ModelGenerator).Assembly])))
            .ToArray();
        var results = await Task.WhenAll(requests);

        await Assert.That(GeneratorMetadataReferenceCache.GetCreationCount(
            excludedAssemblies: [typeof(ModelGenerator).Assembly])).IsEqualTo(1);
        await Assert.That(results.All(result => result.Length == results[0].Length)).IsTrue();
        await Assert.That(results.Skip(1).All(result => ReferenceEquals(result[0], results[0][0]))).IsTrue();

        var first = CSharpCompilation.Create(
            "FirstIsolatedCompilation",
            [CSharpSyntaxTree.ParseText("internal sealed class First { }")],
            results[0],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var second = CSharpCompilation.Create(
            "SecondIsolatedCompilation",
            [CSharpSyntaxTree.ParseText("internal sealed class Second { }")],
            results[0],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var changedFirst = first.AddSyntaxTrees(CSharpSyntaxTree.ParseText("internal sealed class Added { }"));

        await Assert.That(first.SyntaxTrees.Count()).IsEqualTo(1);
        await Assert.That(second.SyntaxTrees.Count()).IsEqualTo(1);
        await Assert.That(changedFirst.SyntaxTrees.Count()).IsEqualTo(2);
    }
}
