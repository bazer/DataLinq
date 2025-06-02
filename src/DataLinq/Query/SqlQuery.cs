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
    public SqlQuery(DataSourceAccess transaction, string? alias = null) : base(transaction, alias)
    {
    }

    public SqlQuery(TableDefinition table, DataSourceAccess transaction, string? alias = null) : base(table, transaction, alias)
    {
    }

    public SqlQuery(string tableName, DataSourceAccess transaction, string? alias = null) : base(tableName, transaction, alias)
    {
    }

    //public SqlQuery Where(WhereClause where)
    //{
    //    new WhereVisitor<object>(this).Parse(where);

    //    return this;
    //}

    //public SqlQuery OrderBy(OrderByClause orderBy)
    //{
    //    foreach (var ordering in orderBy.Orderings)
    //    {
    //        new OrderByVisitor<object>(this).Parse(ordering);
    //    }

    //    return this;
    //}
}

public class SqlQuery<T>
{
    protected WhereGroup<T> WhereGroup;
    internal Dictionary<string, object> SetList = new Dictionary<string, object>();
    protected List<Join<T>> JoinList = new List<Join<T>>();
    internal List<OrderBy> OrderByList = new List<OrderBy>();
    internal List<string> WhatList;
    protected int? limit;
    protected int? offset;
    public bool LastIdQuery { get; protected set; }
    public DataSourceAccess DataSource { get; }

    public TableDefinition Table { get; }
    public string? Alias { get; }

    internal string EscapeCharacter => DataSource.Provider.Constants.EscapeCharacter;

    public SqlQuery(DataSourceAccess dataSource, string? alias = null)
    {
        CheckTransaction(dataSource);

        this.DataSource = dataSource;
        this.Table = dataSource.Provider.Metadata.TableModels.Single(x => x.Model.CsType.Type == typeof(T)).Table;
        this.Alias = alias;
    }

    public SqlQuery(TableDefinition table, DataSourceAccess transaction, string? alias = null)
    {
        CheckTransaction(transaction);

        this.DataSource = transaction;
        this.Table = table;
        this.Alias = alias;
    }

    public SqlQuery(string tableName, DataSourceAccess transaction, string? alias = null)
    {
        CheckTransaction(transaction);

        this.DataSource = transaction;
        this.Table = transaction.Provider.Metadata.TableModels.Single(x => x.Table.DbName == tableName).Table;
        this.Alias = alias;
    }

    private void CheckTransaction(DataSourceAccess dataSource)
    {
        if (dataSource is Transaction transaction && (transaction.Status == DatabaseTransactionStatus.Committed || transaction.Status == DatabaseTransactionStatus.RolledBack))
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
        WhereGroup ??= new WhereGroup<T>(this);

        return WhereGroup.AddWhere(columnName, alias, BooleanType.And);
    }

    public WhereGroup<T> Where(IEnumerable<(string columnName, object? value)> wheres, BooleanType type = BooleanType.And, string? alias = null)
    {
        WhereGroup ??= new WhereGroup<T>(this);

        foreach (var (columnName, value) in wheres)
            WhereGroup.AddWhere(columnName, alias, type).EqualTo(value);

        return WhereGroup;
    }

    public WhereGroup<T> Where(Func<Func<string, Where<T>>, WhereGroup<T>> func)
    {
        WhereGroup ??= new WhereGroup<T>(this);

        return WhereGroup.And(func);
    }

    public Where<T> WhereNot(string columnName, string? alias = null)
    {
        WhereGroup ??= new WhereGroup<T>(this);

        return WhereGroup.AddWhereNot(columnName, alias, BooleanType.And);
    }

    public WhereGroup<T> WhereNot(IEnumerable<(string columnName, object? value)> wheres, BooleanType type = BooleanType.And, string? alias = null)
    {
        WhereGroup ??= new WhereGroup<T>(this);

        foreach (var (columnName, value) in wheres)
            WhereGroup.AddWhereNot(columnName, alias, type).EqualTo(value);

        return WhereGroup;
    }

    public WhereGroup<T> AddWhereGroup(BooleanType type = BooleanType.And)
    {
        WhereGroup ??= new WhereGroup<T>(this);

        // Create a new sub-group. Its internal logic will be AND by default.
        var newSubGroup = new WhereGroup<T>(this, BooleanType.And, false);
        // Add this new sub-group to the main WhereGroup, connecting it with the specified 'type'.
        this.WhereGroup.AddSubGroup(newSubGroup, type);
        return newSubGroup; // Return the new sub-group for further chaining.
    }

    public WhereGroup<T> AddWhereNotGroup(BooleanType type = BooleanType.And)
    {
        WhereGroup ??= new WhereGroup<T>(this);

        // Create a new negated sub-group. Its internal logic will be AND by default.
        var newSubGroup = new WhereGroup<T>(this, BooleanType.And, true); // true for isNegated
        // Add this new sub-group to the main WhereGroup, connecting it with the specified 'type'.
        this.WhereGroup.AddSubGroup(newSubGroup, type);
        return newSubGroup; // Return the new negated sub-group.
    }

    public WhereGroup<T> GetBaseWhereGroup(BooleanType type = BooleanType.And)
    {
        WhereGroup ??= new WhereGroup<T>(this, type);

        return WhereGroup;
    }

    public SqlQuery<T> Where(WhereClause where)
    {
        new WhereVisitor<T>(this).Parse(where);

        return this;
    }

    public SqlQuery<T> OrderBy(OrderByClause orderBy)
    {
        foreach (var ordering in orderBy.Orderings)
        {
            new OrderByVisitor<T>(this).Parse(ordering);
        }

        return this;
    }

    internal Sql GetWhere(Sql sql, string? paramPrefix)
    {
        // Ensure WhereGroup is initialized (should be by constructor)
        if (this.WhereGroup == null || this.WhereGroup.Length == 0)
            return sql;

        sql.AddText("\nWHERE\n");
        // The root WhereGroup doesn't need outer parentheses unless it's negated (which it isn't by default here)
        // or if it internally has only one child that is a group needing parens.
        // AddCommandString now handles its own parentheses better.
        bool rootGroupNeedsParens = this.WhereGroup.IsNegated || (this.WhereGroup.Length > 1);
        this.WhereGroup.AddCommandString(sql, paramPrefix, true, rootGroupNeedsParens);

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

    internal ColumnDefinition? GetColumn(MemberExpression expression)
    {
        return Table.Columns.SingleOrDefault(x => x.ValueProperty.PropertyName == expression.Member.Name);
    }

    internal Sql AddTableName(Sql sql, string tableName, string? alias)
    {
        DataSource.Provider.GetTableName(sql, tableName, alias);

        return sql;
    }

    internal Sql GetJoins(Sql sql, string? paramPrefix)
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
        sql.AddText(string.Join(", ", OrderByList.Select(x => $"{x.DbName(EscapeCharacter)}{(x.Ascending ? "" : " DESC")}")));

        return sql;
    }

    public SqlQuery<T> OrderBy(string columnName, string? alias = null, bool ascending = true)
    {
        if (alias == null)
            (columnName, alias) = QueryUtils.ParseColumnNameAndAlias(columnName);

        return OrderBy(this.Table.Columns.Single(x => x.DbName == columnName), alias, ascending);
    }

    public SqlQuery<T> OrderBy(ColumnDefinition column, string? alias = null, bool ascending = true)
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

    public SqlQuery<T> OrderByDesc(ColumnDefinition column, string? alias = null)
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
        return DataSource.Provider.GetLimitOffset(sql, limit, offset);
    }

    internal Sql GetSet(Sql sql, string? paramPrefix)
    {
        int length = SetList.Count;
        if (length == 0)
            return sql;

        int i = 0;
        foreach (var with in SetList)
        {
            DataSource.Provider.GetParameter(sql, paramPrefix + "v" + i, with.Value);
            DataSource.Provider.GetParameterComparison(sql, with.Key, Relation.Equal, [paramPrefix + "v" + i]);

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


    public SqlQuery<T> What(IEnumerable<ColumnDefinition> columns)
    {
        return What(columns.Select(x => $"{EscapeCharacter}{x.DbName}{EscapeCharacter}"));
    }

    public SqlQuery<T> What(IEnumerable<string> selectors)
    {
        WhatList ??= [];
        WhatList.AddRange(selectors.Select(x => Table.Columns.Any(y => y.DbName == x) ? $"{EscapeCharacter}{x}{EscapeCharacter}" : x));

        return this;
    }

    public SqlQuery<T> What(params string[] selectors)
    {
        return What(selectors.AsEnumerable());
    }

    public SqlQuery<T> AddLastIdQuery()
    {
        this.LastIdQuery = true;

        return this;
    }
}