using System.Linq.Expressions;
using Slim.Metadata;
using Slim.Mutation;

namespace Slim.Query
{
    public class Delete : Query<Delete>
    {
        private readonly Expression expression;

        public Delete(Table table, Transaction transaction) : base(table, transaction)
        {
        }

        public Delete(Table table, Transaction transaction, Expression expression)
            : base(table, transaction)
        {
            this.expression = expression;
        }

        public override Sql ToSql(string paramPrefix = null)
        {
            return GetWhere(
                new Sql().AddFormat("DELETE FROM {0} \r\n", Table.DbName),
                paramPrefix);
        }

        //public override int ParameterCount
        //{
        //    get { return whereList.Count; }
        //}
    }
}