using System;

namespace DataLinq.Attributes;

public enum ReferentialAction
{
    Unspecified,
    NoAction,
    Restrict,
    Cascade,
    SetNull,
    SetDefault
}

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public sealed class ForeignKeyAttribute : Attribute
{
    public ForeignKeyAttribute(string table, string column, string name)
        : this(table, column, name, null, ReferentialAction.Unspecified, ReferentialAction.Unspecified)
    {
    }

    public ForeignKeyAttribute(string table, string column, string name, int ordinal)
        : this(table, column, name, (int?)ordinal, ReferentialAction.Unspecified, ReferentialAction.Unspecified)
    {
    }

    public ForeignKeyAttribute(string table, string column, string name, ReferentialAction onUpdate, ReferentialAction onDelete)
        : this(table, column, name, null, onUpdate, onDelete)
    {
    }

    public ForeignKeyAttribute(string table, string column, string name, int ordinal, ReferentialAction onUpdate, ReferentialAction onDelete)
        : this(table, column, name, (int?)ordinal, onUpdate, onDelete)
    {
    }

    private ForeignKeyAttribute(string table, string column, string name, int? ordinal, ReferentialAction onUpdate, ReferentialAction onDelete)
    {
        Table = table;
        Column = column;
        Name = name;
        Ordinal = ordinal;
        OnUpdate = onUpdate;
        OnDelete = onDelete;
    }

    public string Table { get; }
    public string Column { get; }
    public string Name { get; }
    public int? Ordinal { get; }
    public ReferentialAction OnUpdate { get; }
    public ReferentialAction OnDelete { get; }
}
