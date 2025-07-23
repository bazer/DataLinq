using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;

namespace DataLinq.Query;

public abstract class Operand
{
    public static ColumnOperand Column(string name, string? alias = null) => new(name, alias);
    public static ColumnOperandWithDefinition Column(ColumnDefinition column, string? alias = null) => new(column, alias);

    public static RawSqlOperand RawSql(string sql) => new(sql);

    public static ValueOperand Value(object? value) => new([value]);
    public static ValueOperand Value(object?[] values) => new(values);
    public static ValueOperand Value(IEnumerable<object> values) => new([.. values]);
}

public class ColumnOperand : Operand
{
    public string Name { get; }
    public string? Alias { get; }

    internal ColumnOperand(string name, string? alias = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Column name cannot be null or empty.", nameof(name));

        if (alias == null)
            (Name, Alias) = QueryUtils.ParseColumnNameAndAlias(name);
        else
        {
            Name = name;
            Alias = alias;
        }
    }

    public string FormatName(string escapeCharacter) => string.IsNullOrEmpty(Alias)
        ? $"{escapeCharacter}{Name}{escapeCharacter}"
        : $"{Alias}.{escapeCharacter}{Name}{escapeCharacter}";

    public override string ToString()
    {
        return string.IsNullOrEmpty(Alias) ? $"{Name}" : $"{Alias}.{Name}";
    }
}

public class ColumnOperandWithDefinition : ColumnOperand
{
    public ColumnDefinition ColumnDefinition { get; }
    internal ColumnOperandWithDefinition(ColumnDefinition column, string? alias = null)
        : base(column.DbName, alias)
    {
        ColumnDefinition = column;
    }
}

public class RawSqlOperand : Operand
{
    public string Sql { get; }

    internal RawSqlOperand(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("Function name cannot be null or empty.", nameof(sql));

        Sql = sql;
    }

    public override string ToString()
    {
        return $"{Sql}";
    }
}

public class ValueOperand : Operand
{
    public object?[] Values { get; }
    public bool HasOneValue => Values.Length == 1;
    public object? FirstValue => Values[0];

    public bool IsNull => Values.Length == 1 && Values[0] == null;

    internal ValueOperand(object?[] values)
    {
        if (values == null || values.Length == 0)
            throw new ArgumentException("Value cannot be null or empty.", nameof(values));

        Values = values;
    }

    public override string ToString()
    {
        return Values.Select(x => x == null ? "NULL" : x.ToString()).ToJoinedString(", ");
    }
}
