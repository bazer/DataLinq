using System;
using DataLinq.Metadata;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public sealed class TypeAttribute : Attribute
{
    public TypeAttribute(string name)
    {
        DatabaseType = DatabaseType.Default;
        Name = name;
    }

    public TypeAttribute(string name, ulong length)
    {
        DatabaseType = DatabaseType.Default;
        Name = name;
        Length = length;
    }

    public TypeAttribute(string name, bool signed)
    {
        DatabaseType = DatabaseType.Default;
        Name = name;
        Signed = signed;
    }

    public TypeAttribute(string name, ulong length, bool signed)
    {
        DatabaseType = DatabaseType.Default;
        Name = name;
        Length = length;
        Signed = signed;
    }

    public TypeAttribute(string name, ulong length, uint decimals, bool signed)
    {
        DatabaseType = DatabaseType.Default;
        Name = name;
        Length = length;
        Decimals = decimals;
        Signed = signed;
    }

    public TypeAttribute(DatabaseType databaseType, string name)
    {
        DatabaseType = databaseType;
        Name = name;
    }

    public TypeAttribute(DatabaseType databaseType, string name, ulong length)
    {
        DatabaseType = databaseType;
        Name = name;
        Length = length;
    }

    public TypeAttribute(DatabaseType databaseType, string name, ulong length, uint decimals)
    {
        DatabaseType = databaseType;
        Name = name;
        Length = length;
        Decimals = decimals;
    }

    public TypeAttribute(DatabaseType databaseType, string name, bool signed)
    {
        DatabaseType = databaseType;
        Name = name;
        Signed = signed;
    }

    public TypeAttribute(DatabaseType databaseType, string name, ulong length, bool signed)
    {
        DatabaseType = databaseType;
        Name = name;
        Length = length;
        Signed = signed;
    }

    public TypeAttribute(DatabaseType databaseType, string name, ulong length, uint decimals, bool signed)
    {
        DatabaseType = databaseType;
        Name = name;
        Length = length;
        Decimals = decimals;
        Signed = signed;
    }

    public TypeAttribute(DatabaseType databaseType, string name, ulong? length, uint? decimals, bool? signed)
    {
        DatabaseType = databaseType;
        Name = name;
        Length = length;
        Decimals = decimals;
        Signed = signed;
    }

    public TypeAttribute(DatabaseColumnType dbType)
    {
        DatabaseType = dbType.DatabaseType;
        Name = dbType.Name;
        Length = dbType.Length;
        Signed = dbType.Signed;
    }

    public ulong? Length { get; }
    public uint? Decimals { get; }
    public DatabaseType DatabaseType { get; }
    public string Name { get; }
    public bool? Signed { get; }
}