﻿using System;
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
using SGF;
using ThrowAway;

[assembly: InternalsVisibleTo("DataLinq.Generators.Tests")]

namespace DataLinq.SourceGenerators;

[IncrementalGenerator]
public class ModelGenerator() : IncrementalGenerator("DataLinqSourceGenerator")
{
    private readonly MetadataFromModelsFactory metadataFactory = new(new MetadataFromInterfacesFactoryOptions());
    private readonly GeneratorFileFactory fileFactory = new(new GeneratorFileFactoryOptions());

    public override void OnInitialize(SgfInitializationContext context)
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
        //IncrementalValuesProvider<DatabaseDefinition> cachedMetadata = compilationAndClasses.SelectMany((source, _) =>
        //    metadataFactory.ReadSyntaxTrees(source.Item2));

        IncrementalValuesProvider<Option<DatabaseDefinition, IDLOptionFailure>> cachedMetadata = compilationAndClasses.SelectMany((source, _) =>
        {
            try
            {
                return metadataFactory.ReadSyntaxTrees(source.Item2);

                //if (Option.CatchAll<List<Option<DatabaseDefinition, IDataLinqOptionFailure>>>(() => metadataFactory.ReadSyntaxTrees(source.Item2))
                //    .TryUnwrap(out var value, out var failure))
                //    return value;
                //else
                //    return [failure];
            }
            catch (Exception e)
            {
                return [DLOptionFailure.Fail(e)];
            }
        });


        // Check if nullable reference types are enabled and set the option
        context.RegisterSourceOutput(context.CompilationProvider, (spc, compilation) =>
        {
            fileFactory.Options.UseNullableReferenceTypes = IsNullableEnabled(compilation);
        });

        // Generate source files based on the cached metadata
        context.RegisterSourceOutput(cachedMetadata, (spc, metadata) =>
        {
            if (spc.CancellationToken.IsCancellationRequested)
                return;

            ExecuteForDatabase(metadata, spc);
        });
    }

    private static bool IsModelDeclaration(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDeclaration &&
               classDeclaration.BaseList?.Types.Any(t => SyntaxParser.IsModelInterface(t.ToString())) == true;
    }


    private static TypeDeclarationSyntax GetModelDeclaration(GeneratorSyntaxContext context) =>
        (TypeDeclarationSyntax)context.Node;

    private void ExecuteForDatabase(Option<DatabaseDefinition, IDLOptionFailure> db, SgfSourceProductionContext context)
    {
        if (db.HasFailed)
        {
            // Create more detailed diagnostics with error location if available
            var failure = db.Failure;
            var location = Location.None;
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "DLG001",
                    "Database Metadata Generation Failed",
                    $"{failure.Value}",
                    "DataLinq.Generators",
                    DiagnosticSeverity.Error,
                    true),
                location));
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

    private void LogInfo(SgfSourceProductionContext context, string message)
    {
#if DEBUG
        context.ReportDiagnostic(Diagnostic.Create(
            new DiagnosticDescriptor(
                "DLG999",
                "Info",
                message,
                "DataLinq.Generators",
                DiagnosticSeverity.Info,
                true),
            Location.None));
#endif
    }

}