using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Exceptions;
using DataLinq.Linq.Visitors;
using DataLinq.Metadata;
using DataLinq.Mutation;
using Remotion.Linq.Clauses;

namespace DataLinq.Query;

public interface IQuery
{
    Sql ToSql(string? paramPrefix = null);
}

public class SqlQuery : SqlQuery<object>
{
    public SqlQuery(Transaction transaction, string? alias = null) : base(transaction, alias)
    {
    }

    public SqlQuery(TableMetadata table, Transaction transaction, string? alias = null) : base(table, transaction, alias)
    {
    }

    public SqlQuery(string tableName, Transaction transaction, string? alias = null) : base(tableName, transaction, alias)
    {
    }

    public SqlQuery Where(WhereClause where)
    {
        new WhereVisitor(this).Parse(where);

        return this;
    }

    public SqlQuery OrderBy(OrderByClause orderBy)
    {
        foreach (var ordering in orderBy.Orderings)
        {
            new OrderByVisitor(this).Parse(ordering);
        }

        return this;
    }
}

public class SqlQuery<T>
{
    protected WhereGroup<T> WhereGroup;
    internal Dictionary<string, object> SetList = new Dictionary<string, object>();
    protected List<Join<T>> JoinList = new List<Join<T>>();
    internal List<OrderBy> OrderByList = new List<OrderBy>();
    internal List<Column> WhatList;
    protected int? limit;
    protected int? offset;
    public bool LastIdQuery { get; protected set; }
    public Transaction Transaction { get; }

    public TableMetadata Table { get; }
    public string? Alias { get; }
    internal string DbName => string.IsNullOrEmpty(Alias)
        ? Table.DbName
        : $"{Table.DbName} {Alias}";

    public SqlQuery(Transaction transaction, string? alias = null)
    {
        CheckTransaction(transaction);

        this.Transaction = transaction;
        this.Table = transaction.Provider.Metadata.TableModels.Single(x => x.Model.CsType == typeof(T)).Table;
        this.Alias = alias;
    }

    public SqlQuery(TableMetadata table, Transaction transaction, string? alias = null)
    {
        CheckTransaction(transaction);

        this.Transaction = transaction;
        this.Table = table;
        this.Alias = alias;
    }

    public SqlQuery(string tableName, Transaction transaction, string? alias = null)
    {
        CheckTransaction(transaction);

        this.Transaction = transaction;
        this.Table = transaction.Provider.Metadata.TableModels.Single(x => x.Table.DbName == tableName).Table;
        this.Alias = alias;
    }

    private void CheckTransaction(Transaction transaction)
    {
        if (/*transaction.Type != TransactionType.ReadOnly && */(transaction.Status == DatabaseTransactionStatus.Committed || transaction.Status == DatabaseTransactionStatus.RolledBack))
            throw new Exception("Can't open a new connection on a committed or rolled back transaction");
    }

    public IEnumerable<T> Select()
    {
        return new Select<T>(this).ExecuteAs<T>();
    }

    public QueryResult Delete()
    {
        return new Delete<T>(this).Execute();
    }

    public QueryResult Insert()
    {
        return new Insert<T>(this).Execute();
    }

    public QueryResult Update()
    {
        return new Update<T>(this).Execute();
    }

    public virtual Select<T> SelectQuery()
    {
        return new Select<T>(this);
    }

    public Delete<T> DeleteQuery()
    {
        return new Delete<T>(this);
    }

    public Insert<T> InsertQuery()
    {
        return new Insert<T>(this);
    }

    public Update<T> UpdateQuery()
    {
        return new Update<T>(this);
    }

    public Where<T> Where(string columnName, string? alias = null)
    {
        if (WhereGroup == null)
            WhereGroup = new WhereGroup<T>(this);

        return WhereGroup.AddWhere(columnName, alias, BooleanType.And);
    }

    public WhereGroup<T> Where(IEnumerable<(string columnName, object? value)> wheres, BooleanType type = BooleanType.And, string? alias = null)
    {
        if (WhereGroup == null)
            WhereGroup = new WhereGroup<T>(this);

        foreach (var (columnName, value) in wheres)
            WhereGroup.AddWhere(columnName, alias, type).EqualTo(value);

        return WhereGroup;
    }

    public WhereGroup<T> Where(Func<Func<string, Where<T>>, WhereGroup<T>> func)
    {
        if (WhereGroup == null)
            WhereGroup = new WhereGroup<T>(this);

        return WhereGroup.And(func);
    }

    public Where<T> WhereNot(string columnName, string? alias = null)
    {
        if (WhereGroup == null)
            WhereGroup = new WhereGroup<T>(this);

        return WhereGroup.AddWhereNot(columnName, alias, BooleanType.And);
    }

    public WhereGroup<T> WhereNot(IEnumerable<(string columnName, object? value)> wheres, BooleanType type = BooleanType.And, string? alias = null)
    {
        if (WhereGroup == null)
            WhereGroup = new WhereGroup<T>(this);

        foreach (var (columnName, value) in wheres)
            WhereGroup.AddWhereNot(columnName, alias, type).EqualTo(value);

        return WhereGroup;
    }

    public WhereGroup<T> AddWhereGroup(BooleanType type = BooleanType.And)
    {
        if (WhereGroup == null)
            WhereGroup = new WhereGroup<T>(this);

        return WhereGroup.AddWhereGroup(new WhereGroup<T>(this), type);
    }

    public WhereGroup<T> AddWhereNotGroup(BooleanType type = BooleanType.And)
    {
        if (WhereGroup == null)
            WhereGroup = new WhereGroup<T>(this);

        return WhereGroup.AddWhereGroup(new WhereGroup<T>(this, true), type);
    }

    public WhereGroup<T> GetBaseWhereGroup(BooleanType type = BooleanType.And)
    {
        if (WhereGroup == null)
            WhereGroup = new WhereGroup<T>(this);

        return WhereGroup;
    }

    internal Sql GetWhere(Sql sql, string paramPrefix)
    {
        if (WhereGroup == null)
            return sql;

        sql.AddText("\nWHERE\n");
        WhereGroup.AddCommandString(sql, paramPrefix, true);

        return sql;
    }

    internal KeyValuePair<string, object> GetFields(Expression left, Expression right)
    {
        if (left is ConstantExpression && right is ConstantExpression)
            throw new InvalidQueryException("Unable to compare 2 constants.");

        if (left is MemberExpression && right is MemberExpression)
            throw new InvalidQueryException("Unable to compare 2 members.");

        if (left is MemberExpression)
            return GetValues(left, right);
        else
            return GetValues(right, left);
    }

    internal KeyValuePair<string, object> GetValues(Expression field, Expression value)
    {
        return new KeyValuePair<string, object>((string)GetValue(field), GetValue(value));
    }

    internal object GetValue(Expression expression)
    {
        if (expression is ConstantExpression constExp)
            return constExp.Value;
        else if (expression is MemberExpression propExp)
            return GetColumn(propExp).DbName;
        else
            throw new InvalidQueryException("Value is not a member or constant.");
    }

    internal Column? GetColumn(MemberExpression expression)
    {
        return Table.Columns.SingleOrDefault(x => x.ValueProperty.CsName == expression.Member.Name);
    }

    internal Sql GetJoins(Sql sql, string paramPrefix)
    {
        foreach (var join in JoinList)
            join.GetSql(sql, paramPrefix);

        return sql;
    }

    public Join<T> Join(string tableName, string? alias = null)
    {
        return Join(tableName, alias, JoinType.Inner);
    }

    public Join<T> LeftJoin(string tableName, string? alias = null)
    {
        return Join(tableName, alias, JoinType.LeftOuter);
    }

    public Join<T> RightJoin(string tableName, string? alias = null)
    {
        return Join(tableName, alias, JoinType.RightOuter);
    }

    private Join<T> Join(string tableName, string? alias, JoinType type)
    {
        if (JoinList == null)
            JoinList = new List<Join<T>>();

        if (alias == null)
            (tableName, alias) = QueryUtils.ParseTableNameAndAlias(tableName);

        var join = new Join<T>(this, tableName, alias, type);
        JoinList.Add(join);

        return join;
    }


    internal Sql GetOrderBy(Sql sql)
    {
        int length = OrderByList.Count;
        if (length == 0)
            return sql;

        sql.AddText("\nORDER BY ");
        sql.AddText(string.Join(", ", OrderByList.Select(x => $"{x.DbName}{(x.Ascending ? "" : " DESC")}")));

        return sql;
    }

    public SqlQuery<T> OrderBy(string columnName, string? alias = null, bool ascending = true)
    {
        if (alias == null)
            (columnName, alias) = QueryUtils.ParseColumnNameAndAlias(columnName);

        return OrderBy(this.Table.Columns.Single(x => x.DbName == columnName), alias, ascending);
    }

    public SqlQuery<T> OrderBy(Column column, string? alias = null, bool ascending = true)
    {
        if (!this.Table.Columns.Contains(column))
            throw new ArgumentException($"Column '{column.DbName}' does not belong to table '{Table.DbName}'");

        this.OrderByList.Add(new OrderBy(column, alias, ascending));

        return this;
    }

    public SqlQuery<T> OrderByDesc(string columnName, string? alias = null)
    {
        if (alias == null)
            (columnName, alias) = QueryUtils.ParseColumnNameAndAlias(columnName);

        return OrderByDesc(this.Table.Columns.Single(x => x.DbName == columnName), alias);
    }

    public SqlQuery<T> OrderByDesc(Column column, string? alias = null)
    {
        if (!this.Table.Columns.Contains(column))
            throw new ArgumentException($"Column '{column.DbName}' does not belong to table '{Table.DbName}'");

        this.OrderByList.Add(new OrderBy(column, alias, false));

        return this;
    }

    public SqlQuery<T> Limit(int limit)
    {
        if (limit < 0)
            throw new ArgumentException($"Argument 'rows' must be positive");

        this.limit = limit;

        return this;
    }

    public SqlQuery<T> Limit(int limit, int offset)
    {
        if (limit < 0)
            throw new ArgumentException($"Argument 'rows' must be positive");

        this.limit = limit;
        this.offset = offset;

        return this;
    }

    public SqlQuery<T> Offset(int offset)
    {
        if (offset < 0)
            throw new ArgumentException($"Argument 'rows' must be positive");

        this.offset = offset;

        return this;
    }

    internal Sql GetLimit(Sql sql)
    {
        return Transaction.Provider.GetLimitOffset(sql, limit, offset);
    }

    internal Sql GetSet(Sql sql, string paramPrefix)
    {
        int length = SetList.Count;
        if (length == 0)
            return sql;

        int i = 0;
        foreach (var with in SetList)
        {
            Transaction.Provider.GetParameter(sql, paramPrefix + "v" + i, with.Value);
            Transaction.Provider.GetParameterComparison(sql, with.Key, Relation.Equal, paramPrefix + "v" + i);

            if (i + 1 < length)
                sql.AddText(",");

            i++;
        }

        return sql;
    }

    public SqlQuery<T> Set<V>(string key, V value)
    {
        SetList.Add(key, value);
        return this;
    }


    public SqlQuery<T> What(IEnumerable<Column> columns)
    {
        WhatList ??= new List<Column>();
        WhatList.AddRange(columns);

        return this;
    }

    public SqlQuery<T> What(IEnumerable<string> columns)
    {
        return What(columns.Select(x => Table.Columns.Single(y => y.DbName == x)));
    }

    public SqlQuery<T> What(params string[] columns)
    {
        return What(columns.AsEnumerable());
    }

    public SqlQuery<T> AddLastIdQuery()
    {
        this.LastIdQuery = true;

        return this;
    }
}