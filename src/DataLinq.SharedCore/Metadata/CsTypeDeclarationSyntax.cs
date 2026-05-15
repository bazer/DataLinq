using System;
using System.Linq;
using DataLinq.Core.Factories;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.Metadata;

internal static class CsTypeDeclarationSyntax
{
    public static CsTypeDeclaration Create(TypeDeclarationSyntax typeSyntax)
    {
        var genericPart = typeSyntax.TypeParameterList != null
            ? typeSyntax.TypeParameterList.ToString()
            : string.Empty;

        return new CsTypeDeclaration(
            MetadataTypeConverter.GetKeywordName(typeSyntax.Identifier.Text + genericPart),
            GetNamespace(typeSyntax),
            ParseModelCsType(typeSyntax));
    }

    public static CsTypeDeclaration Create(PropertyDeclarationSyntax propertyDeclarationSyntax) =>
        new(
            MetadataTypeConverter.GetKeywordName(ParseTypeName(propertyDeclarationSyntax.Type) ?? string.Empty),
            GetNamespace(propertyDeclarationSyntax),
            ParseModelCsType(propertyDeclarationSyntax.Type));

    public static CsTypeDeclaration CreatePreservingSourceType(PropertyDeclarationSyntax propertyDeclarationSyntax) =>
        new(
            MetadataTypeConverter.GetKeywordName(ParseSourceTypeName(propertyDeclarationSyntax.Type) ?? string.Empty),
            GetNamespace(propertyDeclarationSyntax),
            ParseModelCsType(propertyDeclarationSyntax.Type));

    public static CsTypeDeclaration Create(BaseTypeSyntax baseTypeSyntax) =>
        new(
            MetadataTypeConverter.GetKeywordName(ParseTypeName(baseTypeSyntax.Type) ?? string.Empty),
            GetNamespace(baseTypeSyntax),
            ParseModelCsType(baseTypeSyntax.Type));

    private static string ParseTypeName(TypeSyntax baseTypeSyntax) =>
        ParseTypeName(baseTypeSyntax, stripTopLevelNullable: true);

    private static string ParseSourceTypeName(TypeSyntax typeSyntax) =>
        typeSyntax is NullableTypeSyntax nullableType
            ? nullableType.ElementType.ToString().Trim()
            : typeSyntax.ToString().Trim();

    private static string ParseTypeName(TypeSyntax typeSyntax, bool stripTopLevelNullable)
    {
        return typeSyntax switch
        {
            GenericNameSyntax genericName => FormatGenericName(genericName),
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            QualifiedNameSyntax qualifiedName => ParseTypeName(qualifiedName.Right, stripTopLevelNullable),
            AliasQualifiedNameSyntax aliasQualifiedName => ParseTypeName(aliasQualifiedName.Name, stripTopLevelNullable),
            NullableTypeSyntax nullableType => stripTopLevelNullable
                ? ParseTypeName(nullableType.ElementType, stripTopLevelNullable: false)
                : $"{ParseTypeName(nullableType.ElementType, stripTopLevelNullable: false)}?",
            ArrayTypeSyntax arrayType => $"{ParseTypeName(arrayType.ElementType, stripTopLevelNullable: false)}{string.Concat(arrayType.RankSpecifiers.Select(rank => rank.ToString()))}",
            PredefinedTypeSyntax predefinedType => predefinedType.Keyword.Text,
            SimpleNameSyntax simpleName => simpleName.Identifier.Text,
            _ => typeSyntax.ToString().Trim('?', '"', '\"')
        };
    }

    private static string FormatGenericName(GenericNameSyntax genericName)
    {
        var typeArguments = genericName.TypeArgumentList.Arguments
            .Select(typeArgument => ParseTypeName(typeArgument, stripTopLevelNullable: false));

        return $"{genericName.Identifier.Text}<{string.Join(", ", typeArguments)}>";
    }

    public static string GetNamespace(SyntaxNode typeSyntax)
    {
        var potentialNamespaceParent = typeSyntax.Parent;

        while (potentialNamespaceParent != null &&
               potentialNamespaceParent is not NamespaceDeclarationSyntax &&
               potentialNamespaceParent is not FileScopedNamespaceDeclarationSyntax)
        {
            potentialNamespaceParent = potentialNamespaceParent.Parent;
        }

        return potentialNamespaceParent switch
        {
            NamespaceDeclarationSyntax namespaceDeclaration => namespaceDeclaration.Name.ToString(),
            FileScopedNamespaceDeclarationSyntax fileScopedNamespaceDeclaration => fileScopedNamespaceDeclaration.Name.ToString(),
            _ => string.Empty
        };
    }

    public static ModelCsType ParseModelCsType(SyntaxNode syntaxNode)
    {
        if (syntaxNode is TypeDeclarationSyntax typeDeclarationSyntax)
        {
            return typeDeclarationSyntax switch
            {
                ClassDeclarationSyntax => ModelCsType.Class,
                RecordDeclarationSyntax => ModelCsType.Record,
                InterfaceDeclarationSyntax => ModelCsType.Interface,
                StructDeclarationSyntax => ModelCsType.Struct,
                _ => throw new NotImplementedException($"Unknown type of TypeDeclarationSyntax '{typeDeclarationSyntax}'"),
            };
        }

        if (syntaxNode is EnumDeclarationSyntax)
            return ModelCsType.Enum;

        throw new NotImplementedException($"Unknown type of SyntaxNode '{syntaxNode}'");
    }

    public static ModelCsType ParseModelCsType(TypeSyntax typeSyntax)
    {
        if (typeSyntax is NullableTypeSyntax nullableTypeSyntax)
            return ParseModelCsType(nullableTypeSyntax.ElementType);

        if (typeSyntax is ArrayTypeSyntax arrayTypeSyntax)
            return ParseModelCsType(arrayTypeSyntax.ElementType);

        if (typeSyntax is QualifiedNameSyntax qualifiedNameSyntax)
            return ParseModelCsType(qualifiedNameSyntax.Right);

        if (typeSyntax is AliasQualifiedNameSyntax aliasQualifiedNameSyntax)
            return ParseModelCsType(aliasQualifiedNameSyntax.Name);

        if (typeSyntax is TupleTypeSyntax)
            return ModelCsType.Struct;

        if (typeSyntax is PointerTypeSyntax)
            return ModelCsType.Struct;

        if (typeSyntax is PredefinedTypeSyntax predefinedTypeSyntax)
        {
            var typeName = predefinedTypeSyntax.Keyword.Text;
            if (MetadataTypeConverter.IsKnownCsType(typeName))
                return MetadataTypeConverter.IsPrimitiveType(typeName)
                    ? ModelCsType.Primitive
                    : ModelCsType.Struct;

            throw new NotImplementedException($"Unknown predefined type '{typeName}'");
        }

        if (typeSyntax is SimpleNameSyntax simpleNameSyntax)
            return ParseTypeNameModelKind(simpleNameSyntax.Identifier.Text);

        if (typeSyntax is GenericNameSyntax genericNameSyntax)
            return ParseTypeNameModelKind(genericNameSyntax.Identifier.Text);

        throw new NotImplementedException($"Unknown type of TypeSyntax '{typeSyntax}'");
    }

    private static ModelCsType ParseTypeNameModelKind(string typeName)
    {
        if (typeName.EndsWith("Class", StringComparison.Ordinal))
            return ModelCsType.Class;
        if (typeName.EndsWith("Record", StringComparison.Ordinal))
            return ModelCsType.Record;
        if (typeName.EndsWith("Interface", StringComparison.Ordinal))
            return ModelCsType.Interface;

        return ModelCsType.Class;
    }
}
