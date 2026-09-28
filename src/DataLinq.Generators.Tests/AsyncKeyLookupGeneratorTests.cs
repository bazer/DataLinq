using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.Generators.Tests;

public sealed class AsyncKeyLookupGeneratorTests : GeneratorTestBase
{
    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(true, false, false)]
    [Arguments(true, false, true)]
    [Arguments(false, true, false)]
    [Arguments(false, true, true)]
    [Arguments(true, true, false)]
    [Arguments(true, true, true)]
    public async Task TypedLookupsRetainModelKeyOrderSourceShapesAndNullableResults(bool composite, bool converted, bool nullable)
    {
        var keyType = converted ? "KeyId" : "int";
        var keyArguments = composite ? "tenant: 7, id: key" : "id: key";
        var source = CSharpSyntaxTree.ParseText($$"""
            #nullable {{(nullable ? "enable" : "disable")}}
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;
            using System.Threading;
            using System.Threading.Tasks;
            namespace AsyncKeys;
            public readonly record struct KeyId(int Value);
            public sealed class KeyConverter : DataLinqScalarConverter<KeyId, int>
            {
                public override int ToProvider(KeyId value, in ScalarConversionContext context) => value.Value;
                public override KeyId FromProvider(int value, in ScalarConversionContext context) => new(value);
            }
            [Database("async_keys")]
            public partial class KeyDb(DataSourceAccess source) : IDatabaseModel
            {
                public DbRead<KeyRow> Rows { get; } = new(source);
            }
            [Table("key_rows")]
            public abstract partial class KeyRow(IRowData row, IDataSourceAccess source)
                : Immutable<KeyRow, KeyDb>(row, source), ITableModel<KeyDb>
            {
                {{(composite ? "[PrimaryKey, Column(\"tenant\")] public abstract int Tenant { get; }" : "")}}
                [PrimaryKey, Column("id")]
                {{(converted ? "[ScalarConverter(typeof(KeyConverter))]" : "")}}
                public abstract {{keyType}} Id { get; }
            }
            public static class Bindings
            {
                public static void Bind({{keyType}} key, IDataSourceAccess source, Database<KeyDb> database,
                    Transaction<KeyDb> transaction, CancellationToken token)
                {
                    ValueTask<KeyRow{{(nullable ? "?" : "")}}> a = KeyRow.GetAsync({{keyArguments}}, dataSource: source);
                    ValueTask<KeyRow{{(nullable ? "?" : "")}}> b = KeyRow.GetAsync({{keyArguments}}, database: database, cancellationToken: token);
                    ValueTask<KeyRow{{(nullable ? "?" : "")}}> c = KeyRow.GetAsync({{keyArguments}}, transaction: transaction, cancellationToken: token);
                }
            }
            """, path: "AsyncKeys.cs");
        var (compilation, diagnostics, trees) = RunGeneratorWithDiagnostics([source],
            nullableContextOptions: nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable);
        await Assert.That(diagnostics.Concat(compilation.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString()).ToArray()).IsEmpty();
        var methods = compilation.GetTypeByMetadataName("AsyncKeys.KeyRow")!.GetMembers("GetAsync").OfType<IMethodSymbol>().ToArray();
        await Assert.That(methods.Length).IsEqualTo(3);
        foreach (var method in methods)
        {
            await Assert.That(method.Parameters.Select(p => p.Name).Take(composite ? 2 : 1).ToArray())
                .IsEquivalentTo(composite ? new[] { "tenant", "id" } : ["id"]);
            await Assert.That(method.Parameters.Last().IsOptional).IsTrue();
            await Assert.That(method.Parameters.Last().Name).IsEqualTo("cancellationToken");
            await Assert.That(((INamedTypeSymbol)method.ReturnType).TypeArgumentNullableAnnotations.Single())
                .IsEqualTo(nullable ? NullableAnnotation.Annotated : NullableAnnotation.None);
        }
        var generatedMethods = trees.SelectMany(t => t.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(m => m.Identifier.ValueText == "GetAsync").ToArray();
        foreach (var method in generatedMethods)
        {
            var calls = method.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(c => c.Expression.ToString()).ToArray();
            await Assert.That(calls.Count(c => c.Contains("KeyFactory.CreateKeyFromModel"))).IsEqualTo(converted ? 1 : 0);
            await Assert.That(calls.Count(c => c.EndsWith("GetByProviderKeyAsync", StringComparison.Ordinal))).IsEqualTo(1);
        }
    }
}
