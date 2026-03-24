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

        IncrementalValuesProvider<Option<DatabaseDefinition, IDLOptionFailure>> cachedMetadata =
            collectedClasses.SelectMany((syntaxTrees, cancellationToken) =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                        return Enumerable.Empty<Option<DatabaseDefinition, IDLOptionFailure>>();

                    return metadataFactory.ReadSyntaxTrees(syntaxTrees);
                }
                catch (Exception e)
                {
                    return new Option<DatabaseDefinition, IDLOptionFailure>[] { DLOptionFailure.Fail(e) };
                }
            });

        context.RegisterSourceOutputSafely(context.CompilationProvider, (sourceProductionContext, compilation) =>
        {
            fileFactory.Options.UseNullableReferenceTypes = IsNullableEnabled(compilation);
        }, GeneratorName);

        context.RegisterSourceOutputSafely(cachedMetadata, (sourceProductionContext, metadata) =>
        {
            if (sourceProductionContext.CancellationToken.IsCancellationRequested)
                return;

            ExecuteForDatabase(metadata, sourceProductionContext);
        }, GeneratorName);
    }

    private static bool IsModelDeclaration(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDeclaration &&
               classDeclaration.BaseList?.Types.Any(t => SyntaxParser.IsModelInterface(t.ToString())) == true;
    }


    private static TypeDeclarationSyntax GetModelDeclaration(GeneratorSyntaxContext context) =>
        (TypeDeclarationSyntax)context.Node;

    private void ExecuteForDatabase(Option<DatabaseDefinition, IDLOptionFailure> db, SourceProductionContext context)
    {
        if (db.HasFailed)
        {
            var failure = db.Failure;
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "DLG001",
                    "Database Metadata Generation Failed",
                    $"{failure.Value}",
                    "DataLinq.Generators",
                    DiagnosticSeverity.Error,
                    true),
                Location.None));
            return;
        }

        try
        {
            foreach (var (path, contents) in fileFactory.CreateModelFiles(db.Value))
            {
                context.AddSource($"{db.Value.Name}/{path}", contents);
            }
        }
        catch (Exception e)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "DLG002",
                    "Model File Generation Failed",
                    $"{e.Message}\n{e.StackTrace}",
                    "DataLinq.Generators",
                    DiagnosticSeverity.Error,
                    true),
                Location.None));
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
