using System;
using System.Linq;

namespace DataLinq.Core.Factories;

public static class MetadataTypeConverter
{
    public static int? CsTypeSize(string csType) => csType.ToLowerInvariant() switch
    {
        "sbyte" => sizeof(sbyte),
        "byte" => sizeof(byte),
        "short" => sizeof(short),
        "ushort" => sizeof(ushort),
        "int" => sizeof(int),
        "uint" => sizeof(uint),
        "long" => sizeof(long),
        "ulong" => sizeof(ulong),
        "char" => sizeof(char),
        "float" => sizeof(float),
        "double" => sizeof(double),
        "bool" => sizeof(bool),
        "decimal" => sizeof(decimal),
        "datetime" => 8,
        "dateonly" => sizeof(long),
        "timeonly" => sizeof(long),
        "guid" => 16,
        "string" => null,
        "byte[]" => null,
        "enum" => sizeof(int),
        _ => null
    };

    public static string GetKeywordName(Type type)
    {
        if (type.IsGenericType)
            return GetFriendlyTypeNameWithKeywords(type);

        // For array types, recursively call GetKeywordName on the element type.
        if (type.IsArray)
        {
            var elementTypeKeyword = GetKeywordName(type.GetElementType()!);
            return $"{elementTypeKeyword}[]";
        }

        // For non-generic, non-array types, use the string-based mapping.
        return GetKeywordName(type.Name);
    }

    public static string GetKeywordName(string typeName)
    {
        // Handle array types by recursively calling this method on the element type.
        if (typeName.EndsWith("[]"))
        {
            string elementType = typeName.Substring(0, typeName.Length - 2);
            string elementKeyword = GetKeywordName(elementType);
            return $"{elementKeyword}[]";
        }

        return typeName switch
        {
            "SByte" => "sbyte",
            "Byte" => "byte",
            "Int16" => "short",
            "UInt16" => "ushort",
            "Int32" => "int",
            "UInt32" => "uint",
            "Int64" => "long",
            "UInt64" => "ulong",
            "Char" => "char",
            "Single" => "float",
            "Double" => "double",
            "Boolean" => "bool",
            "Decimal" => "decimal",
            "String" => "string",
            _ => typeName,
        };
    }

    public static Type? GetType(string typeName) => Type.GetType(GetFullTypeName(typeName));

    public static string GetFullTypeName(string typeName) => typeName.ToLowerInvariant() switch
    {
        "sbyte" => "System.SByte",
        "byte" => "System.Byte",
        "short" => "System.Int16",
        "ushort" => "System.UInt16",
        "int" => "System.Int32",
        "uint" => "System.UInt32",
        "long" => "System.Int64",
        "ulong" => "System.UInt64",
        "char" => "System.Char",
        "float" => "System.Single",
        "double" => "System.Double",
        "bool" => "System.Boolean",
        "decimal" => "System.Decimal",
        "datetime" => "System.DateTime",
        "dateonly" => "System.DateOnly",
        "timeonly" => "System.TimeOnly",
        "guid" => "System.Guid",
        "string" => "System.String",
        "byte[]" => "System.Byte[]",
        _ => typeName,
    };

    public static bool IsCsTypeNullable(string csType) => csType.ToLowerInvariant() switch
    {
        "int" => true,
        "string" => false, // Reference type, nullability handled by context
        "bool" => true,
        "double" => true,
        "datetime" => true,
        "dateonly" => true,
        "timeonly" => true,
        "float" => true,
        "long" => true,
        "guid" => true,
        "byte[]" => false, // Reference type
        "decimal" => true,
        "enum" => true,
        "sbyte" => true,
        "byte" => true,
        "short" => true,
        "ushort" => true,
        "uint" => true,
        "ulong" => true,
        "char" => true,
        _ => false,
    };

    public static bool IsKnownCsType(string csType) => csType.ToLowerInvariant() switch
    {
        "int" => true,
        "string" => true,
        "bool" => true,
        "double" => true,
        "datetime" => true,
        "dateonly" => true,
        "timeonly" => true,
        "float" => true,
        "long" => true,
        "guid" => true,
        "byte[]" => true,
        "decimal" => true,
        "enum" => true,
        "sbyte" => true,
        "byte" => true,
        "short" => true,
        "ushort" => true,
        "uint" => true,
        "ulong" => true,
        "char" => true,
        _ => false,
    };

    public static bool IsPrimitiveType(string typeName) => GetKeywordName(typeName.ToLowerInvariant()) switch
    {
        "bool" => true,
        "byte" => true,
        "char" => true,
        "decimal" => true,
        "double" => true,
        "float" => true,
        "int" => true,
        "long" => true,
        "sbyte" => true,
        "short" => true,
        "uint" => true,
        "ulong" => true,
        "ushort" => true,
        _ => false,
    };

    public static string RemoveInterfacePrefix(string interfaceName) =>
        interfaceName.StartsWith("I") && interfaceName.Length > 1 && char.IsUpper(interfaceName[1])
            ? interfaceName.Substring(1)
            : interfaceName;

    private static string GetFriendlyTypeNameWithKeywords(Type type)
    {
        if (type.IsGenericType)
        {
            var baseName = type.Name;
            int index = baseName.IndexOf('`');
            if (index > 0)
            {
                baseName = baseName.Substring(0, index);
            }

            var genericArgs = type.GetGenericArguments();
            var genericArgNames = genericArgs.Select(GetKeywordName); // Use GetKeywordName recursively
            return $"{baseName}<{string.Join(", ", genericArgNames)}>";
        }

        return GetKeywordName(type.Name);
    }
}