using System;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Threading.Tasks;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public sealed class AsyncNavigationGeneratorTests : GeneratorTestBase
{
    private static SyntaxTree Model(string member = "", bool optional = false, bool nullable = true, string inherited = "") =>
        CSharpSyntaxTree.ParseText($$"""
            #nullable {{(nullable ? "enable" : "disable")}}
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;
            namespace AsyncNavigationConsumer;
            [Database("async_navigation")]
            public partial class RefDb(DataSourceAccess source) : IDatabaseModel
            {
                public DbRead<Parent> Parents { get; } = new(source);
                public DbRead<Child> Children { get; } = new(source);
            }
            [Table("parents")]
            public abstract partial class Parent(IRowData row, IDataSourceAccess source)
                : Immutable<Parent, RefDb>(row, source), ITableModel<RefDb>
            {
                [PrimaryKey, Column("id")] public abstract int Id { get; }
                [Relation("children", "parent_id", "FK_parent")] public abstract IImmutableRelation<Child> Children { get; }
            }
            public abstract class ChildBase(IRowData row, IDataSourceAccess source) : Immutable<Child, RefDb>(row, source)
            { {{inherited}} }
            [Table("children")]
            public abstract partial class Child(IRowData row, IDataSourceAccess source)
                : ChildBase(row, source), ITableModel<RefDb>
            {
                [PrimaryKey, Column("id")] public abstract int Id { get; }
                [ForeignKey("parents", "id", "FK_parent"), Column("parent_id")]
                public abstract int{{(optional ? "?" : "")}} ParentId { get; }
                [Relation("parents", "id", "FK_parent")]
                public abstract Parent{{(optional ? "?" : "")}} Parent { get; }
                {{member}}
            }
            """, path: "AsyncNavigationModel.cs");

    [Test]
    [Arguments("public int ParentAsync(CancellationToken cancellationToken = default) => 0;", "")]
    [Arguments("public int ParentAsync() => 0;", "")]
    [Arguments("public int ParentAsync(string other = \"\") => 0;", "")]
    [Arguments("public int ParentAsync(CancellationToken cancellationToken = default, int other = 0) => 0;", "")]
    [Arguments("public int ParentAsync => 0;", "")]
    [Arguments("", "public int ParentAsync(CancellationToken cancellationToken = default) => 0;")]
    [Arguments("", "public virtual Task<Parent> ParentAsync(CancellationToken cancellationToken = default) => throw new Exception();")]
    [Arguments("", "public virtual ValueTask<Parent?> ParentAsync(CancellationToken cancellationToken = default) => throw new Exception();")]
    public async Task ConflictsReportTheRelationAndMemberAndStopOnlyTheirDatabase(string member, string inherited)
    {
        var unrelated = CSharpSyntaxTree.ParseText("""
            using DataLinq; using DataLinq.Attributes; using DataLinq.Instances; using DataLinq.Interfaces;
            namespace SeparateDatabase;
            [Database("unrelated")] public partial class OtherDb(DataSourceAccess source) : IDatabaseModel
            { public DbRead<OtherRow> Rows { get; } = new(source); }
            [Table("other_rows")] public abstract partial class OtherRow(IRowData row, IDataSourceAccess source)
                : Immutable<OtherRow, OtherDb>(row, source), ITableModel<OtherDb>
            { [PrimaryKey, Column("id")] public abstract int Id { get; } }
            """, path: "Other.cs");
        var (_, diagnostics, trees) = RunGeneratorWithDiagnostics([Model(member, inherited: inherited), unrelated]);
        var conflicts = diagnostics.Where(d => d.Id == "DLG004").ToArray();
        await Assert.That(conflicts.Length).IsEqualTo(1);
        var conflict = conflicts[0];
        await Assert.That(conflict.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(conflict.Descriptor.Title.ToString()).IsEqualTo("Async navigation member conflict");
        await Assert.That(conflict.GetMessage()).Contains("Child.Parent");
        await Assert.That(conflict.GetMessage()).Contains("ParentAsync");
        await Assert.That(conflict.AdditionalLocations.Count).IsEqualTo(1);
        await Assert.That(conflict.Location.SourceTree!.GetText().ToString(conflict.Location.SourceSpan)).IsEqualTo("Parent");
        await Assert.That(trees.Any(t => t.ToString().Contains("ImmutableChild"))).IsFalse();
        await Assert.That(trees.Any(t => t.ToString().Contains("ImmutableOtherRow"))).IsTrue();
    }

    [Test]
    [Arguments("public int ParentAsync(int unrelated) => unrelated;", "")]
    [Arguments("public T ParentAsync<T>(T unrelated) => unrelated;", "")]
    [Arguments("", "public int ParentAsync(int unrelated) => unrelated;")]
    [Arguments("", "public virtual ValueTask<Parent> ParentAsync(CancellationToken cancellationToken = default) => throw new Exception();")]
    public async Task HarmlessOverloadsAndLegitimateInheritedOverridesRemainUsable(string member, string inherited)
    {
        var (compilation, diagnostics, _) = RunGeneratorWithDiagnostics([Model(member, inherited: inherited)]);
        await AssertNoErrors(compilation, diagnostics);
        var method = compilation.GetTypeByMetadataName("AsyncNavigationConsumer.Child")!.GetMembers("ParentAsync")
            .OfType<IMethodSymbol>().Single(m => m.Parameters.Length == 1 && m.Parameters[0].Type.Name == "CancellationToken");
        await Assert.That(method.IsVirtual || method.IsOverride).IsTrue();
        await Assert.That(method.IsOverride).IsEqualTo(inherited.Contains("virtual"));
    }

    [Test]
    public async Task PartialConflictsAreLocated()
    {
        var part = CSharpSyntaxTree.ParseText("""
            namespace AsyncNavigationConsumer;
            public abstract partial class Child { public int ParentAsync() => 0; }
            """, path: "Child.User.cs");
        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([Model(), part]);
        var conflict = diagnostics.Single(d => d.Id == "DLG004");
        await Assert.That(conflict.AdditionalLocations.Single().SourceTree!.FilePath).IsEqualTo("Child.User.cs");
    }

    [Test]
    public async Task LegacyObliviousVirtualMethodCanBeOverridden()
    {
        var (compilation, diagnostics, _) = RunGeneratorWithDiagnostics([Model(nullable: false,
            inherited: "public virtual ValueTask<Parent> ParentAsync(CancellationToken token = default) => throw new Exception();")],
            nullableContextOptions: NullableContextOptions.Disable);
        await AssertNoErrors(compilation, diagnostics);
    }

    [Test]
    public async Task ImmutablePartialCannotHijackTheGeneratedNavigation()
    {
        var part = CSharpSyntaxTree.ParseText("""
            namespace AsyncNavigationConsumer;
            public partial class ImmutableChild { public int ParentAsync() => 0; }
            """, path: "ImmutableChild.User.cs");
        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([Model(), part]);
        await Assert.That(diagnostics.Count(d => d.Id == "DLG004")).IsEqualTo(1);
    }

    [Test]
    public async Task IncrementalGenerationTracksInheritedOverrideAndPartialConflicts()
    {
        var references = GeneratorMetadataReferenceCache.GetReferences(excludedAssemblies: [typeof(ModelGenerator).Assembly],
            additionalLocations: [GetDataLinqRuntimeAssemblyPath()]);
        Compilation Create(SyntaxTree model, params SyntaxTree[] extra) => CSharpCompilation.Create("IncrementalAsyncNavigation",
            new[] { model }.Concat(extra), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModelGenerator().AsSourceGenerator());
        driver = driver.RunGenerators(Create(Model()));
        await Assert.That(string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString())))
            .Contains("public virtual global::System.Threading.Tasks.ValueTask<Parent> ParentAsync");
        driver = driver.RunGenerators(Create(Model(inherited: "public virtual ValueTask<Parent> ParentAsync(CancellationToken token = default) => throw new Exception();")));
        await Assert.That(string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString())))
            .DoesNotContain("public virtual global::System.Threading.Tasks.ValueTask<Parent> ParentAsync");
        var conflict = CSharpSyntaxTree.ParseText("namespace AsyncNavigationConsumer { public abstract partial class Child { public int ParentAsync() => 0; } }", path: "Partial.cs");
        driver = driver.RunGenerators(Create(Model(), conflict));
        await Assert.That(driver.GetRunResult().Diagnostics.Count(d => d.Id == "DLG004")).IsEqualTo(1);
        await Assert.That(driver.GetRunResult().GeneratedTrees.Length).IsEqualTo(0);
        driver = driver.RunGenerators(Create(Model()));
        await Assert.That(driver.GetRunResult().Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(driver.GetRunResult().GeneratedTrees.Length > 0).IsTrue();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncNavigationUsesTheSharedHolderAndPreservesNullabilityFailuresAndCustomDispatch(bool optional, bool nullable)
    {
        // The property spelling still describes an optional reference when annotations are disabled.
        var model = Model(optional: optional, nullable: nullable);
        var resultType = "Parent" + (optional && nullable ? "?" : "");
        var probe = CSharpSyntaxTree.ParseText($$"""
            #nullable {{(nullable ? "enable" : "disable")}}
            using System; using System.Reflection; using System.Threading; using System.Threading.Tasks;
            using DataLinq.Instances;
            namespace AsyncNavigationConsumer;
            public static class Probe
            {
                private sealed class Reference : IAsyncImmutableForeignKey<Parent>
                {
                    public int AsyncReads;
                    public Exception Failure;
                    public Parent Value => throw new Exception("Async navigation called the synchronous getter.");
                    public ValueTask<Parent{{(nullable ? "?" : "")}}> GetAsync(CancellationToken cancellationToken = default)
                    {
                        cancellationToken.ThrowIfCancellationRequested(); AsyncReads++;
                        if (Failure is not null) throw Failure;
                        return new((Parent)null!);
                    }
                    public void Clear() { }
                }
                private sealed class Custom : Child
                {
                    public Custom() : base(null!, null!) { }
                    public override int Id => 1;
                    public override int{{(optional ? "?" : "")}} ParentId => 1;
                    public override {{resultType}} Parent => throw new Exception("Do not use the synchronous override.");
                    public override ValueTask<{{resultType}}> ParentAsync(CancellationToken cancellationToken = default)
                        => new(new ImmutableParent(null!, null!));
                }
                private sealed class Legacy : Child
                {
                    public Legacy() : base(null!, null!) { }
                    public override int Id => 1;
                    public override int{{(optional ? "?" : "")}} ParentId => 1;
                    public override {{resultType}} Parent => throw new Exception("Do not fall back to the synchronous override.");
                }
                private sealed class LegacyReference : IImmutableForeignKey<Parent>
                {
                    public Parent Value => throw new Exception("Do not fall back to the synchronous holder.");
                    public void Clear() { }
                }
                public static async Task Run()
                {
                    Child child = new ImmutableChild(null!, null!);
                    var reference = new Reference();
                    typeof(ImmutableChild).GetField("_Parent", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(child, reference);
                    {{(optional ? "if (await child.ParentAsync() is not null) throw new Exception(\"Optional reference was not null.\");" : "try { await child.ParentAsync(); throw new Exception(\"Required reference was null.\"); } catch (InvalidOperationException e) when (e.Message.Contains(\"Child.Parent\")) { }")}}
                    if (reference.AsyncReads != 1) throw new Exception("Reference was not loaded exactly once.");
                    var original = new NotSupportedException("original capability failure");
                    reference.Failure = original;
                    try { await child.ParentAsync(cancellationToken: default); throw new Exception("Missing failure."); }
                    catch (NotSupportedException e) when (ReferenceEquals(e, original)) { }
                    try { await child.ParentAsync(new CancellationToken(true)); throw new Exception("Missing cancellation."); }
                    catch (OperationCanceledException) { }
                    if (await ((Child)new Custom()).ParentAsync() is null) throw new Exception("Custom override did not dispatch.");
                    try { await ((Child)new Legacy()).ParentAsync(); throw new Exception("Missing legacy rejection."); }
                    catch (NotSupportedException) { }
                    typeof(ImmutableChild).GetField("_Parent", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(child, new LegacyReference());
                    try { await child.ParentAsync(); throw new Exception("Missing holder capability rejection."); }
                    catch (NotSupportedException) { }
                }
            }
            """, path: "Probe.cs");
        var (compilation, diagnostics, _) = RunGeneratorWithDiagnostics([model, probe],
            nullableContextOptions: nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable);
        await AssertNoErrors(compilation, diagnostics);
        var returnType = (INamedTypeSymbol)((IMethodSymbol)compilation.GetTypeByMetadataName("AsyncNavigationConsumer.Child")!
            .GetMembers("ParentAsync").Single()).ReturnType;
        await Assert.That(returnType.TypeArgumentNullableAnnotations.Single()).IsEqualTo(!nullable ? NullableAnnotation.None
            : optional ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated);
        using var bytes = new MemoryStream();
        var emitted = compilation.Emit(bytes);
        await Assert.That(emitted.Success).IsTrue();
        var context = new AssemblyLoadContext("async-navigation-" + Guid.NewGuid(), isCollectible: true);
        context.Resolving += (loader, name) => name.Name == "DataLinq" ? loader.LoadFromAssemblyPath(GetDataLinqRuntimeAssemblyPath()) : null;
        try
        {
            bytes.Position = 0;
            var assembly = context.LoadFromStream(bytes);
            await (Task)assembly.GetType("AsyncNavigationConsumer.Probe")!.GetMethod("Run")!.Invoke(null, null)!;
        }
        finally { context.Unload(); }
    }

    private static async Task AssertNoErrors(Compilation compilation, System.Collections.Generic.IEnumerable<Diagnostic> diagnostics) =>
        await Assert.That(diagnostics.Concat(compilation.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString()).ToArray()).IsEmpty();
}
