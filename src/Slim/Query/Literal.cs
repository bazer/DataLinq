using System.Collections.Generic;
using System.Data;
using System.Linq;
using Slim.Mutation;

namespace Slim.Query
{
    public class Literal : IQuery
    {
        private readonly string sql;
        private readonly IEnumerable<IDataParameter> parameters;
        protected Transaction transaction;
        public Transaction Transaction => transaction;

        public Literal(Transaction transaction, string sql)
        {
            this.transaction = transaction;
            this.sql = sql;
            this.parameters = new List<IDataParameter>();
        }

        public Literal(Transaction database, string sql, IEnumerable<IDataParameter> parameters)
        {
            this.transaction = database;
            this.sql = sql;
            this.parameters = parameters;
        }

        public Sql ToSql(string paramPrefix)
        {
            return new Sql(sql, parameters.ToArray());
        }

        public IDbCommand ToDbCommand()
        {
            return Transaction.Provider.ToDbCommand(this);
        }

        public int ParameterCount
        {
            get { return parameters.Count(); }
        }
    }
}