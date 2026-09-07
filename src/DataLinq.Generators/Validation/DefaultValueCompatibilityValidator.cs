using System.Linq;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.SourceGenerators;

internal sealed class DefaultValueCompatibilityValidator : IGeneratorDatabaseValidator
{
    public void Validate(DatabaseDefinition database, Compilation compilation, System.Threading.CancellationToken cancellationToken,
        System.Action<Diagnostic> reportDiagnostic, GeneratorValidationContext validationContext)
    {
        foreach (var property in database.TableModels.SelectMany(x => x.Model.ValueProperties.Values))
        {
            var defaultAttr = property.GetDefaultAttribute();
            if (defaultAttr == null || string.IsNullOrWhiteSpace(defaultAttr.CodeExpression))
                continue;

            if (!SourceModelSyntaxResolver.TryGetDefaultExpressionContext(property, compilation, cancellationToken, out var expressionContext))
                continue;

            var conversion = expressionContext.SemanticModel.ClassifyConversion(expressionContext.ExpressionSyntax, expressionContext.PropertyType);
            if (conversion.IsImplicit)
                continue;

            reportDiagnostic(Diagnostic.Create(
                GeneratorDiagnostics.InvalidDefaultValue,
                expressionContext.ExpressionSyntax.GetLocation(),
                defaultAttr.CodeExpression,
                property.PropertyName,
                expressionContext.PropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));

            validationContext.SuppressDefaultValue(property);
        }
    }
}
