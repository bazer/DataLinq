using System;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class DefaultAttribute(DatabaseType databaseType, string value) : Attribute
{
    public DatabaseType DatabaseType { get; } = databaseType;
    public string Value { get; } = value;
}