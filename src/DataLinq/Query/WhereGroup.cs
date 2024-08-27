using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Query;

public enum BooleanType
{
    And,
    Or
}

public class WhereGroup<T> : IWhere<T>
{
    public readonly SqlQuery<T> Query;
    protected List<(IWhere<T> where, BooleanType type)>? whereList;

    private bool IsNegated = false;

    public Transaction Transaction => throw new NotImplementedException();

    internal WhereGroup(SqlQuery<T> query, bool isNegated = false)
    {
        Query = query;
        IsNegated = isNegated;
    }

    public void AddCommandString(Sql sql, string prefix = "", bool addCommandParameter = true, bool addParentheses = false)
    {
        int length = whereList?.Count ?? 0;
        if (length == 0)
            return;

        if (IsNegated)
            sql.AddText("NOT ");

        if (addParentheses || IsNegated)
            sql.AddText("(");

        //if (whereList!.All(x => x.type == BooleanType.Or && x.where is Where<T> w && w.IsValue && !w.IsNegated))
        //{
        //    sql.AddText("IN (");
        //    whereList.Select(x => Query.DataSource.Provider.GetParameterValue(sql, x.where.)
        //    sql.AddText("IN )");
        //}
        //else
        //{
            for (int i = 0; i < length; i++)
            {
                if (i != 0)
                {
                    if (whereList?[i].type == BooleanType.And)
                        sql.AddText(" AND ");
                    else if (whereList?[i].type == BooleanType.Or)
                        sql.AddText(" OR ");
                    else
                        throw new NotImplementedException();
                }

                whereList?[i].where.AddCommandString(sql, prefix, addCommandParameter, whereList[i].where is WhereGroup<T>);
            }
        //}

        if (addParentheses || IsNegated)
            sql.AddText(")");
    }

    public Where<T> AddWhere(string columnName, string? alias, BooleanType type)
    {
        return AddWhere(new Where<T>(this, columnName, alias), type);
    }

    public Where<T> AddWhereNot(string columnName, string? alias, BooleanType type)
    {
        return AddWhere(new Where<T>(this, columnName, alias, isNegated: true), type);
    }

    internal Where<T> AddWhere(Where<T> where, BooleanType type)
    {
        if (whereList == null)
            whereList = new List<(IWhere<T> where, BooleanType type)>();

        whereList.Add((where, type));

        return where;
    }

    internal WhereGroup<T> AddWhereGroup(WhereGroup<T> group, BooleanType type)
    {
        if (whereList == null)
            whereList = new List<(IWhere<T> where, BooleanType type)>();

        whereList.Add((group, type));

        return group;
    }

    public Where<T> And(string columnName, string? alias = null)
    {
        return AddWhere(new Where<T>(this, columnName, alias), BooleanType.And);
    }

    public WhereGroup<T> And(Func<Func<string, Where<T>>, WhereGroup<T>> func)
    {
        var group = AddWhereGroup(new WhereGroup<T>(this.Query), BooleanType.And);

        var where = new Where<T>(group);
        group.AddWhere(where, BooleanType.And);
        func(columnName => where.AddKey(columnName, null));

        return this;
    }

    public Where<T> Or(string columnName, string? alias = null)
    {
        return AddWhere(new Where<T>(this, columnName, alias), BooleanType.Or);
    }

    public WhereGroup<T> Or(Func<Func<string, Where<T>>, WhereGroup<T>> func)
    {
        var group = AddWhereGroup(new WhereGroup<T>(this.Query), BooleanType.Or);

        var where = new Where<T>(group);
        group.AddWhere(where, BooleanType.And);
        func(columnName => where.AddKey(columnName, null));

        return this;
    }

    public SqlQuery<T> Set<V>(string key, V value)
    {
        return Query.Set(key, value);
    }

    public IEnumerable<T> Select()
    {
        return Query.Select();
    }

    public QueryResult Delete()
    {
        return Query.Delete();
    }

    public QueryResult Insert()
    {
        return Query.Insert();
    }

    public QueryResult Update()
    {
        return Query.Update();
    }

    public Select<T> SelectQuery()
    {
        return new Select<T>(Query);
    }

    public Insert<T> InsertQuery()
    {
        return new Insert<T>(Query);
    }

    public Where<T> Where(string columnName, string? alias = null)
    {
        return Query.Where(columnName, alias);
    }

    public WhereGroup<T> Where(IEnumerable<(string columnName, object? value)> wheres, BooleanType type = BooleanType.And, string? alias = null)
    {
        return Query.Where(wheres, type, alias);
    }

    public WhereGroup<T> WhereNot(IEnumerable<(string columnName, object? value)> wheres, BooleanType type = BooleanType.And, string? alias = null)
    {
        return Query.WhereNot(wheres, type, alias);
    }

    public SqlQuery<T> OrderBy(string columnName, string? alias = null, bool ascending = true)
    {
        return Query.OrderBy(columnName, alias, ascending);
    }

    public SqlQuery<T> OrderBy(ColumnDefinition column, string? alias = null, bool ascending = true)
    {
        return Query.OrderBy(column, alias, ascending);
    }

    public SqlQuery<T> OrderByDesc(string columnName, string? alias = null)
    {
        return Query.OrderByDesc(columnName, alias);
    }

    public SqlQuery<T> OrderByDesc(ColumnDefinition column, string? alias = null)
    {
        return Query.OrderByDesc(column, alias);
    }

    public SqlQuery<T> Limit(int rows)
    {
        return Query.Limit(rows);
    }

    public Join<T> Join(string tableName, string? alias = null)
    {
        return Query.Join(tableName, alias);
    }

    public Join<T> LeftJoin(string tableName, string? alias = null)
    {
        return Query.Join(tableName, alias);
    }

    public Join<T> RightJoin(string tableName, string? alias = null)
    {
        return Query.Join(tableName, alias);
    }

    public override string ToString()
    {
        var sql = new Sql();
        AddCommandString(sql);

        return sql.ToString();
    }
}
