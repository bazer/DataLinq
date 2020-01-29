using Slim.Metadata;
using Slim.Mutation;

namespace Slim.Query
{
    public class Update : Change
    {
        public Update(Table table, Transaction transaction) : base(table, transaction)
        {
        }

        protected Sql GetWith(Sql sql, string paramPrefix)
        {
            int length = withList.Count;
            if (length == 0)
                return sql;

            int i = 0;
            foreach (var with in withList)
            {
                Transaction.Provider.GetParameter(sql, paramPrefix + "v" + i, with.Value);
                Transaction.Provider.GetParameterComparison(sql, with.Key, Relation.Equal, paramPrefix + "v" + i);

                if (i + 1 < length)
                    sql.AddText(",");

                i++;
            }

            return sql;
        }

        public override Sql ToSql(string paramPrefix)
        {
            var sql = GetWith(
                new Sql().AddFormat("UPDATE {0} SET ", Table.DbName),
                paramPrefix);

            return GetWhere(
                sql.AddText(" \r\n"),
                paramPrefix);
        }

        //public override int ParameterCount
        //{
        //    get { return withList.Count + whereList.Count; }
        //}
    }
}