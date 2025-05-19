using System;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public sealed class RelationAttribute : Attribute
{
    public RelationAttribute(string table, string column, string? name = null)
    {
        Table = table;
        Columns = [column];
        Name = name;
    }

    public RelationAttribute(string table, string[] column, string? name = null)
    {
        Table = table;
        Columns = column;
        Name = name;
    }

    public string Table { get; }
    public string[] Columns { get; }
    public string? Name { get; }
}