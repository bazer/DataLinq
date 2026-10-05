using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ThrowAway;

namespace DataLinq.SourceGenerators;

internal static class ModelGeneratorTrackingNames
{
    public const string ModelDeclarations = "DataLinq.ModelDeclarations";
    public const string EnumDeclarations = "DataLinq.EnumDeclarations";
    public const string CollectedModelDeclarations = "DataLinq.CollectedModelDeclarations";
    public const string CollectedEnumDeclarations = "DataLinq.CollectedEnumDeclarations";
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

    internal static ImmutableArray<EnumDeclarationInput> NormalizeEnumDeclarationOrder(ImmutableArray<EnumDeclarationInput> enumDeclarations)
        => enumDeclarations
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
        => x.Snapshot.Equals(y.Snapshot) && x.TypeAttributeSignature.SequenceEqual(y.TypeAttributeSignature, StringComparer.Ordinal);

    public int GetHashCode(ModelDeclarationInput obj)
    {
        unchecked
        {
            var hash = obj.Snapshot.GetHashCode();
            foreach (var value in obj.TypeAttributeSignature)
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(value);
            return hash;
        }
    }
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

internal sealed class EnumDeclarationInputComparer : IEqualityComparer<EnumDeclarationInput>
{
    public static EnumDeclarationInputComparer Instance { get; } = new();

    public bool Equals(EnumDeclarationInput x, EnumDeclarationInput y)
        => x.Snapshot.Equals(y.Snapshot);

    public int GetHashCode(EnumDeclarationInput obj)
        => obj.Snapshot.GetHashCode();
}

internal sealed class EnumDeclarationInputArrayComparer : IEqualityComparer<ImmutableArray<EnumDeclarationInput>>
{
    public static EnumDeclarationInputArrayComparer Instance { get; } = new();

    public bool Equals(ImmutableArray<EnumDeclarationInput> x, ImmutableArray<EnumDeclarationInput> y)
    {
        if (x.Length != y.Length)
            return false;

        for (var i = 0; i < x.Length; i++)
        {
            if (!EnumDeclarationInputComparer.Instance.Equals(x[i], y[i]))
                return false;
        }

        return true;
    }

    public int GetHashCode(ImmutableArray<EnumDeclarationInput> obj)
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in obj)
                hash = hash * 31 + EnumDeclarationInputComparer.Instance.GetHashCode(item);

            return hash;
        }
    }
}

internal readonly struct EnumDeclarationInput
{
    public EnumDeclarationInput(EnumDeclarationSyntax syntax, ModelDeclarationSnapshot snapshot)
    {
        Syntax = syntax;
        Snapshot = snapshot;
    }

    public EnumDeclarationSyntax Syntax { get; }
    public ModelDeclarationSnapshot Snapshot { get; }

    public static EnumDeclarationInput Create(EnumDeclarationSyntax syntax)
        => new(syntax, ModelDeclarationSnapshot.Create(syntax));
}

internal readonly struct ModelDeclarationInput
{
    public ModelDeclarationInput(TypeDeclarationSyntax syntax, ModelDeclarationSnapshot snapshot)
        : this(syntax, snapshot, ImmutableDictionary<AttributeSyntax, TypeAttribute>.Empty, ImmutableArray<string>.Empty)
    {
    }

    private ModelDeclarationInput(TypeDeclarationSyntax syntax, ModelDeclarationSnapshot snapshot,
        IReadOnlyDictionary<AttributeSyntax, TypeAttribute> typeAttributes, ImmutableArray<string> typeAttributeSignature)
    {
        Syntax = syntax;
        Snapshot = snapshot;
        TypeAttributes = typeAttributes;
        TypeAttributeSignature = typeAttributeSignature;
    }

    public TypeDeclarationSyntax Syntax { get; }
    public ModelDeclarationSnapshot Snapshot { get; }
    public IReadOnlyDictionary<AttributeSyntax, TypeAttribute> TypeAttributes { get; }
    public ImmutableArray<string> TypeAttributeSignature { get; }

    public static ModelDeclarationInput Create(TypeDeclarationSyntax syntax)
        => new(syntax, ModelDeclarationSnapshot.Create(syntax));

    public static ModelDeclarationInput Create(TypeDeclarationSyntax syntax, SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var attributes = TypeAttributeConstantResolver.Resolve(syntax, semanticModel, cancellationToken);
        var signature = ImmutableArray.CreateBuilder<string>();
        foreach (var item in attributes.OrderBy(static item => item.Key.SpanStart))
        {
            var value = item.Value;
            signature.Add(value.DatabaseType.ToString());
            signature.Add(value.Name);
            signature.Add(value.Length?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>");
            signature.Add(value.Decimals?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>");
            signature.Add(value.Signed?.ToString() ?? "<null>");
        }
        return new(syntax, ModelDeclarationSnapshot.Create(syntax), attributes, signature.ToImmutable());
    }
}

internal readonly struct ModelDeclarationSnapshot : IEquatable<ModelDeclarationSnapshot>
{
    public ModelDeclarationSnapshot(
        string @namespace,
        string name,
        string structuralText,
        bool nullableAnnotationsDisabled,
        string propertyNullableAnnotationContext)
    {
        Namespace = @namespace;
        Name = name;
        StructuralText = structuralText;
        NullableAnnotationsDisabled = nullableAnnotationsDisabled;
        PropertyNullableAnnotationContext = propertyNullableAnnotationContext;
    }

    public string Namespace { get; }
    public string Name { get; }
    public string StructuralText { get; }
    public bool NullableAnnotationsDisabled { get; }
    public string PropertyNullableAnnotationContext { get; }

    public static ModelDeclarationSnapshot Create(BaseTypeDeclarationSyntax syntax)
        => new(
            GetNamespace(syntax),
            syntax.Identifier.ValueText,
            GetStructuralText(syntax),
            SyntaxParser.NullableAnnotationsAreDisabledAt(syntax),
            GetPropertyNullableAnnotationContext(syntax));

    public bool Equals(ModelDeclarationSnapshot other)
        => string.Equals(Namespace, other.Namespace, StringComparison.Ordinal) &&
           string.Equals(Name, other.Name, StringComparison.Ordinal) &&
           string.Equals(StructuralText, other.StructuralText, StringComparison.Ordinal) &&
           NullableAnnotationsDisabled == other.NullableAnnotationsDisabled &&
           string.Equals(
               PropertyNullableAnnotationContext,
               other.PropertyNullableAnnotationContext,
               StringComparison.Ordinal);

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
            hash = hash * 31 + NullableAnnotationsDisabled.GetHashCode();
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(PropertyNullableAnnotationContext);
            return hash;
        }
    }

    private static string GetPropertyNullableAnnotationContext(BaseTypeDeclarationSyntax syntax)
    {
        if (syntax is not TypeDeclarationSyntax typeDeclaration)
            return string.Empty;

        return new string(typeDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Select(static property => SyntaxParser.NullableAnnotationsAreDisabledAt(property) ? '1' : '0')
            .ToArray());
    }

    private static string GetStructuralText(BaseTypeDeclarationSyntax syntax)
    {
        // Source metadata copies imports into generated files. An alias/import edit
        // therefore changes the declaration input even when its body is identical.
        var imports = syntax.Ancestors().Reverse().SelectMany(static ancestor => ancestor switch
        {
            CompilationUnitSyntax unit => unit.Usings.AsEnumerable(),
            BaseNamespaceDeclarationSyntax ns => ns.Usings.AsEnumerable(),
            _ => Enumerable.Empty<UsingDirectiveSyntax>()
        });
        return string.Join("\n", imports.Select(static item => item.WithoutTrivia().NormalizeWhitespace().ToFullString()))
            + "\n" + syntax.WithoutTrivia().NormalizeWhitespace().ToFullString();
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
