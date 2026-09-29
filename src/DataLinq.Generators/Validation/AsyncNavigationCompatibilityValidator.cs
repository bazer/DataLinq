using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.SourceGenerators;

// Most models have no competing name. Only those that do need a small semantic
// probe: C# overload resolution, rather than a name ban, decides safe call binding.
internal sealed class AsyncNavigationCompatibilityValidator : IGeneratorDatabaseValidator
{
    public void Validate(DatabaseDefinition database, Compilation compilation, CancellationToken cancellationToken,
        Action<Diagnostic> reportDiagnostic, GeneratorValidationContext validationContext)
    {
        var tokenType = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        foreach (var model in database.TableModels.Where(t => !t.IsStub).Select(t => t.Model))
        {
            var modelName = FullName(model);
            if (compilation.GetTypeByMetadataName(modelName) is not { } modelSymbol) continue;
            var immutableName = string.IsNullOrEmpty(model.CsType.Namespace) ? "Immutable" + model.CsType.Name
                : model.CsType.Namespace + ".Immutable" + model.CsType.Name;
            var immutableSymbol = compilation.GetTypeByMetadataName(immutableName);
            foreach (var relation in model.RelationProperties.Values.Where(r => r.RelationPart.Type == RelationPartType.ForeignKey))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = relation.PropertyName + "Async";
                var candidates = Members(modelSymbol, name).Concat(immutableSymbol?.GetMembers(name) ?? []).ToArray();
                if (candidates.Length == 0) continue;
                var target = relation.RelationPart.GetOtherSide().ColumnIndex.Table.Model;
                var result = $"global::System.Threading.Tasks.ValueTask<global::{FullName(target)}{(relation.CsNullable ? "?" : "")}>";
                var signature = $"{result} {name}(global::System.Threading.CancellationToken cancellationToken = default) => throw new global::System.NotSupportedException();";
                var inherited = candidates.OfType<IMethodSymbol>().FirstOrDefault(m =>
                    !SymbolEqualityComparer.Default.Equals(m.ContainingType, modelSymbol) &&
                    !SymbolEqualityComparer.Default.Equals(m.ContainingType, immutableSymbol) && ExactTokenSignature(m, tokenType));
                var canOverride = inherited is { IsStatic: false, IsSealed: false, DeclaredAccessibility: Accessibility.Public }
                    && (inherited.IsVirtual || inherited.IsAbstract || inherited.IsOverride);
                var modifier = canOverride ? "override" : "virtual";
                var namespaceStart = string.IsNullOrEmpty(model.CsType.Namespace) ? "" : $"namespace {model.CsType.Namespace} {{";
                var namespaceEnd = string.IsNullOrEmpty(model.CsType.Namespace) ? "" : "}";
                var probe = CSharpSyntaxTree.ParseText($$"""
                    #nullable enable
                    {{namespaceStart}}
                    public abstract partial class {{model.CsType.Name}} { public {{modifier}} {{signature}} }
                    public partial class Immutable{{model.CsType.Name}} : {{model.CsType.Name}} { public override {{signature}} }
                    {{namespaceEnd}}
                    internal static class __DataLinqAsyncNavigationBindingProbe
                    {
                        private static void Base(global::{{modelName}} row, global::System.Threading.CancellationToken token)
                        { _ = row.{{name}}(); _ = row.{{name}}(token); _ = row.{{name}}(cancellationToken: token); }
                        private static void Concrete(global::{{immutableName}} row, global::System.Threading.CancellationToken token)
                        { _ = row.{{name}}(); _ = row.{{name}}(token); _ = row.{{name}}(cancellationToken: token); }
                    }
                    """, (CSharpParseOptions?)compilation.SyntaxTrees.FirstOrDefault()?.Options, cancellationToken: cancellationToken);
                var checkedCompilation = compilation.AddSyntaxTrees(probe);
                var semantic = checkedCompilation.GetSemanticModel(probe);
                var generatedMethods = probe.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Identifier.ValueText == name).ToArray();
                var generated = (IMethodSymbol)semantic.GetDeclaredSymbol(generatedMethods[0], cancellationToken)!;
                ISymbol? conflict = null;
                string? reason = null;
                foreach (var member in candidates)
                {
                    var direct = SymbolEqualityComparer.Default.Equals(member.ContainingType, modelSymbol)
                        || SymbolEqualityComparer.Default.Equals(member.ContainingType, immutableSymbol);
                    if (direct && (member is not IMethodSymbol || ExactTokenSignature((IMethodSymbol)member, tokenType)))
                    { conflict = member; reason = "the generated declaration would duplicate an existing member"; break; }
                }
                if (conflict is null && inherited is not null && (!canOverride || generated.OverriddenMethod is null ||
                    !SameReturnContract(generated.ReturnType, generated.OverriddenMethod.ReturnType)))
                { conflict = inherited; reason = "the inherited signature cannot be safely overridden with the navigation return contract"; }
                if (conflict is null)
                {
                    foreach (var invocation in probe.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        var binding = semantic.GetSymbolInfo(invocation, cancellationToken);
                        if (binding.Symbol is IMethodSymbol method && method.Locations.Any(l => l.SourceTree == probe)) continue;
                        conflict = binding.Symbol ?? binding.CandidateSymbols.FirstOrDefault(s => !s.Locations.Any(l => l.SourceTree == probe)) ?? candidates[0];
                        reason = $"the call '{invocation}' does not bind uniquely to the generated navigation";
                        break;
                    }
                }
                if (conflict is null)
                {
                    if (canOverride) validationContext.AsyncNavigationOverrides.Add(relation);
                    continue;
                }
                validationContext.StopGeneration = true;
                var location = modelSymbol.GetMembers(relation.PropertyName).FirstOrDefault()?.Locations.FirstOrDefault(l => l.IsInSource)
                    ?? modelSymbol.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
                reportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.AsyncNavigationConflict, location,
                    additionalLocations: conflict.Locations.Where(l => l.IsInSource && l.SourceTree != probe), properties: null,
                    messageArgs: [modelName + "." + relation.PropertyName, name, conflict.ToDisplayString(), reason!]));
            }
        }
    }

    private static string FullName(ModelDefinition model) => string.IsNullOrEmpty(model.CsType.Namespace)
        ? model.CsType.Name : model.CsType.Namespace + "." + model.CsType.Name;

    private static bool ExactTokenSignature(IMethodSymbol method, INamedTypeSymbol? tokenType) => method.Arity == 0
        && method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.None
        && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, tokenType);

    private static bool SameReturnContract(ITypeSymbol generated, ITypeSymbol inherited)
    {
        if (!SymbolEqualityComparer.Default.Equals(generated, inherited)) return false;
        if (generated is not INamedTypeSymbol result || inherited is not INamedTypeSymbol original) return true;
        // An oblivious legacy declaration imposes no nullable annotation contract.
        return result.TypeArgumentNullableAnnotations.Zip(original.TypeArgumentNullableAnnotations,
            (left, right) => left == NullableAnnotation.None || right == NullableAnnotation.None || left == right).All(same => same);
    }

    private static IEnumerable<ISymbol> Members(INamedTypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var member in current.GetMembers(name))
                if (SymbolEqualityComparer.Default.Equals(current, type) || member.DeclaredAccessibility != Accessibility.Private)
                    yield return member;
    }
}
