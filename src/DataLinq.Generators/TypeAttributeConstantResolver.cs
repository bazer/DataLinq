using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using DataLinq.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.SourceGenerators;

internal static class TypeAttributeConstantResolver
{
    public static IReadOnlyDictionary<AttributeSyntax, TypeAttribute> Resolve(
        TypeDeclarationSyntax declaration, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var result = new Dictionary<AttributeSyntax, TypeAttribute>();
        foreach (var property in declaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (semanticModel.GetDeclaredSymbol(property, cancellationToken) is not IPropertySymbol symbol)
                continue;
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != "DataLinq.Attributes.TypeAttribute" ||
                    attribute.AttributeConstructor is not { } constructor ||
                    attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not AttributeSyntax syntax ||
                    attribute.ConstructorArguments.Length != constructor.Parameters.Length ||
                    attribute.ConstructorArguments.Any(static argument => argument.Kind == TypedConstantKind.Error))
                    continue;

                // Roslyn normalizes named constructor arguments and performs the legal
                // constant conversions (for example const int -> ulong) for us.
                var provider = DatabaseType.Default;
                string? name = null;
                ulong? length = null;
                uint? decimals = null;
                bool? signed = null;
                for (var i = 0; i < constructor.Parameters.Length; i++)
                {
                    var value = attribute.ConstructorArguments[i].Value;
                    switch (constructor.Parameters[i].Name)
                    {
                        case "databaseType": provider = (DatabaseType)Convert.ToInt32(value, CultureInfo.InvariantCulture); break;
                        case "name": name = value as string; break;
                        case "length": length = value == null ? null : Convert.ToUInt64(value, CultureInfo.InvariantCulture); break;
                        case "decimals": decimals = value == null ? null : Convert.ToUInt32(value, CultureInfo.InvariantCulture); break;
                        case "signed": signed = value == null ? null : (bool)value; break;
                    }
                }
                if (name != null)
                    result.Add(syntax, new TypeAttribute(provider, name, length, decimals, signed));
            }
        }
        return result;
    }
}
