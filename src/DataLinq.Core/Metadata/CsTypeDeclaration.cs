using System;
using System.Reflection;
using DataLinq.Core.Factories;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.Metadata;

public enum ModelCsType
{
    Class,
    Record,
    Interface,
    Struct,
    Primitive,
    Enum,
    Tuple,
    Pointer
}

public class ModelUsing(string fullNamespaceName)
{
    public string FullNamespaceName { get; } = fullNamespaceName;

    public override string ToString() => FullNamespaceName;
}

public readonly record struct CsTypeDeclaration
{
    public readonly Type? Type { get; }
    public readonly string Name { get; }
    public readonly string Namespace { get; }
    public readonly ModelCsType ModelCsType { get; }

    public CsTypeDeclaration(Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        Type = type;
        Name = MetadataTypeConverter.GetKeywordName(type);
        Namespace = type.Namespace;
        ModelCsType = ParseModelCsType(type);
    }

    public CsTypeDeclaration(TypeDeclarationSyntax typeSyntax)
    {
        Name = MetadataTypeConverter.GetKeywordName(typeSyntax.Identifier.Text);
        Namespace = GetNamespace(typeSyntax);
        ModelCsType = ParseModelCsType(typeSyntax);
    }

    public CsTypeDeclaration(PropertyDeclarationSyntax propertyDeclarationSyntax)
    {
        var typeSyntax = propertyDeclarationSyntax.Type;
        Name = MetadataTypeConverter.GetKeywordName(typeSyntax.ToString().Trim('?', '"', '\"'));
        Namespace = GetNamespace(propertyDeclarationSyntax);
        ModelCsType = ParseModelCsType(typeSyntax);
    }

    public CsTypeDeclaration(BaseTypeSyntax baseTypeSyntax)
    {
        var typeSyntax = baseTypeSyntax.Type as SimpleNameSyntax;
        Name = MetadataTypeConverter.GetKeywordName(typeSyntax?.Identifier.Text ?? string.Empty);
        Namespace = GetNamespace(baseTypeSyntax);
        ModelCsType = ParseModelCsType(baseTypeSyntax.Type);
    }

    public CsTypeDeclaration(string name, string @namespace, ModelCsType modelCsType)
    {
        Name = name;
        Namespace = @namespace;
        ModelCsType = modelCsType;
    }

    public CsTypeDeclaration MutateName(string name) => new(name, Namespace, ModelCsType);

    //public CsTypeDeclaration Clone() => new(Name, Namespace, ModelCsType);

    public static ModelCsType ParseModelCsType(Type type)
    {
        if (type.IsClass)
        {
            if (type.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) != null)
                return ModelCsType.Record;

            return ModelCsType.Class;
        }

        if (type.IsInterface)
            return ModelCsType.Interface;

        if (type.IsValueType)
        {
            if (type.IsPrimitive)
            {
                // Handle primitive types (e.g., int, bool, char)
                return ModelCsType.Primitive;
            }
            else
            {
                // Handle other value types (e.g., structs, enums)
                return ModelCsType.Struct;
            }
        }

        throw new NotImplementedException($"Unknown type '{type}'");
    }

    public static string GetNamespace(SyntaxNode typeSyntax)
    {
        // Traverse up the syntax tree to find the containing namespace
        SyntaxNode? potentialNamespaceParent = typeSyntax.Parent;

        while (potentialNamespaceParent != null && !(potentialNamespaceParent is NamespaceDeclarationSyntax || potentialNamespaceParent is FileScopedNamespaceDeclarationSyntax))
        {
            potentialNamespaceParent = potentialNamespaceParent.Parent;
        }

        // If we found a NamespaceDeclarationSyntax, return its name
        if (potentialNamespaceParent is NamespaceDeclarationSyntax namespaceDeclaration)
        {
            return namespaceDeclaration.Name.ToString();
        }
        else if (potentialNamespaceParent is FileScopedNamespaceDeclarationSyntax fileScopedNamespaceDeclaration)
        {
            return fileScopedNamespaceDeclaration.Name.ToString();
        }

        // If no namespace was found, return an empty string or some default value
        return string.Empty;
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
        else if (syntaxNode is EnumDeclarationSyntax)
        {
            // Handle enum types
            return ModelCsType.Struct; // Assuming enums are treated as structs
        }
        else
        {
            throw new NotImplementedException($"Unknown type of SyntaxNode '{syntaxNode}'");
        }
    }

    public static ModelCsType ParseModelCsType(TypeSyntax typeSyntax)
    {
        if (typeSyntax is NullableTypeSyntax nullableTypeSyntax)
        {
            // Handle nullable types
            return ParseModelCsType(nullableTypeSyntax.ElementType);
        }
        else if (typeSyntax is ArrayTypeSyntax arrayTypeSyntax)
        {
            // Handle array types
            return ParseModelCsType(arrayTypeSyntax.ElementType);
        }
        else if (typeSyntax is QualifiedNameSyntax qualifiedNameSyntax)
        {
            // Handle qualified names (e.g., System.String)
            return ParseModelCsType(qualifiedNameSyntax.Right);
        }
        else if (typeSyntax is AliasQualifiedNameSyntax aliasQualifiedNameSyntax)
        {
            // Handle alias qualified names (e.g., global::System.String)
            return ParseModelCsType(aliasQualifiedNameSyntax.Name);
        }
        else if (typeSyntax is TupleTypeSyntax)
        {
            // Handle tuple types
            return ModelCsType.Struct; // Assuming tuples are treated as structs
        }
        else if (typeSyntax is PointerTypeSyntax)
        {
            // Handle pointer types
            return ModelCsType.Struct; // Assuming pointers are treated as structs
        }
        else if (typeSyntax is PredefinedTypeSyntax predefinedTypeSyntax)
        {
            // Handle predefined types (built-in types)
            var typeName = predefinedTypeSyntax.Keyword.Text;
            if (MetadataTypeConverter.IsKnownCsType(typeName))
            {
                if (MetadataTypeConverter.IsPrimitiveType(typeName))
                    return ModelCsType.Primitive;
                else
                    return ModelCsType.Struct;
            }
            else
                throw new NotImplementedException($"Unknown predefined type '{typeName}'");
        }
        else if (typeSyntax is SimpleNameSyntax simpleNameSyntax)
        {
            // Check if the type name ends with "Class", "Record", or "Interface"
            var typeName = simpleNameSyntax.Identifier.Text;
            if (typeName.EndsWith("Class"))
                return ModelCsType.Class;
            if (typeName.EndsWith("Record"))
                return ModelCsType.Record;
            if (typeName.EndsWith("Interface"))
                return ModelCsType.Interface;

            // Default to class if no specific suffix is found
            return ModelCsType.Class;
        }
        else if (typeSyntax is GenericNameSyntax genericNameSyntax)
        {
            // Handle generic types
            var typeName = genericNameSyntax.Identifier.Text;
            if (typeName.EndsWith("Class"))
                return ModelCsType.Class;
            if (typeName.EndsWith("Record"))
                return ModelCsType.Record;
            if (typeName.EndsWith("Interface"))
                return ModelCsType.Interface;

            // Default to class if no specific suffix is found
            return ModelCsType.Class;
        }
        else
        {
            throw new NotImplementedException($"Unknown type of TypeSyntax '{typeSyntax}'");
        }
    }
}
