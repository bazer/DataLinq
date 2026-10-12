using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public sealed class ServiceGeneratorTests : GeneratorTestBase
{
    [Test]
    [Arguments(12, false)]
    [Arguments(12, true)]
    [Arguments(13, false)]
    [Arguments(13, true)]
    [Arguments(14, false)]
    [Arguments(14, true)]
    public async Task PublicConsumerServicesCompileWithBothConstructorStylesAndAllReturnFamilies(int language, bool nullable)
    {
        var options = new CSharpParseOptions(language switch { 12 => LanguageVersion.CSharp12, 13 => LanguageVersion.CSharp13, _ => LanguageVersion.CSharp14 });
        var sources = new[] { "BindingModels.cs", "ServiceBindings.cs" }
            .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(Fixture(), file)), options, file)).ToArray();
        var (compilation, diagnostics, trees) = RunGeneratorWithDiagnostics(sources, options,
            nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable);
        await Assert.That(diagnostics.Concat(compilation.GetDiagnostics()).Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString()).ToArray()).IsEmpty();
        var services = trees.Where(tree => tree.FilePath.EndsWith(".Service.g.cs", StringComparison.Ordinal)).ToArray();
        await Assert.That(services.Length).IsEqualTo(2);
        var service = compilation.GetTypeByMetadataName("SessionConsumer.BindingService")!;
        await Assert.That(service.GetMembers("Echo").Length).IsEqualTo(2);
        await Assert.That(service.GetMembers("NativeAsync").Length).IsEqualTo(2);
        await Assert.That(service.GetMembers().OfType<IFieldSymbol>().Any(field => field.Type.Name == "IDataLinqSession")).IsFalse();
    }

    [Test]
    [Arguments("missing-partial", "partial")]
    [Arguments("missing-factory", "constructor")]
    [Arguments("ambiguous-factory", "constructor")]
    [Arguments("stored-factory", "DataLinqFactory")]
    [Arguments("wrong-session", "IDataLinqSession")]
    [Arguments("async-void", "async void")]
    [Arguments("value-task", "awaitables")]
    [Arguments("missing-token", "CancellationToken")]
    [Arguments("collision", "already used")]
    [Arguments("public-core", "private instance")]
    [Arguments("generic-method", "generic")]
    [Arguments("by-reference", "ref/out/in")]
    [Arguments("result-opt-in", "TransactionResult")]
    [Arguments("wrong-name", "ending in Core")]
    public async Task InvalidDeclarationsProduceActionableDiagnosticsWithoutGeneratorCrashes(string scenario, string expected)
    {
        var constructor = "(IDataLinqUnitOfWorkFactory<TestDb> factory)";
        var modifiers = "partial ";
        var body = "[DataLinqOperation] private int WorkCore(IDataLinqSession<TestDb> session) => 1;";
        switch (scenario)
        {
            case "missing-partial": modifiers = ""; break;
            case "missing-factory": constructor = "()"; break;
            case "ambiguous-factory": constructor = "(IDataLinqUnitOfWorkFactory<TestDb> one, IDataLinqUnitOfWorkFactory<TestDb> two)"; break;
            case "stored-factory": constructor = ""; body += " public Sample(IDataLinqUnitOfWorkFactory<TestDb> factory) { }"; break;
            case "wrong-session": body = "[DataLinqOperation] private int WorkCore(IDataLinqUnitOfWork<TestDb> session) => 1;"; break;
            case "async-void": body = "[DataLinqOperation] private async void WorkCore(IDataLinqSession<TestDb> session) { await Task.Yield(); }"; break;
            case "value-task": body = "[DataLinqOperation] private ValueTask WorkCore(IDataLinqSession<TestDb> session) => default;"; break;
            case "missing-token": body = "[DataLinqOperation] private Task WorkCoreAsync(IDataLinqSession<TestDb> session) => Task.CompletedTask;"; break;
            case "collision": body += " public int Work() => 1;"; break;
            case "public-core": body = body.Replace("private", "public"); break;
            case "generic-method": body = body.Replace("WorkCore(", "WorkCore<T>("); break;
            case "by-reference": body = "[DataLinqOperation] private int WorkCore(IDataLinqSession<TestDb> session, ref int value) => value;"; break;
            case "result-opt-in": body = body.Replace("[DataLinqOperation]", "[DataLinqOperation(ResultAware = true)]"); break;
            case "wrong-name": body = body.Replace("WorkCore", "Work"); break;
        }
        var code = "using System.Threading; using System.Threading.Tasks; using DataLinq.Attributes; using DataLinq.Interfaces; namespace SessionConsumer; public " + modifiers + "class Sample" + constructor + " : IDataLinqService<TestDb> { " + body + " }";
        var options = new CSharpParseOptions(LanguageVersion.CSharp12);
        var (_, diagnostics, generated) = RunGeneratorWithDiagnostics([
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(Fixture(), "BindingModels.cs")), options),
            CSharpSyntaxTree.ParseText(code, options)], options);
        await Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "DLG005" && diagnostic.GetMessage().Contains(expected, StringComparison.Ordinal))).IsTrue();
        await Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "DLG000")).IsFalse();
        await Assert.That(generated.Any(tree => tree.FilePath.EndsWith(".Service.g.cs", StringComparison.Ordinal))).IsFalse();
    }

    private static string Fixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "tests", "fixtures", "W4SessionConsumer"))) root = root.Parent;
        return Path.Combine(root?.FullName ?? throw new InvalidOperationException("Cannot locate W4 fixtures."), "tests", "fixtures", "W4SessionConsumer");
    }
}
