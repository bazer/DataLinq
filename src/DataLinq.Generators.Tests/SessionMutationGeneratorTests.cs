using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public sealed class SessionMutationGeneratorTests : GeneratorTestBase
{
    [Test]
    [Arguments(12, false)]
    [Arguments(12, true)]
    [Arguments(13, false)]
    [Arguments(13, true)]
    [Arguments(14, false)]
    [Arguments(14, true)]
    public async Task ExistingAndParticipantCallsCompileAcrossStaticTypesAndLanguageSettings(int language, bool nullable)
    {
        var options = new CSharpParseOptions(language switch { 12 => LanguageVersion.CSharp12, 13 => LanguageVersion.CSharp13, _ => LanguageVersion.CSharp14 });
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "tests", "fixtures", "W4SessionConsumer")))
            root = root.Parent;
        if (root is null) throw new InvalidOperationException("Cannot locate W4 session consumer sources.");
        var fixture = Path.Combine(root.FullName, "tests", "fixtures", "W4SessionConsumer");
        var sources = new[] { "BindingModels.cs", "Bindings.cs" }
            .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(fixture, file)), options, file)).ToArray();
        var (compilation, diagnostics, _) = RunGeneratorWithDiagnostics(sources, options,
            nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable);
        await Assert.That(diagnostics.Concat(compilation.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString()).ToArray()).IsEmpty();
        // Preserve the old declarations in addition to proving call-site binding.
        var extensions = compilation.GetTypeByMetadataName("SessionConsumer.TestRowExtensions")!;
        var legacy = extensions.GetMembers().OfType<IMethodSymbol>().Where(m => m.Parameters.Any(p => p.Type.ToDisplayString() == "DataLinq.Mutation.Transaction")).ToArray();
        await Assert.That(legacy.Length).IsEqualTo(18);
        var session = compilation.GetTypeByMetadataName("DataLinq.Interfaces.IDataLinqSession`1")!;
        await Assert.That(session.AllInterfaces).IsEmpty();
        await Assert.That(session.GetMembers().Any(m => m.Name is "Provider" or "DatabaseAccess" or "Commit" or "Rollback" or "Dispose" or "DisposeAsync")).IsFalse();
    }
}
