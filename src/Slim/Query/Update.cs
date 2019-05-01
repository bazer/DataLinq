using Slim;
using Slim.Metadata;

namespace Modl.Db.Query
{
    public class Update : Change
    //where M : IDbModl, new()
    {
        public Update(Transaction database, Table table) : base(database, table)
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
                DatabaseProvider.DatabaseProvider.GetParameter(sql, paramPrefix + "v" + i, with.Value);
                DatabaseProvider.DatabaseProvider.GetParameterComparison(sql, with.Key, Relation.Equal, paramPrefix + "v" + i);

                if (i + 1 < length)
                    sql.AddText(",");

                i++;
            }

            return sql;

            //int i = 0, j = 0;

            //return sql
            //    .Join(",", withList.Select(x => DatabaseProvider.GetParameterComparison(x.Key, Relation.Equal, paramPrefix + "v" + i++)).ToArray())
            //    .AddParameters(withList.Select(x => DatabaseProvider.GetParameter(paramPrefix + "v" + j++, x.Value)).ToArray());

            //return new Sql(
            //    string.Join(",", withList.Select(x => DatabaseProvider.GetParameterComparison(x.Key, Relation.Equal, paramPrefix + "v" + i++))),
            //    withList.Select(x => DatabaseProvider.GetParameter(paramPrefix + "v" + j++, x.Value)).ToArray());
        }

        public override Sql ToSql(string paramPrefix)
        {
            var sql = GetWith(
                new Sql().AddFormat("UPDATE {0} SET ", Table.DbName),
                paramPrefix);

            return GetWhere(
                sql.AddText(" \r\n"),
                paramPrefix);

            //var with = GetWith(paramPrefix);
            //var where = GetWhere(paramPrefix);

            //return new Sql(
            //    string.Format("UPDATE {0} SET {1} \r\n{2}", Modl<M, IdType>.Table, with.Text, where.Text),
            //    with.Parameters.Concat(where.Parameters).ToArray());
        }

        public override int ParameterCount
        {
            get { return withList.Count + whereList.Count; }
        }
    }
}