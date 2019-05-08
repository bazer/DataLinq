using System.Linq.Expressions;
using Slim;
using Slim.Metadata;

namespace Slim.Query
{
    public class Delete : Query<Delete>
    {
        private readonly Expression expression;

        public Delete(Transaction transaction, Table table) : base(transaction, table)
        {
        }

        public Delete(Transaction transaction, Table table, Expression expression)
            : base(transaction, table)
        {
            this.expression = expression;
        }

        public override Sql ToSql(string paramPrefix)
        {
            return GetWhere(
                new Sql().AddFormat("DELETE FROM {0} \r\n", Table.DbName),
                paramPrefix);
        }

        public override int ParameterCount
        {
            get { return whereList.Count; }
        }
    }
}