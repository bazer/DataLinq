using System.Collections.Immutable;
using System.Linq;
using DataLinq.Core.Factories;
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
        // Create a provider for model declarations
        IncrementalValuesProvider<TypeDeclarationSyntax> modelDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsModelDeclaration(s),
                transform: static (ctx, _) => GetModelDeclaration(ctx))
            .Where(static m => m is not null)!;

        // Combine the compilation and model declarations
        IncrementalValueProvider<(Compilation, ImmutableArray<TypeDeclarationSyntax>)> compilationAndClasses = context.CompilationProvider
            .Combine(modelDeclarations.Collect());

        // Cache the metadata
        IncrementalValuesProvider<DatabaseDefinition> cachedMetadata = compilationAndClasses.SelectMany((source, _) =>
            metadataFactory.ReadSyntaxTrees(source.Item2));

        // Check if nullable reference types are enabled and set the option
        context.RegisterSourceOutput(context.CompilationProvider, (spc, compilation) =>
        {
            fileFactory.Options.UseNullableReferenceTypes = IsNullableEnabled(compilation);
        });

        // Generate source files based on the cached metadata
        context.RegisterSourceOutput(cachedMetadata, (spc, metadata) => ExecuteForDatabase(metadata, spc));
    }

    private static bool IsModelDeclaration(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDeclaration &&
               classDeclaration.BaseList?.Types.Any(t => SyntaxParser.IsModelInterface(t.ToString())) == true;
    }

    private static TypeDeclarationSyntax GetModelDeclaration(GeneratorSyntaxContext context) =>
        (TypeDeclarationSyntax)context.Node;

    private void ExecuteForDatabase(DatabaseDefinition db, SourceProductionContext context)
    {
        foreach (var (path, contents) in fileFactory.CreateModelFiles(db))
        {
            context.AddSource($"{db.Name}/{path}", contents);
        }
    }

    private static bool IsNullableEnabled(Compilation compilation)
    {
        return compilation.Options.NullableContextOptions switch
        {
            NullableContextOptions.Enable => true,
            NullableContextOptions.Warnings => true,
            NullableContextOptions.Annotations => true,
            _ => false,
        };
    }
}