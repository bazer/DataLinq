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
using Microsoft.CodeAnalysis.Text;
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
        IncrementalValuesProvider<ModelDeclarationInput> modelDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsModelDeclaration(node),
                transform: static (syntaxContext, _) => GetModelDeclaration(syntaxContext))
            .WithComparer(ModelDeclarationInputComparer.Instance);

        IncrementalValueProvider<ImmutableArray<ModelDeclarationInput>> collectedClasses =
            modelDeclarations.Collect()
                .Select(static (declarations, _) => ModelGeneratorInput.NormalizeModelDeclarationOrder(declarations))
                .WithComparer(ModelDeclarationInputArrayComparer.Instance);

        var generatorInputs = context.CompilationProvider
            .Combine(collectedClasses)
            .Select(static (input, _) => ModelGeneratorInput.CreateFromNormalized(input.Left, input.Right));

        context.RegisterSourceOutputSafely(generatorInputs, (sourceProductionContext, generatorInput) =>
        {
            if (sourceProductionContext.CancellationToken.IsCancellationRequested)
                return;

            fileFactory.Options.UseNullableReferenceTypes = generatorInput.UseNullableReferenceTypes;

            foreach (var metadata in ReadMetadataSafely(generatorInput.SyntaxDeclarations, sourceProductionContext.CancellationToken))
                ExecuteForDatabase(metadata, generatorInput.Compilation, sourceProductionContext);
        }, GeneratorName);
    }

    private static bool IsModelDeclaration(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDeclaration &&
               classDeclaration.BaseList?.Types.Any(t => SyntaxParser.IsModelInterface(t.ToString())) == true;
    }

    private static ModelDeclarationInput GetModelDeclaration(GeneratorSyntaxContext context) =>
        ModelDeclarationInput.Create((TypeDeclarationSyntax)context.Node);

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
            context.ReportDiagnostic(Diagnostic.Create(
                GeneratorDiagnostics.MetadataGenerationFailed,
                ResolveFailureLocation(db.Failure.Value, compilation),
                $"{db.Failure.Value}"));
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
            context.ReportDiagnostic(Diagnostic.Create(
                GeneratorDiagnostics.ModelFileGenerationFailed,
                ResolveGenerationFailureLocation(e, db.Value, compilation),
                $"{e.Message}\n{e.StackTrace}"));
        }
    }

    private static Location ResolveFailureLocation(IDLOptionFailure failure, Compilation compilation)
        => ResolveSourceLocation(failure.GetMostRelevantSourceLocation(), compilation);

    private static Location ResolveGenerationFailureLocation(Exception exception, DatabaseDefinition database, Compilation compilation)
    {
        if (exception is ModelFileGenerationException modelFileGenerationException)
        {
            var modelLocation = modelFileGenerationException.GetSourceLocation();
            if (modelLocation.HasValue)
                return ResolveSourceLocation(modelLocation, compilation);
        }

        return ResolveSourceLocation(database.GetSourceLocation(), compilation);
    }

    private static Location ResolveSourceLocation(SourceLocation? sourceLocation, Compilation compilation)
    {
        if (!sourceLocation.HasValue)
            return Location.None;

        var filePath = sourceLocation.Value.File.FullPath;
        var syntaxTree = compilation.SyntaxTrees.FirstOrDefault(x =>
            string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (syntaxTree == null)
            return Location.None;

        if (!sourceLocation.Value.Span.HasValue)
            return syntaxTree.GetLocation(new TextSpan(0, 0));

        var span = sourceLocation.Value.Span.Value;
        return syntaxTree.GetLocation(new TextSpan(span.Start, span.Length));
    }
}
