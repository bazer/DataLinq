using System;
using System.Linq;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DataLinq.SourceGenerators;

internal static class SourceModelSyntaxResolver
{
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

    private static PropertyDeclarationSyntax? TryFindPropertySyntax(SyntaxNode root, ValueProperty property)
    {
        var propertySpan = property.SourceInfo?.PropertySpan;
        if (propertySpan == null)
            return null;

        return FindNode<PropertyDeclarationSyntax>(root, propertySpan.Value);
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
                    && CsTypeDeclaration.GetNamespace(containingType) == property.Model.CsType.Namespace;
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
        var name = attributeSyntax.Name.ToString();
        return name == "Default" || name == "DefaultAttribute";
    }
}
