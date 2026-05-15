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
            .WithComparer(ModelDeclarationInputComparer.Instance)
            .WithTrackingName(ModelGeneratorTrackingNames.ModelDeclarations);

        IncrementalValuesProvider<EnumDeclarationInput> enumDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is EnumDeclarationSyntax,
                transform: static (syntaxContext, _) => GetEnumDeclaration(syntaxContext))
            .WithComparer(EnumDeclarationInputComparer.Instance)
            .WithTrackingName(ModelGeneratorTrackingNames.EnumDeclarations);

        IncrementalValueProvider<ImmutableArray<ModelDeclarationInput>> collectedClasses =
            modelDeclarations.Collect()
                .Select(static (declarations, _) => ModelGeneratorInput.NormalizeModelDeclarationOrder(declarations))
                .WithComparer(ModelDeclarationInputArrayComparer.Instance)
                .WithTrackingName(ModelGeneratorTrackingNames.CollectedModelDeclarations);

        IncrementalValueProvider<ImmutableArray<EnumDeclarationInput>> collectedEnums =
            enumDeclarations.Collect()
                .Select(static (declarations, _) => ModelGeneratorInput.NormalizeEnumDeclarationOrder(declarations))
                .WithComparer(EnumDeclarationInputArrayComparer.Instance)
                .WithTrackingName(ModelGeneratorTrackingNames.CollectedEnumDeclarations);

        var metadataResults = collectedClasses
            .Combine(collectedEnums)
            .Select(static (input, cancellationToken) => ReadMetadataSafely(input.Left, input.Right, cancellationToken))
            .WithTrackingName(ModelGeneratorTrackingNames.MetadataResults);

        var generatorInputs = context.CompilationProvider
            .Combine(metadataResults)
            .Select(static (input, _) => ModelGeneratorExecutionInput.Create(input.Left, input.Right))
            .WithTrackingName(ModelGeneratorTrackingNames.GeneratorInputs);

        context.RegisterSourceOutputSafely(generatorInputs, (sourceProductionContext, generatorInput) =>
        {
            if (sourceProductionContext.CancellationToken.IsCancellationRequested)
                return;

            foreach (var metadata in generatorInput.MetadataResults)
                ExecuteForDatabase(metadata, generatorInput.Compilation, sourceProductionContext, generatorInput.UseNullableReferenceTypes);
        }, GeneratorName);
    }

    private static bool IsModelDeclaration(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDeclaration &&
               classDeclaration.BaseList?.Types.Any(t => SyntaxParser.IsModelInterface(t.Type)) == true;
    }

    private static ModelDeclarationInput GetModelDeclaration(GeneratorSyntaxContext context) =>
        ModelDeclarationInput.Create((TypeDeclarationSyntax)context.Node);

    private static EnumDeclarationInput GetEnumDeclaration(GeneratorSyntaxContext context) =>
        EnumDeclarationInput.Create((EnumDeclarationSyntax)context.Node);

    private static ImmutableArray<Option<DatabaseDefinition, IDLOptionFailure>> ReadMetadataSafely(
        ImmutableArray<ModelDeclarationInput> declarations,
        ImmutableArray<EnumDeclarationInput> enumDeclarations,
        System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
                return [];

            var syntaxTrees = declarations.Select(static declaration => declaration.Syntax).ToImmutableArray();
            var enumSyntaxTrees = enumDeclarations.Select(static declaration => declaration.Syntax).ToImmutableArray();
            var metadataFactory = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions());
            return metadataFactory.ReadSyntaxTrees(syntaxTrees, enumSyntaxTrees).ToImmutableArray();
        }
        catch (Exception e)
        {
            return [DLOptionFailure.Fail(e)];
        }
    }

    private void ExecuteForDatabase(Option<DatabaseDefinition, IDLOptionFailure> db, Compilation compilation, SourceProductionContext context, bool useNullableReferenceTypes)
    {
        if (db.HasFailed)
        {
            ReportFailureDiagnostics(db.Failure.Value, compilation, context);
            return;
        }

        try
        {
            var validationContext = new GeneratorValidationContext();
            foreach (var validator in validators)
                validator.Validate(db.Value, compilation, context, validationContext);

            var runtimeValuePropertyTypeNames = ResolveRuntimeValuePropertyTypeNames(
                db.Value,
                compilation,
                context.CancellationToken);

            GeneratorFileFactoryOptions CreateOptions(bool nullableReferenceTypes) => new()
            {
                UseNullableReferenceTypes = nullableReferenceTypes,
                RuntimeValuePropertyTypeNames = runtimeValuePropertyTypeNames,
                SuppressedDefaultValueProperties = validationContext.SuppressedDefaultValueProperties,
            };

            var databaseNullableContext = ResolveDatabaseNullableReferenceTypes(db.Value, compilation, useNullableReferenceTypes);
            var emissionResult = EmitGeneratedSources(
                db.Value,
                table => CreateOptions(ResolveTableNullableReferenceTypes(table, compilation, databaseNullableContext)),
                () => CreateOptions(databaseNullableContext));

            foreach (var sourceFile in emissionResult.SourceFiles)
                context.AddSource(sourceFile.HintName, sourceFile.Contents);

            foreach (var failure in emissionResult.Failures)
                ReportGenerationFailureDiagnostic(failure.Exception, db.Value, compilation, context);
        }
        catch (Exception e)
        {
            ReportGenerationFailureDiagnostic(e, db.Value, compilation, context);
        }
    }

    internal static GeneratedDatabaseEmissionResult EmitGeneratedSources(
        DatabaseDefinition database,
        GeneratorFileFactoryOptions fileFactoryOptions)
        => EmitGeneratedSources(database, _ => fileFactoryOptions, () => fileFactoryOptions);

    private static IReadOnlyDictionary<ValueProperty, string> ResolveRuntimeValuePropertyTypeNames(
        DatabaseDefinition database,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var runtimeTypeNames = new Dictionary<ValueProperty, string>();

        foreach (var property in database.TableModels
            .Where(static tableModel => !tableModel.IsStub)
            .SelectMany(static tableModel => tableModel.Model.ValueProperties.Values))
        {
            if (SourceModelSyntaxResolver.TryGetRuntimeTypeName(
                property,
                compilation,
                cancellationToken,
                out var runtimeTypeName))
                runtimeTypeNames[property] = runtimeTypeName;
        }

        return runtimeTypeNames;
    }

    internal static GeneratedDatabaseEmissionResult EmitGeneratedSources(
        DatabaseDefinition database,
        Func<TableModel, GeneratorFileFactoryOptions> tableFileOptionsFactory,
        Func<GeneratorFileFactoryOptions> databaseFileOptionsFactory)
    {
        var sourceFiles = new List<GeneratedSourceFile>();
        var failures = new List<GeneratedDatabaseEmissionFailure>();
        var hasTableModel = false;

        foreach (var table in database.TableModels.Where(static tableModel => !tableModel.IsStub))
        {
            hasTableModel = true;
            try
            {
                var fileFactory = new GeneratorFileFactory(tableFileOptionsFactory(table));
                var (path, contents) = fileFactory.CreateModelFile(table);
                sourceFiles.Add(new GeneratedSourceFile($"{database.Name}/{path}", contents));
            }
            catch (Exception exception)
            {
                failures.Add(new GeneratedDatabaseEmissionFailure(exception));
            }
        }

        if (hasTableModel && failures.Count == 0)
        {
            try
            {
                var fileFactory = new GeneratorFileFactory(databaseFileOptionsFactory());
                var (path, contents) = fileFactory.CreateDatabaseMetadataBootstrapFile(database);
                sourceFiles.Add(new GeneratedSourceFile($"{database.Name}/{path}", contents));
            }
            catch (Exception exception)
            {
                failures.Add(new GeneratedDatabaseEmissionFailure(exception));
            }
        }

        return new GeneratedDatabaseEmissionResult(sourceFiles, failures);
    }

    private static bool ResolveDatabaseNullableReferenceTypes(
        DatabaseDefinition database,
        Compilation compilation,
        bool fallback)
    {
        if (TryResolveNullableReferenceTypes(database.GetSourceLocation(), compilation, out var enabled))
            return enabled;

        foreach (var table in database.TableModels.Where(static tableModel => !tableModel.IsStub))
        {
            if (TryResolveNullableReferenceTypes(table.Model.GetSourceLocation(), compilation, out enabled))
                return enabled;
        }

        return fallback;
    }

    private static bool ResolveTableNullableReferenceTypes(
        TableModel table,
        Compilation compilation,
        bool fallback)
    {
        if (TryResolveNullableReferenceTypes(table.Model.GetSourceLocation(), compilation, out var enabled))
            return enabled;

        return fallback;
    }

    private static bool TryResolveNullableReferenceTypes(
        SourceLocation? sourceLocation,
        Compilation compilation,
        out bool enabled)
    {
        enabled = false;
        if (!sourceLocation.HasValue)
            return false;

        var filePath = sourceLocation.Value.File.FullPath;
        var syntaxTree = compilation.SyntaxTrees.FirstOrDefault(x =>
            string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (syntaxTree == null)
            return false;

        var position = sourceLocation.Value.Span?.Start ?? 0;
        if (position < 0)
            position = 0;
        else
        {
            var textLength = syntaxTree.GetText().Length;
            if (position > textLength)
                position = textLength;
        }

        var nullableContext = compilation.GetSemanticModel(syntaxTree).GetNullableContext(position);
        enabled =
            (nullableContext & NullableContext.Enabled) == NullableContext.Enabled ||
            (nullableContext & NullableContext.AnnotationsEnabled) == NullableContext.AnnotationsEnabled;
        return true;
    }

    private static void ReportFailureDiagnostics(
        IDLOptionFailure failure,
        Compilation compilation,
        SourceProductionContext context)
    {
        var issues = DataLinqDiagnosticIssue.FromFailure(failure);
        if (issues.Count == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GeneratorDiagnostics.MetadataGenerationFailed,
                ResolveSourceLocation(failure.GetMostRelevantSourceLocation(), compilation),
                $"{failure}"));
            return;
        }

        foreach (var issue in issues)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GeneratorDiagnostics.MetadataGenerationFailed,
                ResolveSourceLocation(issue.SourceLocation, compilation),
                FormatDiagnosticIssueMessage(issue)));
        }
    }

    private static string FormatDiagnosticIssueMessage(DataLinqDiagnosticIssue issue)
    {
        var contextMessages = issue.ContextMessages
            .Where(static message => !string.IsNullOrWhiteSpace(message));

        var message = $"[{issue.FailureType}] {issue.Message}";
        return string.Join(
            "\n",
            contextMessages.Append(message));
    }

    private static void ReportGenerationFailureDiagnostic(
        Exception exception,
        DatabaseDefinition database,
        Compilation compilation,
        SourceProductionContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            GeneratorDiagnostics.ModelFileGenerationFailed,
            ResolveGenerationFailureLocation(exception, database, compilation),
            exception.Message));
    }

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

internal sealed class GeneratedDatabaseEmissionResult
{
    public GeneratedDatabaseEmissionResult(
        IReadOnlyList<GeneratedSourceFile> sourceFiles,
        IReadOnlyList<GeneratedDatabaseEmissionFailure> failures)
    {
        SourceFiles = sourceFiles;
        Failures = failures;
    }

    public IReadOnlyList<GeneratedSourceFile> SourceFiles { get; }
    public IReadOnlyList<GeneratedDatabaseEmissionFailure> Failures { get; }
}

internal readonly struct GeneratedSourceFile
{
    public GeneratedSourceFile(string hintName, string contents)
    {
        HintName = hintName;
        Contents = contents;
    }

    public string HintName { get; }
    public string Contents { get; }
}

internal sealed class GeneratedDatabaseEmissionFailure
{
    public GeneratedDatabaseEmissionFailure(Exception exception)
    {
        Exception = exception;
    }

    public Exception Exception { get; }
}
