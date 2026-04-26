using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ThrowAway;

namespace DataLinq.SourceGenerators;

internal static class ModelGeneratorTrackingNames
{
    public const string ModelDeclarations = "DataLinq.ModelDeclarations";
    public const string CollectedModelDeclarations = "DataLinq.CollectedModelDeclarations";
    public const string MetadataResults = "DataLinq.MetadataResults";
    public const string GeneratorInputs = "DataLinq.GeneratorInputs";
}

internal sealed class ModelGeneratorExecutionInput
{
    private ModelGeneratorExecutionInput(
        Compilation compilation,
        ImmutableArray<Option<DatabaseDefinition, IDLOptionFailure>> metadataResults,
        bool useNullableReferenceTypes)
    {
        Compilation = compilation;
        MetadataResults = metadataResults;
        UseNullableReferenceTypes = useNullableReferenceTypes;
    }

    public Compilation Compilation { get; }
    public ImmutableArray<Option<DatabaseDefinition, IDLOptionFailure>> MetadataResults { get; }
    public bool UseNullableReferenceTypes { get; }

    public static ModelGeneratorExecutionInput Create(
        Compilation compilation,
        ImmutableArray<Option<DatabaseDefinition, IDLOptionFailure>> metadataResults)
        => new(compilation, metadataResults, ModelGeneratorInput.IsNullableEnabled(compilation));
}

internal sealed class ModelGeneratorInput
{
    private ModelGeneratorInput(
        Compilation compilation,
        ImmutableArray<ModelDeclarationInput> modelDeclarations,
        bool useNullableReferenceTypes)
    {
        Compilation = compilation;
        ModelDeclarations = modelDeclarations;
        UseNullableReferenceTypes = useNullableReferenceTypes;
    }

    public Compilation Compilation { get; }
    public ImmutableArray<ModelDeclarationInput> ModelDeclarations { get; }
    public bool UseNullableReferenceTypes { get; }
    public ImmutableArray<TypeDeclarationSyntax> SyntaxDeclarations => ModelDeclarations.Select(static x => x.Syntax).ToImmutableArray();

    public static ModelGeneratorInput Create(Compilation compilation, ImmutableArray<ModelDeclarationInput> modelDeclarations)
        => new(compilation, NormalizeModelDeclarationOrder(modelDeclarations), IsNullableEnabled(compilation));

    public static ModelGeneratorInput CreateFromNormalized(Compilation compilation, ImmutableArray<ModelDeclarationInput> modelDeclarations)
        => new(compilation, modelDeclarations, IsNullableEnabled(compilation));

    internal static ImmutableArray<ModelDeclarationInput> NormalizeModelDeclarationOrder(ImmutableArray<ModelDeclarationInput> modelDeclarations)
        => modelDeclarations
            .OrderBy(static declaration => declaration.Snapshot.Namespace, StringComparer.Ordinal)
            .ThenBy(static declaration => declaration.Snapshot.Name, StringComparer.Ordinal)
            .ThenBy(static declaration => declaration.Syntax.SyntaxTree.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static declaration => declaration.Syntax.SpanStart)
            .ToImmutableArray();

    internal static bool IsNullableEnabled(Compilation compilation)
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

internal sealed class ModelDeclarationInputComparer : IEqualityComparer<ModelDeclarationInput>
{
    public static ModelDeclarationInputComparer Instance { get; } = new();

    public bool Equals(ModelDeclarationInput x, ModelDeclarationInput y)
        => x.Snapshot.Equals(y.Snapshot);

    public int GetHashCode(ModelDeclarationInput obj)
        => obj.Snapshot.GetHashCode();
}

internal sealed class ModelDeclarationInputArrayComparer : IEqualityComparer<ImmutableArray<ModelDeclarationInput>>
{
    public static ModelDeclarationInputArrayComparer Instance { get; } = new();

    public bool Equals(ImmutableArray<ModelDeclarationInput> x, ImmutableArray<ModelDeclarationInput> y)
    {
        if (x.Length != y.Length)
            return false;

        for (var i = 0; i < x.Length; i++)
        {
            if (!ModelDeclarationInputComparer.Instance.Equals(x[i], y[i]))
                return false;
        }

        return true;
    }

    public int GetHashCode(ImmutableArray<ModelDeclarationInput> obj)
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in obj)
                hash = hash * 31 + ModelDeclarationInputComparer.Instance.GetHashCode(item);

            return hash;
        }
    }
}

internal readonly struct ModelDeclarationInput
{
    public ModelDeclarationInput(TypeDeclarationSyntax syntax, ModelDeclarationSnapshot snapshot)
    {
        Syntax = syntax;
        Snapshot = snapshot;
    }

    public TypeDeclarationSyntax Syntax { get; }
    public ModelDeclarationSnapshot Snapshot { get; }

    public static ModelDeclarationInput Create(TypeDeclarationSyntax syntax)
        => new(syntax, ModelDeclarationSnapshot.Create(syntax));
}

internal readonly struct ModelDeclarationSnapshot : IEquatable<ModelDeclarationSnapshot>
{
    public ModelDeclarationSnapshot(string @namespace, string name, string structuralText)
    {
        Namespace = @namespace;
        Name = name;
        StructuralText = structuralText;
    }

    public string Namespace { get; }
    public string Name { get; }
    public string StructuralText { get; }

    public static ModelDeclarationSnapshot Create(TypeDeclarationSyntax syntax)
        => new(
            GetNamespace(syntax),
            syntax.Identifier.ValueText,
            syntax.WithoutTrivia().NormalizeWhitespace().ToFullString());

    public bool Equals(ModelDeclarationSnapshot other)
        => string.Equals(Namespace, other.Namespace, StringComparison.Ordinal) &&
           string.Equals(Name, other.Name, StringComparison.Ordinal) &&
           string.Equals(StructuralText, other.StructuralText, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is ModelDeclarationSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Namespace);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Name);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(StructuralText);
            return hash;
        }
    }

    private static string GetNamespace(SyntaxNode syntax)
    {
        for (var current = syntax.Parent; current != null; current = current.Parent)
        {
            if (current is NamespaceDeclarationSyntax namespaceDeclaration)
                return namespaceDeclaration.Name.ToString();

            if (current is FileScopedNamespaceDeclarationSyntax fileScopedNamespaceDeclaration)
                return fileScopedNamespaceDeclaration.Name.ToString();
        }

        return string.Empty;
    }
}
