using Slim;
using Slim.Metadata;

namespace Modl.Db.Query
{
    public class Insert : Change
    //where M : IDbModl, new()
    {
        public Insert(DatabaseProvider database, Table table) : base(database, table)
        {
        }

        protected Sql GetWith(Sql sql, string paramPrefix)
        {
            int length = withList.Count;
            if (length == 0)
                return sql.AddFormat("VALUES (NULL)");

            sql.AddFormat("({0}) VALUES (", string.Join(",", withList.Keys));

            int i = 0;
            foreach (var with in withList)
            {
                DatabaseProvider.GetParameter(sql, paramPrefix + "v" + i, with.Value);
                DatabaseProvider.GetParameterValue(sql, paramPrefix + "v" + i);

                if (i + 1 < length)
                    sql.AddText(",");
                else
                    sql.AddText(")");

                i++;
            }

            return sql;

            //int i = 0, j = 0;

            //return sql
            //    .AddFormat("({0}) VALUES ({1})",
            //        string.Join(",", withList.Keys),
            //        string.Join(",", withList.Values.Select(x => DatabaseProvider.GetParameterValue(paramPrefix + "v" + i++))))
            //    .AddParameters(withList.Select(x => DatabaseProvider.GetParameter(paramPrefix + "v" + j++, x.Value)).ToArray());

            //return new Sql(
            //    string.Format("({0}) VALUES ({1})",
            //        string.Join(",", withList.Keys),
            //        string.Join(",", withList.Values.Select(x => DatabaseProvider.GetParameterValue(paramPrefix + "v" + i++)))),
            //    withList.Select(x => DatabaseProvider.GetParameter(paramPrefix + "v" + j++, x.Value)).ToArray());

            //return string.Format("({0}) VALUES ({1})",
            //    string.Join(",", withList.Keys),
            //    string.Join(",", withList.Values.Select(x => "'" + x + "'"))
            //);
        }

        public override Sql ToSql(string paramPrefix)
        {
            return GetWith(
                new Sql().AddFormat("INSERT INTO {0} ", Table.DbName),
                paramPrefix);

            //var with = GetWith(paramPrefix);

            //return new Sql(
            //    string.Format("INSERT INTO {0} {1}", Modl<M, IdType>.Table, with.Text),
            //    with.Parameters);
        }

        public override int ParameterCount
        {
            get { return withList.Count; }
        }

        //public override string ToString()
        //{
        //    return string.Format("INSERT INTO {0} {1}", Modl<C>.TableName, ValuesToString());
        //}
    }
}