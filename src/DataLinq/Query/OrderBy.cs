using System;
using DataLinq.Metadata;

namespace DataLinq.Query;

public class OrderBy
{
    public ColumnDefinition? Column { get; }
    public string? Alias { get; }
    public string? RawExpression { get; }
    public bool Ascending { get; }

    internal string DbName(string escapeCharacter) => string.IsNullOrEmpty(Alias)
        ? RawExpression ?? $"{escapeCharacter}{Column!.DbName}{escapeCharacter}"
        : $"{Alias}.{escapeCharacter}{Column!.DbName}{escapeCharacter}";

    internal void AddDbName(Sql sql, string escapeCharacter)
    {
        if (RawExpression is not null)
        {
            sql.AddText(RawExpression);
            return;
        }

        var column = Column ?? throw new InvalidOperationException("Column orderings require a column definition.");
        if (!string.IsNullOrEmpty(Alias))
        {
            sql.AddText(Alias);
            sql.AddText(".");
        }

        sql.AddText(escapeCharacter);
        sql.AddText(column.DbName);
        sql.AddText(escapeCharacter);
    }

    public OrderBy(ColumnDefinition column, string? alias, bool ascending)
    {
        this.Column = column;
        this.Alias = alias;
        this.Ascending = ascending;
    }

    public OrderBy(string rawExpression, bool ascending)
    {
        if (string.IsNullOrWhiteSpace(rawExpression))
            throw new ArgumentException("Raw order-by expressions cannot be empty.", nameof(rawExpression));

        RawExpression = rawExpression;
        Ascending = ascending;
    }
}
