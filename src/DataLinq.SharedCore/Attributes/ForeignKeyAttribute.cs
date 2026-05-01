using System;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public sealed class ForeignKeyAttribute : Attribute
{
    public ForeignKeyAttribute(string table, string column, string name)
        : this(table, column, name, null)
    {
    }

    public ForeignKeyAttribute(string table, string column, string name, int ordinal)
        : this(table, column, name, (int?)ordinal)
    {
    }

    private ForeignKeyAttribute(string table, string column, string name, int? ordinal)
    {
        Table = table;
        Column = column;
        Name = name;
        Ordinal = ordinal;
    }

    public string Table { get; }
    public string Column { get; }
    public string Name { get; }
    public int? Ordinal { get; }
}
