using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.Metadata;

public enum ModelCsType
{
    Class,
    Record,
    Interface
}

public class ModelUsing
{
    public string FullNamespaceName { get; set; }
}

public readonly record struct CsTypeDeclaration
{
    public readonly Type? Type { get; }
    public readonly string Name { get; }
    public readonly string Namespace { get; }
    public readonly ModelCsType ModelCsType { get; }

    public CsTypeDeclaration(Type type)
    {
        Type = type;
        Name = type.Name;
        Namespace = type.Namespace;
        ModelCsType = ParseModelCsType(type);
    }

    public CsTypeDeclaration(TypeDeclarationSyntax typeSyntax)
    {
        Name = typeSyntax.Identifier.Text;
        Namespace = GetNamespace(typeSyntax);
        ModelCsType = ParseModelCsType(typeSyntax);
    }

    public CsTypeDeclaration(BaseTypeSyntax baseTypeSyntax)
    {
        var typeSyntax = baseTypeSyntax.Type as SimpleNameSyntax;
        Name = typeSyntax?.Identifier.Text ?? string.Empty;
        Namespace = GetNamespace(baseTypeSyntax);
        ModelCsType = ParseModelCsType(baseTypeSyntax);
    }

    public CsTypeDeclaration(string name, string @namespace, ModelCsType modelCsType)
    {
        Name = name;
        Namespace = @namespace;
        ModelCsType = modelCsType;
    }

    public CsTypeDeclaration MutateName(string name) => new(name, Namespace, ModelCsType);

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

    

    public static ModelCsType ParseModelCsType(TypeDeclarationSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            ClassDeclarationSyntax => ModelCsType.Class,
            RecordDeclarationSyntax => ModelCsType.Record,
            InterfaceDeclarationSyntax => ModelCsType.Interface,
            _ => throw new NotImplementedException($"Unknown type of TypeDeclarationSyntax '{typeSyntax}'"),
        };
    }

    //public static ModelCsType ParseModelCsType(BaseTypeSyntax baseTypeSyntax)
    //{
    //    var typeSyntax = baseTypeSyntax.Type;
    //    return typeSyntax switch
    //    {
    //        SimpleNameSyntax simpleNameSyntax when simpleNameSyntax.Identifier.Text.EndsWith("Class") => ModelCsType.Class,
    //        SimpleNameSyntax simpleNameSyntax when simpleNameSyntax.Identifier.Text.EndsWith("Record") => ModelCsType.Record,
    //        SimpleNameSyntax simpleNameSyntax when simpleNameSyntax.Identifier.Text.EndsWith("Interface") => ModelCsType.Interface,
    //        _ => throw new NotImplementedException($"Unknown type of BaseTypeSyntax '{baseTypeSyntax}'"),
    //    };
    //}

    //public static ModelCsType ParseModelCsType(BaseTypeSyntax baseTypeSyntax)
    //{
    //    if (baseTypeSyntax is SimpleBaseTypeSyntax simpleBaseType)
    //    {
    //        var typeName = simpleBaseType.Type.ToString();

    //        // Handle known types
    //        return typeName switch
    //        {
    //            "ICustomDatabaseModel" => ModelCsType.Interface,
    //            "IDatabaseModel" => ModelCsType.Interface,
    //            "ITableModel" 
    //            _ => throw new NotImplementedException($"Unknown type of BaseTypeSyntax '{typeName}'")
    //        };
    //    }
    //    else if (baseTypeSyntax.Type is GenericNameSyntax genericBaseType)
    //    {
    //        var typeName = genericBaseType.Identifier.Text;

    //        // Handle generic types
    //        return typeName switch
    //        {
    //            "Immutable" => ModelCsType.Class,
    //            _ => throw new NotImplementedException($"Unknown type of BaseTypeSyntax '{typeName}'")
    //        };
    //    }
    //    else
    //    {
    //        throw new NotImplementedException($"Unknown type of BaseTypeSyntax '{baseTypeSyntax}'");
    //    }
    //}

    public static ModelCsType ParseModelCsType(BaseTypeSyntax baseTypeSyntax)
    {
        if (baseTypeSyntax.Type is SimpleNameSyntax simpleNameSyntax)
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
        else if (baseTypeSyntax.Type is GenericNameSyntax genericNameSyntax)
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
            throw new NotImplementedException($"Unknown type of BaseTypeSyntax '{baseTypeSyntax}'");
        }
    }

}
