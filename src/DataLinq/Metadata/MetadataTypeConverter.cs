﻿using System;

namespace DataLinq.Metadata;

public static class MetadataTypeConverter
{
    public static int? CsTypeSize(string csType)
    {
        return csType switch
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
    }

    public static string GetKeywordName(Type type)
    {
        switch (type.Name)
        {
            case "SByte":
                return "sbyte";
            case "Byte":
                return "byte";
            case "Int16":
                return "short";
            case "UInt16":
                return "ushort";
            case "Int32":
                return "int";
            case "UInt32":
                return "uint";
            case "Int64":
                return "long";
            case "UInt64":
                return "ulong";
            case "Char":
                return "char";
            case "Single":
                return "float";
            case "Double":
                return "double";
            case "Boolean":
                return "bool";
            case "Decimal":
                return "decimal";
            case "String":
                return "string";
            default:
                return type.Name;
        }
    }

    public static bool IsCsTypeNullable(string csType)
    {
        return csType switch
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
            _ => throw new NotImplementedException($"Unknown type '{csType}'"),
        };
    }
}
