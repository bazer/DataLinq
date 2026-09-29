using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public sealed class AsyncMutationGeneratorTests : GeneratorTestBase
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AllNineteenMutationOverloadsPreserveReceiverNamesAndTypedCallbacks(bool nullable)
    {
        var source = CSharpSyntaxTree.ParseText($$"""
            #nullable {{(nullable ? "enable" : "disable")}}
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;
            namespace AsyncMutations;
            [Database("async_mutations")]
            public partial class TestDb(DataSourceAccess source) : IDatabaseModel
            {
                public DbRead<TestRow> Rows { get; } = new(source);
            }
            [Table("rows")]
            public abstract partial class TestRow(IRowData row, IDataSourceAccess source)
                : Immutable<TestRow, TestDb>(row, source), ITableModel<TestDb>
            {
                [PrimaryKey, Column("id")] public abstract int Id { get; }
                [Column("value")] public abstract string Value { get; }
            }
            public static class Bindings
            {
                public static Task<TestRow>[] Bind(TestRow model, MutableTestRow mutable,
                    Database<TestDb> database, Transaction transaction, CancellationToken token)
                {
                    Action<MutableTestRow> changes = row => row.Value = "edited";
                    return [
                        mutable.InsertAsync(database: database),
                        mutable.InsertAsync(changes: changes, transaction: transaction, cancellationToken: token),
                        mutable.InsertAsync(changes: changes, database: database, cancellationToken: token),
                        transaction.InsertAsync(model: mutable, changes: changes, cancellationToken: token),
                        model.UpdateAsync(changes: changes),
                        model.UpdateAsync(changes: changes, transaction: transaction, cancellationToken: token),
                        database.UpdateAsync(model: model, changes: changes, cancellationToken: token),
                        transaction.UpdateAsync(model: model, changes: changes, cancellationToken: token),
                        mutable.UpdateAsync(database: database, cancellationToken: token),
                        model.SaveAsync(changes: changes),
                        model.SaveAsync(changes: changes, transaction: transaction, cancellationToken: token),
                        database.SaveAsync(model: model, changes: changes, cancellationToken: token),
                        transaction.SaveAsync(model: model, changes: changes, cancellationToken: token),
                        model.SaveAsync(changes: changes, database: database, cancellationToken: token),
                        mutable.SaveAsync(database: database),
                        mutable.SaveAsync(changes: changes, transaction: transaction, cancellationToken: token),
                        mutable.SaveAsync(changes: changes, database: database, cancellationToken: token),
                        mutable.SaveAsync(transaction: transaction, cancellationToken: token),
                        transaction.SaveAsync(model: mutable, changes: changes, cancellationToken: token)
                    ];
                }
            }
            """, path: "AsyncMutations.cs");
        var (compilation, diagnostics, _) = RunGeneratorWithDiagnostics([source],
            nullableContextOptions: nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable);
        await Assert.That(diagnostics.Concat(compilation.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString()).ToArray()).IsEmpty();
        var extensions = compilation.GetTypeByMetadataName("AsyncMutations.TestRowExtensions")!;
        var sync = extensions.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.Name is "Insert" or "Update" or "Save").ToArray();
        var asyncMethods = extensions.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.Name is "InsertAsync" or "UpdateAsync" or "SaveAsync").ToArray();
        await Assert.That(asyncMethods.Length).IsEqualTo(19);
        foreach (var method in asyncMethods)
        {
            await Assert.That(method.Parameters.Last().Name).IsEqualTo("cancellationToken");
            await Assert.That(method.Parameters.Last().IsOptional).IsTrue();
            await Assert.That(method.ReturnType.ToDisplayString()).IsEqualTo("System.Threading.Tasks.Task<AsyncMutations.TestRow>");
            var counterpart = sync.Single(m => m.Name + "Async" == method.Name && m.Arity == method.Arity &&
                m.Parameters.Select(p => p.Type.ToDisplayString()).SequenceEqual(method.Parameters.SkipLast(1).Select(p => p.Type.ToDisplayString())));
            await Assert.That(method.Parameters.SkipLast(1).Select(p => p.Name).ToArray())
                .IsEquivalentTo(counterpart.Parameters.Select(p => p.Name).ToArray());
        }
        var bridge = compilation.GetTypeByMetadataName("AsyncMutations.MutableTestRow")!.GetMembers("ExecuteOwnedMutationAsync").Single();
        await Assert.That(bridge.DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
    }
}
