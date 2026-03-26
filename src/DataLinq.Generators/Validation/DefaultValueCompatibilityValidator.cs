using System.Linq;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.SourceGenerators;

internal sealed class DefaultValueCompatibilityValidator : IGeneratorDatabaseValidator
{
    public void Validate(DatabaseDefinition database, Compilation compilation, SourceProductionContext context)
    {
        foreach (var property in database.TableModels.SelectMany(x => x.Model.ValueProperties.Values))
        {
            var defaultAttr = property.GetDefaultAttribute();
            if (defaultAttr == null || string.IsNullOrWhiteSpace(defaultAttr.CodeExpression))
                continue;

            if (!SourceModelSyntaxResolver.TryGetDefaultExpressionContext(property, compilation, context.CancellationToken, out var expressionContext))
                continue;

            var conversion = expressionContext.SemanticModel.ClassifyConversion(expressionContext.ExpressionSyntax, expressionContext.PropertyType);
            if (conversion.IsImplicit)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                GeneratorDiagnostics.InvalidDefaultValue,
                expressionContext.ExpressionSyntax.GetLocation(),
                defaultAttr.CodeExpression,
                property.PropertyName,
                expressionContext.PropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));

            property.SetAttributes(property.Attributes.Where(x => !ReferenceEquals(x, defaultAttr)));
        }
    }
}
