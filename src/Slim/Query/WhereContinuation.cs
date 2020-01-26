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

    public class WhereContinuation<Q> : IQuery
        where Q : Query<Q>
    {
        private readonly Q query;
        protected List<(Where<Q> where, ContinuationType type)> whereList;

        public Transaction Transaction => throw new NotImplementedException();

        internal WhereContinuation(Q query)
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

        protected Where<Q> AddWhere(Where<Q> where, ContinuationType type)
        {
            if (whereList == null)
                whereList = new List<(Where<Q> where, ContinuationType type)>();

            whereList.Add((where, type));

            return where;
        }

        public Where<Q> And(string columnName)
        {
            return AddWhere(new Where<Q>(query, columnName), ContinuationType.And);
        }

        public Where<Q> Or(string columnName)
        {
            return AddWhere(new Where<Q>(query, columnName), ContinuationType.Or);
        }

        public Q Query()
        {
            return query;
        }

        public IDbCommand ToDbCommand()
        {
            return query.ToDbCommand();
        }

        public Sql ToSql(string paramPrefix = null)
        {
            return query.ToSql(paramPrefix);
        }
    }
}
