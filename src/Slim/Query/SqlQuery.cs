using Remotion.Linq.Clauses;
using Slim.Exceptions;
using Slim.Linq.Visitors;
using Slim.Metadata;
using Slim.Mutation;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;

namespace Slim.Query
{
    public interface IQuery
    {
        //Transaction Transaction { get; }

        //IDbCommand ToDbCommand();

        Sql ToSql(string paramPrefix = null);

        //int ParameterCount { get; }
    }

    public class SqlQuery : SqlQuery<object>
    {
        public SqlQuery(Transaction transaction) : base(transaction)
        {
        }

        public SqlQuery(Table table, Transaction transaction) : base(table, transaction)
        {
        }

        public SqlQuery(string tableName, Transaction transaction) : base(tableName, transaction)
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
        protected WhereGroup<T> WhereContainer;
        internal Dictionary<string, object> SetList = new Dictionary<string, object>();
        //protected List<Join<Query>> joinList = new List<Join<Query>>();
        internal List<OrderBy> OrderByList = new List<OrderBy>();
        internal List<Column> WhatList;
        public Transaction Transaction { get; }

        public Table Table { get; }

        public SqlQuery(Transaction transaction)
        {
            this.Transaction = transaction;
            this.Table = transaction.Provider.Metadata.Tables.Single(x => x.Model.CsType == typeof(T));
        }

        public SqlQuery(Table table, Transaction transaction)
        {
            this.Transaction = transaction;
            this.Table = table;
        }

        public SqlQuery(string tableName, Transaction transaction)
        {
            this.Transaction = transaction;
            this.Table = transaction.Provider.Metadata.Tables.Single(x => x.DbName == tableName);
        }

        public IEnumerable<T> Select()
        {
            return new Select<T>(this).Execute();
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

        public Where<T> Where(string columnName)
        {
            if (WhereContainer == null)
                WhereContainer = new WhereGroup<T>(this);

            return WhereContainer.AddWhere(columnName, BooleanType.And);
        }

        public WhereGroup<T> Where(Func<Func<string, Where<T>>, WhereGroup<T>> func)
        {
            if (WhereContainer == null)
                WhereContainer = new WhereGroup<T>(this);

            return WhereContainer.And(func);
        }

        public WhereGroup<T> CreateWhereGroup(BooleanType type = BooleanType.And)
        {
            if (WhereContainer == null)
                WhereContainer = new WhereGroup<T>(this);

            return WhereContainer.AddWhereContainer(new WhereGroup<T>(this), type);
        }

        internal Sql GetWhere(Sql sql, string paramPrefix)
        {
            if (WhereContainer == null)
                return sql;

            sql.AddText("\r\nWHERE");
            WhereContainer.AddCommandString(sql, paramPrefix, true);

            return sql;
        }

        internal KeyValuePair<string, object> GetFields(BinaryExpression node)
        {
            if (node.Left is ConstantExpression && node.Right is ConstantExpression)
                throw new InvalidQueryException("Unable to compare 2 constants.");

            if (node.Left is MemberExpression)
                return GetValues(node.Left, node.Right);
            else
                return GetValues(node.Right, node.Left);
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

        internal Column GetColumn(MemberExpression expression)
        {
            return Table.Columns.Single(x => x.ValueProperty.CsName == expression.Member.Name);
        }


        //protected Sql GetJoins(Sql sql, string tableAlias)
        //{
        //    foreach (var join in joinList)
        //        join.GetSql(sql, tableAlias);

        //    return sql;
        //}

        //public Join<Select> InnerJoin(string tableName)
        //{
        //    var join = new Join<Select>(this, tableName, JoinType.Inner);
        //    joinList.Add(join);

        //    return join;
        //}

        //public Join<Select> OuterJoin(string tableName)
        //{
        //    var join = new Join<Select>(this, tableName, JoinType.Outer);
        //    joinList.Add(join);

        //    return join;
        //}

        internal Sql GetOrderBy(Sql sql)
        {
            int length = OrderByList.Count;
            if (length == 0)
                return sql;

            sql.AddText("\r\nORDER BY ");
            sql.AddText(string.Join(", ", OrderByList.Select(x => $"{x.Column.DbName}{(x.Ascending ? "" : " DESC")}")));

            return sql;
        }

        public SqlQuery<T> OrderBy(string columnName, bool ascending = true)
        {
            var column = this.Table.Columns.Single(x => x.DbName == columnName);

            return OrderBy(column, ascending);
        }

        public SqlQuery<T> OrderBy(Column column, bool ascending = true)
        {
            if (!this.Table.Columns.Contains(column))
                throw new ArgumentException($"Column '{column.DbName}' does not belong to table '{Table.DbName}'");

            this.OrderByList.Add(new OrderBy(column, ascending));

            return this;
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
    }
}