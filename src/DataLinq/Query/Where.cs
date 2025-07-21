using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Extensions.Helpers;

namespace DataLinq.Query;

public enum Relation
{
    Equal,
    EqualNull,
    NotEqual,
    NotEqualNull,
    Like,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    In,
    NotIn,
    AlwaysFalse,
    AlwaysTrue
}

public interface IWhere<T> : IQueryPart
{
}

public class Where<T> : IWhere<T>
{
    private string? Key;
    private object?[]? Value;
    private string? RawSql;
    private Relation Relation;
    internal bool IsValue = true;
    internal bool IsRaw = false;
    internal bool IsNegated = false;
    protected WhereGroup<T> WhereGroup;
    private string? KeyAlias;
    private string? ValueAlias;


    private string KeyName => string.IsNullOrEmpty(KeyAlias)
        ? $"{WhereGroup.Query.EscapeCharacter}{Key}{WhereGroup.Query.EscapeCharacter}"
        : $"{KeyAlias}.{WhereGroup.Query.EscapeCharacter}{Key}{WhereGroup.Query.EscapeCharacter}";

    private string ValueName => string.IsNullOrEmpty(ValueAlias)
        ? $"{WhereGroup.Query.EscapeCharacter}{Value?[0] as string}{WhereGroup.Query.EscapeCharacter}"
        : $"{ValueAlias}.{WhereGroup.Query.EscapeCharacter}{Value?[0]}{WhereGroup.Query.EscapeCharacter}";

    internal Where(WhereGroup<T> group, string key, string? keyAlias, bool isValue = true, bool isNegated = false)
    {
        if (keyAlias == null)
            (key, keyAlias) = QueryUtils.ParseColumnNameAndAlias(key);

        WhereGroup = group;
        Key = key;
        IsValue = isValue;
        IsNegated = isNegated;
        KeyAlias = keyAlias;
    }

    internal Where(WhereGroup<T> group, Relation fixedRelation)
    {
        if (fixedRelation != Relation.AlwaysTrue && fixedRelation != Relation.AlwaysFalse)
            throw new ArgumentException("This constructor is for AlwaysTrue or AlwaysFalse relations only.");

        WhereGroup = group;
        Relation = fixedRelation;
    }

    internal Where(WhereGroup<T> group)
    {
        WhereGroup = group;
    }

    internal Where<T> AddKey(string key, string? alias, bool isValue = true)
    {
        Key = key;
        IsValue = isValue;
        KeyAlias = alias;

        return this;
    }

    public WhereGroup<T> EqualTo<V>(V value)
    {
        return SetAndReturn(value, value == null ? Relation.EqualNull : Relation.Equal);
    }

    public WhereGroup<T> EqualToNull()
    {
        return SetAndReturnNull(Relation.EqualNull);
    }

    public WhereGroup<T> EqualToColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Relation.Equal);
    }

    public WhereGroup<T> NotEqualTo<V>(V value)
    {
        return SetAndReturn(value, value == null ? Relation.NotEqualNull : Relation.NotEqual);
    }

    public WhereGroup<T> NotEqualToNull()
    {
        return SetAndReturnNull(Relation.NotEqualNull);
    }

    public WhereGroup<T> NotEqualToColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Relation.NotEqual);
    }

    public WhereGroup<T> EqualToRaw(string sql)
    {
        return SetAndReturnRaw(sql, Relation.Equal);
    }

    public WhereGroup<T> NotEqualToRaw(string sql)
    {
        return SetAndReturnRaw(sql, Relation.NotEqual);
    }

    public WhereGroup<T> GreaterThanRaw(string sql)
    {
        return SetAndReturnRaw(sql, Relation.GreaterThan);
    }

    public WhereGroup<T> GreaterThanOrEqualToRaw(string sql)
    {
        return SetAndReturnRaw(sql, Relation.GreaterThanOrEqual);
    }

    public WhereGroup<T> LessThanRaw(string sql)
    {
        return SetAndReturnRaw(sql, Relation.LessThan);
    }

    public WhereGroup<T> LessThanOrEqualToRaw(string sql)
    {
        return SetAndReturnRaw(sql, Relation.LessThanOrEqual);
    }

    public WhereGroup<T> Like<V>(V value)
    {
        return SetAndReturn(value, Relation.Like);
    }

    public WhereGroup<T> LikeColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Relation.Like);
    }

    public WhereGroup<T> GreaterThan<V>(V value)
    {
        return SetAndReturn(value, Relation.GreaterThan);
    }

    public WhereGroup<T> GreaterThanColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Relation.GreaterThan);
    }

    public WhereGroup<T> GreaterThanOrEqual<V>(V value)
    {
        return SetAndReturn(value, Relation.GreaterThanOrEqual);
    }

    public WhereGroup<T> GreaterThanOrEqualToColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Relation.GreaterThanOrEqual);
    }

    public WhereGroup<T> LessThan<V>(V value)
    {
        return SetAndReturn(value, Relation.LessThan);
    }

    public WhereGroup<T> LessThanColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Relation.LessThan);
    }

    public WhereGroup<T> LessThanOrEqual<V>(V value)
    {
        return SetAndReturn(value, Relation.LessThanOrEqual);
    }

    public WhereGroup<T> LessThanOrEqualToColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Relation.LessThanOrEqual);
    }

    public WhereGroup<T> In<V>(IEnumerable<V> values) =>
        In(values.ToArray());

    public WhereGroup<T> In<V>(params V[] values)
    {
        return SetAndReturn(values, Relation.In);
    }

    public WhereGroup<T> NotIn<V>(IEnumerable<V> values) =>
        NotIn(values.ToArray());

    public WhereGroup<T> NotIn<V>(params V[] values)
    {
        return SetAndReturn(values, Relation.NotIn);
    }

    protected WhereGroup<T> SetAndReturn<V>(V[] value, Relation relation)
    {
        Value = value.Cast<object>().ToArray();
        Relation = relation;
        return this.WhereGroup;
    }

    protected WhereGroup<T> SetAndReturn<V>(V? value, Relation relation)
    {
        Value = [value];
        Relation = relation;
        return this.WhereGroup;
    }

    protected WhereGroup<T> SetAndReturnNull(Relation relation)
    {
        Value = null;
        Relation = relation;
        return this.WhereGroup;
    }

    protected WhereGroup<T> SetAndReturnColumn(string column, string? alias, Relation relation)
    {
        if (alias == null)
            (column, alias) = QueryUtils.ParseColumnNameAndAlias(column);

        Value = [column];
        ValueAlias = alias;
        IsValue = false;
        IsRaw = false;
        Relation = relation;

        return this.WhereGroup;
    }

    private WhereGroup<T> SetAndReturnRaw(string sql, Relation relation)
    {
        RawSql = sql;
        IsValue = false;
        IsRaw = true;
        Relation = relation;
        return WhereGroup;
    }

    public void AddCommandString(Sql sql, string prefix, bool addCommandParameter = true, bool addParentheses = false)
    {
        // Handle fixed conditions first
        if (Relation == Relation.AlwaysTrue || Relation == Relation.AlwaysFalse)
        {
            if (Relation == Relation.AlwaysFalse)
                sql.AddText(IsNegated ? "1=1" : "1=0");
            else if (Relation == Relation.AlwaysTrue)
                sql.AddText(IsNegated ? "1=0" : "1=1");
            return;
        }


        addParentheses = addParentheses || IsNegated; // || Relation == Relation.In || Relation == Relation.NotIn;

        var indexList = addCommandParameter ? GetCommandParameter(sql, prefix).ToArray() : ([sql.Index]);

        if (IsNegated)
            sql.AddText("NOT ");

        if (addParentheses)
            sql.AddText("(");

        if (IsValue)
        {
            // Check for empty IN/NOT IN here before calling provider
            if ((Relation == Relation.In || Relation == Relation.NotIn) && (Value == null || Value.Length == 0))
            {
                // Empty list for IN means false, for NOT IN means true
                sql.AddText(Relation == Relation.In ? "1=0" : "1=1");
            }
            else
            {
                WhereGroup.Query.DataSource.Provider.GetParameterComparison(sql, KeyName, Relation, indexList.Select(x => prefix + "w" + x).ToArray());
            }
        }
        else if (IsRaw)
        {
            sql.AddFormat("{0} {1} {2}", KeyName, Relation.ToSql(), RawSql);
        }
        else
            sql.AddFormat("{0} {1} {2}", KeyName, Relation.ToSql(), ValueName);

        if (addParentheses)
            sql.AddText(")");
    }

    protected IEnumerable<int> GetCommandParameter(Sql sql, string prefix)
    {
        if (IsValue)
        {
            if (Value == null)
            {
                yield return sql.Index;
                WhereGroup.Query.DataSource.Provider.GetParameter(sql, prefix + "w" + sql.IndexAdd(), null);
            }
            else
            {
                foreach (var value in Value)
                {
                    yield return sql.Index;
                    WhereGroup.Query.DataSource.Provider.GetParameter(sql, prefix + "w" + sql.IndexAdd(), value);
                }
            }
        }
    }

    public override string ToString()
    {
        var sql = new Sql();
        AddCommandString(sql, "");

        return sql.ToString();
    }
}