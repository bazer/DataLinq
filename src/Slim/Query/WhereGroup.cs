using Slim.Metadata;
using Slim.Mutation;
using System;
using System.Collections.Generic;

namespace Slim.Query
{
    public enum BooleanType
    {
        And,
        Or
    }

    public class WhereGroup<T> : IWhere<T>
    {
        public readonly SqlQuery<T> Query;
        protected List<(IWhere<T> where, BooleanType type)> whereList;

        public Transaction Transaction => throw new NotImplementedException();

        internal WhereGroup(SqlQuery<T> query)
        {
            this.Query = query;

        }

        public void AddCommandString(Sql sql, string prefix, bool addCommandParameter, bool addParentheses = false)
        {
            int length = whereList?.Count ?? 0;
            if (length == 0)
                return;

            if (addParentheses)
                sql.AddText("(");
            else
                sql.AddText("\r\n");

            for (int i = 0; i < length; i++)
            {
                if (i != 0)
                {
                    if (whereList[i].type == BooleanType.And)
                        sql.AddText(" AND ");
                    else if (whereList[i].type == BooleanType.Or)
                        sql.AddText(" OR ");
                    else
                        throw new NotImplementedException();
                }

                whereList[i].where.AddCommandString(sql, prefix, addCommandParameter, whereList[i].where is WhereGroup<T>);
            }

            if (addParentheses)
                sql.AddText(")");
        }

        public Where<T> AddWhere(string columnName, BooleanType type)
        {
            return AddWhere(new Where<T>(this, columnName), type);
        }

        internal Where<T> AddWhere(Where<T> where, BooleanType type)
        {
            if (whereList == null)
                whereList = new List<(IWhere<T> where, BooleanType type)>();

            whereList.Add((where, type));

            return where;
        }

        internal WhereGroup<T> AddWhereContainer(WhereGroup<T> where, BooleanType type)
        {
            if (whereList == null)
                whereList = new List<(IWhere<T> where, BooleanType type)>();

            whereList.Add((where, type));

            return where;
        }

        public Where<T> And(string columnName)
        {
            return AddWhere(new Where<T>(this, columnName), BooleanType.And);
        }

        public WhereGroup<T> And(Func<Func<string, Where<T>>, WhereGroup<T>> func)
        {
            var container = AddWhereContainer(new WhereGroup<T>(this.Query), BooleanType.And);

            var where = new Where<T>(container);
            container.AddWhere(where, BooleanType.And);
            func(columnName => where.AddKey(columnName));

            return this;
        }

        public Where<T> Or(string columnName)
        {
            return AddWhere(new Where<T>(this, columnName), BooleanType.Or);
        }

        public WhereGroup<T> Or(Func<Func<string, Where<T>>, WhereGroup<T>> func)
        {
            var container = AddWhereContainer(new WhereGroup<T>(this.Query), BooleanType.Or);

            var where = new Where<T>(container);
            container.AddWhere(where, BooleanType.And);
            func(columnName => where.AddKey(columnName));

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

        public SqlQuery<T> OrderBy(string columnName, bool ascending = true)
        {
            return Query.OrderBy(columnName, ascending);
        }

        public SqlQuery<T> OrderBy(Column column, bool ascending = true)
        {
            return Query.OrderBy(column, ascending);
        }
    }
}
