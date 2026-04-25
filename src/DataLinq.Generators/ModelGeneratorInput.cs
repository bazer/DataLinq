using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.SourceGenerators;

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

    internal static ImmutableArray<ModelDeclarationInput> NormalizeModelDeclarationOrder(ImmutableArray<ModelDeclarationInput> modelDeclarations)
        => modelDeclarations
            .OrderBy(static declaration => declaration.Snapshot.Namespace, StringComparer.Ordinal)
            .ThenBy(static declaration => declaration.Snapshot.Name, StringComparer.Ordinal)
            .ThenBy(static declaration => declaration.Syntax.SyntaxTree.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static declaration => declaration.Syntax.SpanStart)
            .ToImmutableArray();

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
