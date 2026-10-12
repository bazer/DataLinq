using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public sealed class ServiceDefaultGeneratorTests : GeneratorTestBase
{
    [Test]
    [Arguments("float value = 1.5f")]
    [Arguments("decimal value = 1.2345678901234567890123456789m")]
    [Arguments("System.DayOfWeek? value = System.DayOfWeek.Monday")]
    [Arguments("double value = double.NaN")]
    [Arguments("float value = float.NaN")]
    [Arguments("float value = float.PositiveInfinity")]
    [Arguments("float value = float.NegativeInfinity")]
    [Arguments("double value = double.PositiveInfinity")]
    [Arguments("double value = double.NegativeInfinity")]
    [Arguments("float value = float.Epsilon")]
    [Arguments("double value = double.Epsilon")]
    [Arguments("float value = -0.0f")]
    [Arguments("double value = -0.0d")]
    [Arguments("ulong value = ulong.MaxValue")]
    [Arguments("long value = long.MinValue")]
    [Arguments("decimal value = decimal.MinValue")]
    [Arguments("float? value = 1.5f")]
    [Arguments("WideEnum? value = WideEnum.Maximum")]
    public async Task OperationDefaultsCompileAndRetainBothOverloadDefaults(string parameter)
    {
        var compilation = await Compile("(IDataLinqUnitOfWorkFactory<TestDb> factory)",
            "[DataLinqOperation] private object? ValueCore(IDataLinqSession<TestDb> session, " + parameter + ") => value;");
        await WithAssembly(compilation, async assembly =>
        {
            var type = assembly.GetType("SessionConsumer.Defaults")!;
            var original = type.GetMethod("ValueCore", BindingFlags.NonPublic | BindingFlags.Instance)!.GetParameters()[1];
            var wrappers = type.GetMethods().Where(method => method.Name == "Value").ToArray();
            await Assert.That(wrappers.Length).IsEqualTo(2);
            foreach (var wrapper in wrappers)
            {
                var generated = wrapper.GetParameters().Last();
                await Assert.That(generated.IsOptional).IsTrue();
                await Assert.That(generated.ParameterType).IsEqualTo(original.ParameterType);
                await Assert.That(generated.DefaultValue).IsEqualTo(original.DefaultValue);
                if (original.DefaultValue is float single)
                    await Assert.That(BitConverter.SingleToInt32Bits((float)generated.DefaultValue!)).IsEqualTo(BitConverter.SingleToInt32Bits(single));
                if (original.DefaultValue is double value)
                    await Assert.That(BitConverter.DoubleToInt64Bits((double)generated.DefaultValue!)).IsEqualTo(BitConverter.DoubleToInt64Bits(value));
            }
        });
    }

    [Test]
    [Arguments("byte value = 3")]
    [Arguments("sbyte value = -3")]
    [Arguments("short value = 3")]
    [Arguments("ushort value = 3")]
    [Arguments("long value = 3L")]
    [Arguments("uint value = 3U")]
    [Arguments("ulong value = 3UL")]
    [Arguments("float value = 1.5f")]
    [Arguments("decimal value = 1.5m")]
    [Arguments("System.DayOfWeek? value = System.DayOfWeek.Monday")]
    [Arguments("byte? value = 3")]
    [Arguments("float? value = 1.5f")]
    [Arguments("WideEnum? value = WideEnum.Maximum")]
    [Arguments("string? value = null")]
    [Arguments("System.Guid value = default")]
    public async Task ConstructorFallbackHasTheAuthoredTypeAndValue(string parameter)
    {
        var compilation = await Compile("(IDataLinqUnitOfWorkFactory<TestDb> factory, " + parameter + ")",
            "public object? Value => value;", """
            public static class Activation
            {
                private sealed class Empty : System.IServiceProvider
                {
                    public object? GetService(System.Type type) => null;
                }
                public static object?[] Run() => [new Defaults(null!).Value, Create<Defaults>().Value];
                private static T Create<T>() where T : class, IDataLinqService<TestDb>, IDataLinqServiceFactory<TestDb, T>
                    => T.Create(new Empty(), null!);
            }
            """);
        await WithAssembly(compilation, async assembly =>
        {
            var results = (object?[])assembly.GetType("SessionConsumer.Activation")!.GetMethod("Run")!.Invoke(null, null)!;
            await Assert.That(results[1]?.GetType()).IsEqualTo(results[0]?.GetType());
            await Assert.That(results[1]).IsEqualTo(results[0]);
        });
    }

    private async Task<Compilation> Compile(string constructor, string body, string extra = "")
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "tests", "fixtures", "W4SessionConsumer"))) root = root.Parent;
        var options = new CSharpParseOptions(LanguageVersion.CSharp12);
        var source = "using DataLinq.Attributes; using DataLinq.Interfaces; namespace SessionConsumer; " +
            "public enum WideEnum : ulong { Maximum = ulong.MaxValue } public partial class Defaults" + constructor +
            " : IDataLinqService<TestDb> { " + body + " } " + extra;
        var (compilation, diagnostics, _) = RunGeneratorWithDiagnostics([
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root!.FullName, "tests", "fixtures", "W4SessionConsumer", "BindingModels.cs")), options),
            CSharpSyntaxTree.ParseText(source, options)], options);
        await Assert.That(diagnostics.Concat(compilation.GetDiagnostics()).Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString()).ToArray()).IsEmpty();
        return compilation;
    }

    private static async Task WithAssembly(Compilation compilation, Func<Assembly, Task> action)
    {
        using var bytes = new MemoryStream();
        var emitted = compilation.Emit(bytes);
        await Assert.That(emitted.Success).IsTrue();
        var context = new AssemblyLoadContext("service-defaults-" + Guid.NewGuid(), isCollectible: true);
        context.Resolving += (_, name) => name.Name == "DataLinq" ? context.LoadFromAssemblyPath(GetDataLinqRuntimeAssemblyPath()) : null;
        try
        {
            bytes.Position = 0;
            await action(context.LoadFromStream(bytes));
        }
        finally { context.Unload(); }
    }
}
