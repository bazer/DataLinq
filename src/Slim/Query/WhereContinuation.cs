using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using Slim.Extensions;
using Remotion.Linq.Clauses;
using System.Linq.Expressions;
using Slim.Mutation;

namespace Slim.Query
{
    public enum ContinuationType
    {
        And,
        Or
    }

    public class WhereContinuation<T>
    {
        private readonly SqlQuery<T> query;
        protected List<(Where<T> where, ContinuationType type)> whereList;

        public Transaction Transaction => throw new NotImplementedException();

        internal WhereContinuation(SqlQuery<T> query)
        {
            this.query = query;

        }

        internal Sql GetWhere(Sql sql, string paramPrefix, bool addCommandParameter = true)
        {
            int length = whereList?.Count ?? 0;
            if (length == 0)
                return sql;

            for (int i = 0; i < length; i++)
            {
                if (whereList[i].type == ContinuationType.And)
                    sql.AddText(" AND ");
                else if (whereList[i].type == ContinuationType.Or)
                    sql.AddText(" OR ");
                else
                    throw new NotImplementedException();

                whereList[i].where.GetCommandString(sql, paramPrefix, addCommandParameter);
            }

            return sql;
        }

        protected Where<T> AddWhere(Where<T> where, ContinuationType type)
        {
            if (whereList == null)
                whereList = new List<(Where<T> where, ContinuationType type)>();

            whereList.Add((where, type));

            return where;
        }

        public Where<T> And(string columnName)
        {
            return AddWhere(new Where<T>(query, columnName), ContinuationType.And);
        }

        public Where<T> Or(string columnName)
        {
            return AddWhere(new Where<T>(query, columnName), ContinuationType.Or);
        }

        public SqlQuery<T> Set<V>(string key, V value)
        {
            return query.Set(key, value);
        }

        public SqlQuery<T> Query()
        {
            return query;
        }

        public IEnumerable<T> Select()
        {
            return query.Select();
        }

        public QueryResult Delete()
        {
            return query.Delete();
        }

        public QueryResult Insert()
        {
            return query.Insert();
        }

        public QueryResult Update()
        {
            return query.Update();
        }

        public Select<T> SelectQuery()
        {
            return new Select<T>(query);
        }
    }
}
