using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.SourceGenerators;

internal readonly struct SourceDefaultExpressionContext
{
    public SourceDefaultExpressionContext(
        ExpressionSyntax expressionSyntax,
        SemanticModel semanticModel,
        ITypeSymbol propertyType)
    {
        ExpressionSyntax = expressionSyntax;
        SemanticModel = semanticModel;
        PropertyType = propertyType;
    }

    public ExpressionSyntax ExpressionSyntax { get; }
    public SemanticModel SemanticModel { get; }
    public ITypeSymbol PropertyType { get; }
}
