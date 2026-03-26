using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ThrowAway;

[assembly: InternalsVisibleTo("DataLinq.Generators.Tests")]

namespace DataLinq.SourceGenerators;

[Generator]
public sealed class ModelGenerator : IIncrementalGenerator
{
    private const string GeneratorName = "DataLinqSourceGenerator";
    private static readonly IGeneratorDatabaseValidator[] validators =
    [
        new DefaultValueCompatibilityValidator()
    ];
    private readonly MetadataFromModelsFactory metadataFactory = new(new MetadataFromInterfacesFactoryOptions());
    private readonly GeneratorFileFactory fileFactory = new(new GeneratorFileFactoryOptions());

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        try
        {
            InitializeCore(context);
        }
        catch (Exception exception)
        {
            context.ReportInitializationException(exception, GeneratorName);
        }
    }

    private void InitializeCore(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<TypeDeclarationSyntax> modelDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsModelDeclaration(node),
                transform: static (syntaxContext, _) => GetModelDeclaration(syntaxContext))
            .Where(static declaration => declaration is not null)!;

        IncrementalValueProvider<ImmutableArray<TypeDeclarationSyntax>> collectedClasses =
            modelDeclarations.Collect();

        var generatorInputs = context.CompilationProvider.Combine(collectedClasses);

        context.RegisterSourceOutputSafely(generatorInputs, (sourceProductionContext, input) =>
        {
            if (sourceProductionContext.CancellationToken.IsCancellationRequested)
                return;

            var (compilation, syntaxTrees) = input;
            fileFactory.Options.UseNullableReferenceTypes = IsNullableEnabled(compilation);

            foreach (var metadata in ReadMetadataSafely(syntaxTrees, sourceProductionContext.CancellationToken))
                ExecuteForDatabase(metadata, compilation, sourceProductionContext);
        }, GeneratorName);
    }

    private static bool IsModelDeclaration(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDeclaration &&
               classDeclaration.BaseList?.Types.Any(t => SyntaxParser.IsModelInterface(t.ToString())) == true;
    }

    private static TypeDeclarationSyntax GetModelDeclaration(GeneratorSyntaxContext context) =>
        (TypeDeclarationSyntax)context.Node;

    private IEnumerable<Option<DatabaseDefinition, IDLOptionFailure>> ReadMetadataSafely(
        ImmutableArray<TypeDeclarationSyntax> syntaxTrees,
        System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
                return [];

            return metadataFactory.ReadSyntaxTrees(syntaxTrees);
        }
        catch (Exception e)
        {
            return [DLOptionFailure.Fail(e)];
        }
    }

    private void ExecuteForDatabase(Option<DatabaseDefinition, IDLOptionFailure> db, Compilation compilation, SourceProductionContext context)
    {
        if (db.HasFailed)
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.MetadataGenerationFailed, Location.None, $"{db.Failure.Value}"));
            return;
        }

        try
        {
            foreach (var validator in validators)
                validator.Validate(db.Value, compilation, context);

            foreach (var (path, contents) in fileFactory.CreateModelFiles(db.Value))
                context.AddSource($"{db.Value.Name}/{path}", contents);
        }
        catch (Exception e)
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.ModelFileGenerationFailed, Location.None, $"{e.Message}\n{e.StackTrace}"));
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
