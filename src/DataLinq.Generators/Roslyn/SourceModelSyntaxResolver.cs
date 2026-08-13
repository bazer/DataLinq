using System;
using System.Linq;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DataLinq.SourceGenerators;

internal static class SourceModelSyntaxResolver
{
    private const string RowDataMetadataName = "DataLinq.Instances.IRowData";
    private const string ReadSourceMetadataName = "DataLinq.Interfaces.IDataLinqReadSource";

    private static readonly SymbolDisplayFormat RuntimeTypeDisplayFormat =
        SymbolDisplayFormat.FullyQualifiedFormat;

    public static bool TryGetDefaultExpressionContext(
        ValueProperty property,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken,
        out SourceDefaultExpressionContext context)
    {
        context = default;

        var filePath = property.CsFile?.FullPath;
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var syntaxTree = compilation.SyntaxTrees.FirstOrDefault(x =>
            string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (syntaxTree == null)
            return false;

        var root = syntaxTree.GetRoot(cancellationToken);
        var propertySyntax = TryFindPropertySyntax(root, property)
            ?? FindPropertySyntaxByName(root, property);

        if (propertySyntax == null)
            return false;

        var argumentExpression = TryFindDefaultExpressionSyntax(root, property)
            ?? FindDefaultExpressionSyntax(propertySyntax);
        if (argumentExpression == null)
            return false;

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var typeInfo = semanticModel.GetTypeInfo(propertySyntax.Type, cancellationToken);
        if (typeInfo.Type == null)
            return false;

        context = new SourceDefaultExpressionContext(argumentExpression, semanticModel, typeInfo.Type);
        return true;
    }

    public static bool TryGetRuntimeTypeName(
        ValueProperty property,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken,
        out string runtimeTypeName)
    {
        runtimeTypeName = string.Empty;

        if (!TryGetPropertyTypeSymbol(property, compilation, cancellationToken, out var typeSymbol))
            return false;

        typeSymbol = UnwrapNullableValueType(typeSymbol);
        if (typeSymbol.TypeKind == TypeKind.Error)
            return false;

        runtimeTypeName = typeSymbol.ToDisplayString(RuntimeTypeDisplayFormat);
        return !string.IsNullOrWhiteSpace(runtimeTypeName);
    }

    public static bool HasAccessibleReadSourceConstructor(
        ModelDefinition model,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var modelMetadataName = string.IsNullOrWhiteSpace(model.CsType.Namespace)
            ? model.CsType.Name
            : $"{model.CsType.Namespace}.{model.CsType.Name}";
        var modelType = compilation.GetTypeByMetadataName(modelMetadataName);
        var rowDataType = compilation.GetTypeByMetadataName(RowDataMetadataName);
        var readSourceType = compilation.GetTypeByMetadataName(ReadSourceMetadataName);

        if (modelType is null || rowDataType is null || readSourceType is null)
            return false;

        return modelType.InstanceConstructors.Any(constructor =>
            IsAccessibleFromGeneratedDerivedType(constructor) &&
            constructor.Parameters.Length == 2 &&
            constructor.Parameters[0].RefKind == RefKind.None &&
            constructor.Parameters[1].RefKind == RefKind.None &&
            SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, rowDataType) &&
            SymbolEqualityComparer.Default.Equals(constructor.Parameters[1].Type, readSourceType));
    }

    public static bool HasExactDatabaseReadSourceConstructor(
        DatabaseDefinition database,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var databaseMetadataName = string.IsNullOrWhiteSpace(database.CsType.Namespace)
            ? database.CsType.Name
            : $"{database.CsType.Namespace}.{database.CsType.Name}";
        var databaseType = compilation.GetTypeByMetadataName(databaseMetadataName);
        var readSourceType = compilation.GetTypeByMetadataName(ReadSourceMetadataName);
        if (databaseType is null || readSourceType is null)
            return false;

        return databaseType.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 1 &&
            constructor.Parameters[0].RefKind == RefKind.None &&
            SymbolEqualityComparer.Default.Equals(
                constructor.Parameters[0].Type,
                readSourceType));
    }

    private static bool IsAccessibleFromGeneratedDerivedType(IMethodSymbol constructor) =>
        constructor.DeclaredAccessibility is
            Accessibility.Public or
            Accessibility.Internal or
            Accessibility.Protected or
            Accessibility.ProtectedOrInternal or
            Accessibility.ProtectedAndInternal;

    private static bool TryGetPropertyTypeSymbol(
        ValueProperty property,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken,
        out ITypeSymbol typeSymbol)
    {
        typeSymbol = null!;

        var filePath = property.CsFile?.FullPath;
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var syntaxTree = compilation.SyntaxTrees.FirstOrDefault(x =>
            string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (syntaxTree == null)
            return false;

        var root = syntaxTree.GetRoot(cancellationToken);
        var propertySyntax = TryFindPropertySyntax(root, property)
            ?? FindPropertySyntaxByName(root, property);

        if (propertySyntax == null)
            return false;

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var typeInfo = semanticModel.GetTypeInfo(propertySyntax.Type, cancellationToken);
        typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType!;
        return typeSymbol != null;
    }

    private static PropertyDeclarationSyntax? TryFindPropertySyntax(SyntaxNode root, ValueProperty property)
    {
        var propertySpan = property.SourceInfo?.PropertySpan;
        if (propertySpan == null)
            return null;

        return FindNode<PropertyDeclarationSyntax>(root, propertySpan.Value);
    }

    private static ITypeSymbol UnwrapNullableValueType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments.Length == 1)
            return namedType.TypeArguments[0];

        return typeSymbol;
    }

    private static ExpressionSyntax? TryFindDefaultExpressionSyntax(SyntaxNode root, ValueProperty property)
    {
        var expressionSpan = property.SourceInfo?.DefaultValueExpressionSpan;
        if (expressionSpan == null)
            return null;

        return FindNode<ExpressionSyntax>(root, expressionSpan.Value);
    }

    private static PropertyDeclarationSyntax? FindPropertySyntaxByName(SyntaxNode root, ValueProperty property)
    {
        return root
            .DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(x =>
            {
                if (x.Identifier.ValueText != property.PropertyName)
                    return false;

                var containingType = x.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                return containingType != null
                    && containingType.Identifier.ValueText == property.Model.CsType.Name
                    && CsTypeDeclarationSyntax.GetNamespace(containingType) == property.Model.CsType.Namespace;
            });
    }

    private static ExpressionSyntax? FindDefaultExpressionSyntax(PropertyDeclarationSyntax propertySyntax)
    {
        return propertySyntax.AttributeLists
            .SelectMany(x => x.Attributes)
            .Where(IsDefaultAttributeSyntax)
            .Select(x => x.ArgumentList?.Arguments.SingleOrDefault()?.Expression)
            .FirstOrDefault(x => x != null);
    }

    private static TSyntax? FindNode<TSyntax>(SyntaxNode root, SourceTextSpan span)
        where TSyntax : SyntaxNode
    {
        var textSpan = new TextSpan(span.Start, span.Length);
        return root.DescendantNodesAndSelf()
            .OfType<TSyntax>()
            .FirstOrDefault(x => x.Span == textSpan);
    }

    private static bool IsDefaultAttributeSyntax(AttributeSyntax attributeSyntax)
    {
        return SyntaxParser.GetUnqualifiedAttributeName(attributeSyntax.Name) == "Default";
    }
}
