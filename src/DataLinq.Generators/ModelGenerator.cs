using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.SourceGenerators;

[Generator]
public class ModelGenerator : IIncrementalGenerator
{
    private readonly MetadataFromInterfacesFactory metadataFactory = new(new MetadataFromInterfacesFactoryOptions());
    private readonly GeneratorFileFactory fileFactory = new(new GeneratorFileFactoryOptions());

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
        node is InterfaceDeclarationSyntax interfaceDeclaration &&
        interfaceDeclaration.BaseList?.Types.Any(t => MetadataFromFileFactory.IsModelInterface(t.ToString())) == true;

    private static TypeDeclarationSyntax GetModelDeclaration(GeneratorSyntaxContext context) =>
        (TypeDeclarationSyntax)context.Node;

    private void Execute(Compilation compilation, ImmutableArray<TypeDeclarationSyntax> modelDeclarations, SourceProductionContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            new DiagnosticDescriptor("DL0001", "Info", "Executing source generator", "Info", DiagnosticSeverity.Info, true), Location.None));

        var metadata = metadataFactory.ReadSyntaxTrees(modelDeclarations);

        foreach (var db in metadata)
        {
            foreach (var (path, contents) in fileFactory.CreateModelFiles(db))
            {
                //var source = GenerateProxyClass(context, table);
                context.AddSource($"{db.Name}/{path}", contents);
            }
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
        var proxyClassName = $"{className}";

        var sb = new StringBuilder();
        var tab = "    ";
        sb.AppendLine("using System;");
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine($"public partial record {className}");
        sb.AppendLine("{");
        sb.AppendLine($"{tab}public string Generated => \"Generator: {className}\";");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
