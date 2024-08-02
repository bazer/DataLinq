using System.Collections.Immutable;
using System.Linq;
using System.Text;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.SourceGenerators;

[Generator]
public class ModelGenerator : IIncrementalGenerator
{
    private readonly MetadataFromFileFactory factory = new(new MetadataFromFileFactoryOptions());

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        //factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions());

        IncrementalValuesProvider<TypeDeclarationSyntax> modelDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsModelDeclaration(s),
                transform: static (ctx, _) => GetModelDeclaration(ctx))
            .Where(static m => m is not null)!;

        IncrementalValueProvider<(Compilation, ImmutableArray<TypeDeclarationSyntax>)> compilationAndClasses = context.CompilationProvider
            .Combine(modelDeclarations.Collect());

        context.RegisterSourceOutput(compilationAndClasses, (spc, source) => Execute(source.Item1, source.Item2, spc));
    }

    private static bool IsModelDeclaration(SyntaxNode node) =>
        node is TypeDeclarationSyntax classDeclaration &&
        classDeclaration.BaseList?.Types.Any(t => MetadataFromFileFactory.IsModelInterface(t.ToString())) == true;

    private static TypeDeclarationSyntax GetModelDeclaration(GeneratorSyntaxContext context) =>
        (TypeDeclarationSyntax)context.Node;

    private void Execute(Compilation compilation, ImmutableArray<TypeDeclarationSyntax> modelDeclarations, SourceProductionContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            new DiagnosticDescriptor("DL0001", "Info", "Executing source generator", "Info", DiagnosticSeverity.Info, true), Location.None));

        var metadata = factory.ReadSyntaxTrees(modelDeclarations.ToList());

        foreach (var table in metadata.TableModels)
        {
            var source = GenerateProxyClass(context, table);
            context.AddSource($"{table.Model.CsTypeName}Proxy.cs", source);
        }
    }

    private string GenerateProxyClass(SourceProductionContext context, TableModelMetadata tableModel)
    {
        var namespaceName = tableModel.Model.Database.CsNamespace;

        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("DL002", "Error", "Unable to determine namespace", "DataLinq", DiagnosticSeverity.Error, true), null));
            return string.Empty;
        }

        var className = tableModel.Model.CsTypeName;
        var proxyClassName = $"{className}Proxy";

        var sb = new StringBuilder();
        var tab = "    ";
        sb.AppendLine("using System;");
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");
        sb.AppendLine($"{tab}public partial record {className}");
        sb.AppendLine(tab + "{");
        sb.AppendLine($"{tab}{tab}public string Generated => \"Generator: {className}\";");
        sb.AppendLine(tab + "}");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
