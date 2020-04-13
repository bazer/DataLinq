using System;
using System.Data;
using System.Linq.Expressions;
using Slim.Metadata;
using Slim.Mutation;

namespace Slim.Query
{
    public class Delete<T> : IQuery
    {
        private readonly SqlQuery<T> query;

        public Delete(SqlQuery<T> query)
        {
            this.query = query;
        }

        public IDbCommand ToDbCommand()
        {
            throw new System.NotImplementedException();
        }

        public Sql ToSql(string paramPrefix = null)
        {
            return query.GetWhere(
                new Sql().AddFormat("DELETE FROM {0} \r\n", query.Table.DbName),
                paramPrefix);
        }

        public QueryResult Execute()
        {
            throw new NotImplementedException();
        }
    }
}