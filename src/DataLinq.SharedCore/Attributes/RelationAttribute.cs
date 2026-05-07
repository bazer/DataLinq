using System;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public sealed class RelationAttribute : Attribute
{
    private readonly string[] columns;

    public RelationAttribute(string table, string column, string? name = null)
    {
        Table = table;
        columns = [column];
        Name = name;
    }

    public RelationAttribute(string table, string[] column, string? name = null)
    {
        Table = table;
        columns = column is null ? [] : [.. column];
        Name = name;
    }

    public string Table { get; }
    public string[] Columns => [.. columns];
    public string? Name { get; }
}
