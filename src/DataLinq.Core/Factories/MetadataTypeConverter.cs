using System;

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

    public static string GetKeywordName(Type type) => GetKeywordName(type.Name);

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

    public static string GetFullTypeName(string typeName) => typeName switch
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
        "DateTime" => "System.DateTime",
        "DateOnly" => "System.DateOnly",
        "TimeOnly" => "System.TimeOnly",
        "Guid" => "System.Guid",
        "String" => "System.String",
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
}
