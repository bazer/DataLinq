using System;
using System.Data;
using System.Linq.Expressions;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Query
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
                new Sql().AddFormat("DELETE FROM {0} \n", query.Table.DbName),
                paramPrefix);
        }

        public QueryResult Execute()
        {
            throw new NotImplementedException();
        }
    }
}