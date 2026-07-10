using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.SourceGenerators;

internal static class ScalarConverterMetadataResolver
{
    private const string PropertyAttributeName = "DataLinq.Attributes.ScalarConverterAttribute";
    private const string RegistrationAttributeName = "DataLinq.Attributes.ScalarConverterRegistrationAttribute";
    private const string ConverterBaseName = "DataLinqScalarConverter";

    public static Option<DatabaseDefinition, IDLOptionFailure> Resolve(
        DatabaseDefinition source,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var registrationsResult = ReadRegistrations(compilation, cancellationToken);
        if (!registrationsResult.TryUnwrap(out var registrations, out var registrationFailure))
            return registrationFailure;

        var database = MetadataDefinitionSnapshot.Copy(source);

        foreach (var tableModel in database.TableModels.Where(static tableModel => !tableModel.IsStub))
        {
            foreach (var relationProperty in tableModel.Model.RelationProperties.Values)
            {
                var marker = relationProperty.Attributes.OfType<ScalarConverterSourceAttribute>().FirstOrDefault();
                if (marker is not null)
                {
                    return CreateFailure(
                        relationProperty.GetAttributeSourceLocation(marker) ?? GetPropertySourceLocation(relationProperty),
                        $"Scalar converter attribute on '{tableModel.Model.CsType.Name}.{relationProperty.PropertyName}' is invalid. Scalar converters can be applied only to value properties.");
                }
            }

            foreach (var property in tableModel.Model.ValueProperties.Values)
            {
                if (!TryGetPropertySymbol(property, compilation, cancellationToken, out var propertySymbol))
                {
                    if (property.Attributes.Any(static attribute => attribute is ScalarConverterSourceAttribute))
                    {
                        return CreateFailure(
                            GetPropertySourceLocation(property),
                            $"Scalar converter metadata for '{tableModel.Model.CsType.Name}.{property.PropertyName}' could not resolve the source property symbol.");
                    }

                    continue;
                }

                var propertyType = UnwrapNullable(propertySymbol.Type);
                var explicitAttributes = propertySymbol.GetAttributes()
                    .Where(static attribute => GetMetadataName(attribute.AttributeClass) == PropertyAttributeName)
                    .OrderBy(attribute => GetSourceLocation(attribute, cancellationToken)?.File.FullPath, StringComparer.Ordinal)
                    .ThenBy(attribute => GetSourceLocation(attribute, cancellationToken)?.Span?.Start ?? int.MaxValue)
                    .ToArray();

                if (explicitAttributes.Length > 1)
                {
                    return CreateFailure(
                        GetSourceLocation(explicitAttributes[1], cancellationToken),
                        $"Value property '{tableModel.Model.CsType.Name}.{property.PropertyName}' defines multiple scalar converter attributes. A value property can have only one explicit scalar converter.");
                }

                INamedTypeSymbol? converterType = null;
                var origin = ScalarConverterOrigin.None;
                SourceLocation? sourceLocation = null;

                if (explicitAttributes.Length == 1)
                {
                    var attribute = explicitAttributes[0];
                    if (!TryGetTypeArgument(attribute, 0, out converterType))
                    {
                        return CreateFailure(
                            GetSourceLocation(attribute, cancellationToken),
                            $"Scalar converter attribute on '{tableModel.Model.CsType.Name}.{property.PropertyName}' does not identify a converter type.");
                    }

                    origin = ScalarConverterOrigin.Property;
                    sourceLocation = GetSourceLocation(attribute, cancellationToken);
                }
                else
                {
                    var registration = registrations.FirstOrDefault(candidate =>
                        SymbolEqualityComparer.Default.Equals(candidate.ModelType, propertyType));
                    if (registration is null)
                        continue;

                    converterType = registration.ConverterType;
                    origin = ScalarConverterOrigin.AssemblyRegistration;
                    sourceLocation = registration.SourceLocation;
                }

                var contractResult = ValidateConverter(
                    converterType!,
                    propertyType,
                    $"value property '{tableModel.Model.CsType.Name}.{property.PropertyName}'",
                    sourceLocation);
                if (!contractResult.TryUnwrap(out var contract, out var contractFailure))
                    return contractFailure;

                property.Column.SetScalarMappingCore(ColumnScalarMapping.Converted(
                    CreateTypeDeclaration(contract.ModelType),
                    CreateTypeDeclaration(contract.ProviderType),
                    CreateTypeDeclaration(converterType!),
                    converter: null,
                    origin,
                    sourceLocation));
            }
        }

        database.Freeze();
        return database;
    }

    private static Option<IReadOnlyList<Registration>, IDLOptionFailure> ReadRegistrations(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var registrations = new List<Registration>();
        var attributes = compilation.Assembly.GetAttributes()
            .Where(static attribute => GetMetadataName(attribute.AttributeClass) == RegistrationAttributeName)
            .Select(attribute => new
            {
                Attribute = attribute,
                Location = GetSourceLocation(attribute, cancellationToken)
            })
            .OrderBy(static entry => entry.Location?.File.FullPath, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Location?.Span?.Start ?? int.MaxValue)
            .ToArray();

        foreach (var entry in attributes)
        {
            if (!TryGetTypeArgument(entry.Attribute, 0, out var modelType) ||
                !TryGetTypeArgument(entry.Attribute, 1, out var converterType))
            {
                return CreateFailure(
                    entry.Location,
                    "Scalar converter assembly registration must identify both a model type and a converter type.");
            }

            var duplicate = registrations.FirstOrDefault(existing =>
                SymbolEqualityComparer.Default.Equals(existing.ModelType, modelType));
            if (duplicate is not null)
            {
                return CreateFailure(
                    entry.Location,
                    $"Duplicate scalar converter assembly registration for model type '{modelType.ToDisplayString()}'. Registrations must be unique per model type.");
            }

            var contractResult = ValidateConverter(
                converterType,
                modelType,
                $"assembly registration for model type '{modelType.ToDisplayString()}'",
                entry.Location);
            if (!contractResult.TryUnwrap(out _, out var contractFailure))
                return contractFailure;

            registrations.Add(new Registration(modelType, converterType, entry.Location));
        }

        return registrations;
    }

    private static Option<ConverterContract, IDLOptionFailure> ValidateConverter(
        INamedTypeSymbol converterType,
        ITypeSymbol expectedModelType,
        string owner,
        SourceLocation? sourceLocation)
    {
        if (converterType.TypeKind != TypeKind.Class || converterType.IsAbstract || converterType.IsUnboundGenericType)
        {
            return CreateFailure(
                sourceLocation,
                $"Scalar converter '{converterType.ToDisplayString()}' for {owner} must be a concrete, closed class.");
        }

        if (!IsAccessibleFromGeneratedCode(converterType))
        {
            return CreateFailure(
                sourceLocation,
                $"Scalar converter '{converterType.ToDisplayString()}' for {owner} and every containing type must be accessible from generated code (public, internal, or protected internal). Private and protected converter declarations are not supported.");
        }

        if (HasGenericTypeShape(converterType) || HasGenericTypeShape(expectedModelType))
        {
            return CreateFailure(
                sourceLocation,
                $"Scalar converter '{converterType.ToDisplayString()}' for {owner} uses a closed generic converter, model, or containing type. Generic scalar converter identities are outside the 0.9 metadata boundary; use a non-generic concrete converter and model type.");
        }

        if (!converterType.InstanceConstructors.Any(static constructor =>
            constructor.DeclaredAccessibility == Accessibility.Public && constructor.Parameters.Length == 0))
        {
            return CreateFailure(
                sourceLocation,
                $"Scalar converter '{converterType.ToDisplayString()}' for {owner} must have a public parameterless constructor.");
        }

        INamedTypeSymbol? converterBase = null;
        for (var current = converterType; current is not null; current = current.BaseType)
        {
            if (current.OriginalDefinition.Name == ConverterBaseName &&
                current.OriginalDefinition.Arity == 2 &&
                current.OriginalDefinition.ContainingNamespace?.ToDisplayString() == "DataLinq")
            {
                converterBase = current;
                break;
            }
        }

        if (converterBase is null || converterBase.TypeArguments.Length != 2)
        {
            return CreateFailure(
                sourceLocation,
                $"Scalar converter '{converterType.ToDisplayString()}' for {owner} must derive from DataLinqScalarConverter<TModel, TProvider> so both conversion directions are known during source metadata construction.");
        }

        var modelType = UnwrapNullable(converterBase.TypeArguments[0]);
        var providerType = converterBase.TypeArguments[1];
        if (IsNullableValueType(converterBase.TypeArguments[0]) || IsNullableValueType(providerType))
        {
            return CreateFailure(
                sourceLocation,
                $"Scalar converter '{converterType.ToDisplayString()}' for {owner} cannot use Nullable<T> contract arguments. DataLinq owns null handling around the non-null model and provider conversion.");
        }

        if (!SymbolEqualityComparer.Default.Equals(modelType, UnwrapNullable(expectedModelType)))
        {
            return CreateFailure(
                sourceLocation,
                $"Scalar converter '{converterType.ToDisplayString()}' for {owner} converts model type '{modelType.ToDisplayString()}', but '{UnwrapNullable(expectedModelType).ToDisplayString()}' is required.");
        }

        if (providerType.TypeKind is TypeKind.Error or TypeKind.TypeParameter or TypeKind.Pointer or TypeKind.FunctionPointer)
        {
            return CreateFailure(
                sourceLocation,
                $"Scalar converter '{converterType.ToDisplayString()}' for {owner} has unsupported canonical provider type '{providerType.ToDisplayString()}'.");
        }

        if (!IsSupportedCanonicalProviderScalar(providerType))
        {
            return CreateFailure(
                sourceLocation,
                $"Scalar converter '{converterType.ToDisplayString()}' for {owner} uses structured canonical provider type '{providerType.ToDisplayString()}'. The 0.9 scalar boundary requires one supported primitive, enum, string, byte array, Guid, or date/time CLR value.");
        }

        return new ConverterContract(modelType, providerType);
    }

    private static bool TryGetPropertySymbol(
        ValueProperty property,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken,
        out IPropertySymbol propertySymbol)
    {
        propertySymbol = null!;
        var filePath = property.CsFile?.FullPath;
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var syntaxTree = compilation.SyntaxTrees.FirstOrDefault(tree =>
            string.Equals(tree.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (syntaxTree is null)
            return false;

        var root = syntaxTree.GetRoot(cancellationToken);
        PropertyDeclarationSyntax? propertySyntax = null;
        if (property.SourceInfo?.PropertySpan is { } propertySpan)
        {
            var textSpan = new TextSpan(propertySpan.Start, propertySpan.Length);
            propertySyntax = root.DescendantNodesAndSelf()
                .OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault(candidate => candidate.Span == textSpan);
        }

        propertySyntax ??= root.DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(candidate =>
                candidate.Identifier.ValueText == property.PropertyName &&
                candidate.FirstAncestorOrSelf<TypeDeclarationSyntax>()?.Identifier.ValueText == property.Model.CsType.Name);

        if (propertySyntax is null)
            return false;

        var resolvedSymbol = compilation.GetSemanticModel(syntaxTree).GetDeclaredSymbol(propertySyntax, cancellationToken) as IPropertySymbol;
        if (resolvedSymbol is null)
            return false;

        propertySymbol = resolvedSymbol;
        return true;
    }

    private static bool TryGetTypeArgument(AttributeData attribute, int index, out INamedTypeSymbol type)
    {
        type = null!;
        if (attribute.ConstructorArguments.Length <= index ||
            attribute.ConstructorArguments[index].Value is not INamedTypeSymbol namedType)
            return false;

        type = namedType;
        return true;
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        IsNullableValueType(type) && type is INamedTypeSymbol namedType
            ? namedType.TypeArguments[0]
            : type;

    private static bool IsNullableValueType(ITypeSymbol type) =>
        type is INamedTypeSymbol namedType &&
        namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (
                Accessibility.Public or
                Accessibility.Internal or
                Accessibility.ProtectedOrInternal))
                return false;
        }

        return true;
    }

    private static bool HasGenericTypeShape(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
            return HasGenericTypeShape(arrayType.ElementType);

        if (type is not INamedTypeSymbol namedType)
            return false;

        for (var current = namedType; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType || current.Arity > 0)
                return true;
        }

        return false;
    }

    private static bool IsSupportedCanonicalProviderScalar(ITypeSymbol type)
    {
        type = UnwrapNullable(type);
        if (type.TypeKind == TypeKind.Enum)
            return true;

        if (type.SpecialType is
            SpecialType.System_Boolean or
            SpecialType.System_Byte or
            SpecialType.System_SByte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Char or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal or
            SpecialType.System_String or
            SpecialType.System_DateTime)
            return true;

        if (type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte })
            return true;

        var fullName = type.ToDisplayString();
        return fullName is
            "System.Guid" or
            "System.DateOnly" or
            "System.TimeOnly" or
            "System.DateTimeOffset" or
            "System.TimeSpan";
    }

    private static string? GetMetadataName(INamedTypeSymbol? type)
    {
        if (type is null)
            return null;

        var namespaceName = type.ContainingNamespace?.ToDisplayString();
        return string.IsNullOrWhiteSpace(namespaceName)
            ? type.MetadataName
            : $"{namespaceName}.{type.MetadataName}";
    }

    private static SourceLocation? GetSourceLocation(
        AttributeData attribute,
        System.Threading.CancellationToken cancellationToken)
    {
        var syntaxReference = attribute.ApplicationSyntaxReference;
        if (syntaxReference is null)
            return null;

        var syntax = syntaxReference.GetSyntax(cancellationToken);
        var filePath = syntax.SyntaxTree.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        return new SourceLocation(
            new CsFileDeclaration(filePath),
            new SourceTextSpan(syntax.SpanStart, syntax.Span.Length));
    }

    private static SourceLocation? GetPropertySourceLocation(PropertyDefinition property)
    {
        if (!property.CsFile.HasValue)
            return null;

        return property.SourceInfo.HasValue
            ? property.SourceInfo.Value.GetPropertyLocation(property.CsFile.Value)
            : new SourceLocation(property.CsFile.Value);
    }

    private static IDLOptionFailure CreateFailure(SourceLocation? sourceLocation, string message) =>
        sourceLocation.HasValue
            ? DLOptionFailure.Fail(DLFailureType.InvalidModel, message, sourceLocation.Value)
            : DLOptionFailure.Fail(DLFailureType.InvalidModel, message);

    private static CsTypeDeclaration CreateTypeDeclaration(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            var elementType = CreateTypeDeclaration(arrayType.ElementType);
            return new CsTypeDeclaration($"{elementType.Name}[]", elementType.Namespace, ModelCsType.Class);
        }

        var modelType = type.TypeKind switch
        {
            TypeKind.Class => ModelCsType.Class,
            TypeKind.Interface => ModelCsType.Interface,
            TypeKind.Enum => ModelCsType.Enum,
            TypeKind.Struct when type.SpecialType != SpecialType.None => ModelCsType.Primitive,
            TypeKind.Struct => ModelCsType.Struct,
            TypeKind.Pointer or TypeKind.FunctionPointer => ModelCsType.Pointer,
            _ => ModelCsType.Class
        };

        var name = GetNestedTypeName(type);
        var namespaceName = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return new CsTypeDeclaration(name, namespaceName, modelType);
    }

    private static string GetNestedTypeName(ITypeSymbol type)
    {
        var names = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
            names.Push(current.Name);

        return string.Join(".", names);
    }

    private sealed class Registration(
        INamedTypeSymbol modelType,
        INamedTypeSymbol converterType,
        SourceLocation? sourceLocation)
    {
        public INamedTypeSymbol ModelType { get; } = modelType;
        public INamedTypeSymbol ConverterType { get; } = converterType;
        public SourceLocation? SourceLocation { get; } = sourceLocation;
    }

    private sealed class ConverterContract(ITypeSymbol modelType, ITypeSymbol providerType)
    {
        public ITypeSymbol ModelType { get; } = modelType;
        public ITypeSymbol ProviderType { get; } = providerType;
    }
}
