﻿using DataLinq.Metadata;

namespace DataLinq.Query;

public class OrderBy
{
    public Column Column { get; }
    public string? Alias { get; }
    public bool Ascending { get; }

    internal string DbName(string escapeCharacter) => string.IsNullOrEmpty(Alias)
        ? $"{escapeCharacter}{Column.DbName}{escapeCharacter}"
        : $"{Alias}.{escapeCharacter}{Column.DbName}{escapeCharacter}";

    public OrderBy(Column column, string? alias, bool ascending)
    {
        this.Column = column;
        this.Alias = alias;
        this.Ascending = ascending;
    }
}