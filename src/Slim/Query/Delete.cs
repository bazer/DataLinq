using System.Linq.Expressions;
using Slim;
using Slim.Metadata;

namespace Modl.Db.Query
{
    public class Delete : Query<Delete>
    //where M : IDbModl, new()
    {
        private Expression expression;

        public Delete(DatabaseProvider database, Table table) : base(database, table)
        {
        }

        public Delete(DatabaseProvider database, Table table, Expression expression)
            : base(database, table)
        {
            this.expression = expression;
            //var parser = new LinqParser<Delete>(this);
            //parser.ParseTree(expression);
        }

        public override Sql ToSql(string paramPrefix)
        {
            //var sql = new Sql().AddFormat("DELETE FROM {0} \r\n", Modl<M, IdType>.Table);

            return GetWhere(
                new Sql().AddFormat("DELETE FROM {0} \r\n", Table.DbName),
                paramPrefix);

            //var where = GetWhere(sql, paramPrefix);

            //return where.

            //return new Sql(
            //    string.Format("DELETE FROM {0} \r\n{1}", Modl<M, IdType>.Table, where.Text),
            //    where.Parameters);
        }

        public override int ParameterCount
        {
            get { return whereList.Count; }
        }

        //public override string ToString()
        //{
        //    return string.Format("DELETE FROM {0} \r\nWHERE \r\n{1}", Modl<C>.TableName, QueryPartsToString());
        //}
    }
}