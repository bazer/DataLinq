using System;
using System.Linq;

namespace DataLinq.Core.Factories;

public static class MetadataTypeConverter
{
    public static int? CsTypeSize(string csType) => csType switch
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
        "DateTime" => 8,
        "DateOnly" => sizeof(long),
        "Guid" => 16,
        "String" => null,
        "byte[]" => null,
        "enum" => sizeof(int),
        _ => null
    };

    public static string GetKeywordName(Type type)
    {
        // Special handling for array types
        if (type.IsArray)
        {
            // Recursively get the C# keyword for the element type (e.g., System.Byte -> byte)
            var elementTypeKeyword = GetKeywordName(type.GetElementType()!);
            return $"{elementTypeKeyword}[]";
        }

        if (type.IsGenericType)
            return GetFriendlyTypeName(type);

        // Fallback to the string-based version for non-generic, non-array types
        return GetKeywordName(type.Name);
    }

    public static string GetKeywordName(string typeName) => typeName switch
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

    public static Type GetType(string typeName) => Type.GetType(GetFullTypeName(typeName));

    public static string GetFullTypeName(string typeName) => typeName.ToLower() switch
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

    public static bool IsCsTypeNullable(string csType) => csType switch
    {
        "int" => true,
        "string" => false,
        "bool" => true,
        "double" => true,
        "DateTime" => true,
        "DateOnly" => true,
        "TimeOnly" => true,
        "float" => true,
        "long" => true,
        "Guid" => true,
        "byte[]" => false,
        "decimal" => true,
        "enum" => true,
        "sbyte" => true,
        "byte" => true,
        "short" => true,
        "ushort" => true,
        "uint" => true,
        "ulong" => true,
        "char" => true,
        "String" => false,
        _ => false,
    };

    public static bool IsKnownCsType(string csType) => csType switch
    {
        "int" => true,
        "string" => true,
        "bool" => true,
        "double" => true,
        "DateTime" => true,
        "DateOnly" => true,
        "TimeOnly" => true,
        "float" => true,
        "long" => true,
        "Guid" => true,
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
        "String" => true,
        _ => false,
    };

    public static bool IsPrimitiveType(string typeName) => GetKeywordName(typeName) switch
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

    private static string GetFriendlyTypeName(Type type)
    {
        if (type.IsGenericType)
        {
            // Get the base name without the trailing `N part.
            var baseName = type.Name;
            int index = baseName.IndexOf('`');
            if (index > 0)
            {
                baseName = baseName.Substring(0, index);
            }
            // Process each generic argument recursively (if necessary)
            var genericArgs = type.GetGenericArguments();
            var genericArgNames = genericArgs.Select(GetFriendlyTypeName);
            return $"{baseName}<{string.Join(", ", genericArgNames)}>";
        }
        else
        {
            return type.Name;
        }
    }
}
