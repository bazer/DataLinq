using System;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public sealed class RequiredReferenceGeneratorTests : GeneratorTestBase
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task RequiredNavigation_CompilesAndEnforcesItsContractWithOrWithoutNullableAnnotations(bool nullable)
    {
        var source = CSharpSyntaxTree.ParseText("#nullable " + (nullable ? "enable" : "disable") + "\n" + """
            using System;
            using System.Reflection;
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;
            namespace RequiredReferenceConsumer;

            [Database("reference_consumer")]
            public sealed partial class RefDb(DataSourceAccess source) : IDatabaseModel
            {
                public DbRead<Parent> Parents { get; } = new(source);
                public DbRead<Child> Children { get; } = new(source);
            }
            [Table("parents")]
            public abstract partial class Parent(IRowData row, IDataSourceAccess source)
                : Immutable<Parent, RefDb>(row, source), ITableModel<RefDb>
            {
                [PrimaryKey, Column("id")] public abstract int Id { get; }
                [Relation("children", "parent_id", "FK_parent")]
                public abstract IImmutableRelation<Child> Children { get; }
            }
            [Table("children")]
            public abstract partial class Child(IRowData row, IDataSourceAccess source)
                : Immutable<Child, RefDb>(row, source), ITableModel<RefDb>
            {
                [PrimaryKey, Column("id")] public abstract int Id { get; }
                [ForeignKey("parents", "id", "FK_parent"), Column("parent_id")]
                public abstract int ParentId { get; }
                [Relation("parents", "id", "FK_parent")] public abstract Parent Parent { get; }
            }
            public static class Probe
            {
                private sealed class MissingReference : IImmutableForeignKey<Parent>
                {
                    public int Reads;
                    public Parent Value { get { Reads++; return null!; } }
                    public void Clear() { }
                }
                public static string Run()
                {
                    // The custom holder makes this a generated consumer test, without database I/O.
                    var child = new ImmutableChild(null!, null!);
                    var reference = new MissingReference();
                    typeof(ImmutableChild).GetField("_Parent", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(child, reference);
                    try { _ = child.Parent; }
                    catch (InvalidOperationException failure)
                    {
                        if (reference.Reads != 1) throw new Exception("Navigation loaded more than once.");
                        return failure.Message;
                    }
                    throw new Exception("Required navigation returned null.");
                }
            }
            """, path: "RequiredReferenceConsumer.cs");
        var (compilation, diagnostics, _) = RunGeneratorWithDiagnostics([source],
            nullableContextOptions: nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable);
        await Assert.That(diagnostics.Concat(compilation.GetDiagnostics())
            .Where(item => item.Severity == DiagnosticSeverity.Error).Select(item => item.ToString()).ToArray()).IsEmpty();
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        await Assert.That(emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)
            .Select(item => item.ToString()).ToArray()).IsEmpty();
        var context = new AssemblyLoadContext($"required-reference-{Guid.NewGuid():N}", isCollectible: true);
        context.Resolving += (loader, name) => name.Name == "DataLinq"
            ? loader.LoadFromAssemblyPath(GetDataLinqRuntimeAssemblyPath()) : null;
        try
        {
            output.Position = 0;
            var consumer = context.LoadFromStream(output);
            var message = (string)consumer.GetType("RequiredReferenceConsumer.Probe")!.GetMethod("Run")!.Invoke(null, null)!;
            await Assert.That(message).Contains("Child.Parent");
        }
        finally { context.Unload(); }
    }
}
