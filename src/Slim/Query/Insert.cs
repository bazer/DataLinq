﻿using Slim.Metadata;
using Slim.Mutation;

namespace Slim.Query
{
    public class Insert : Change
    {
        public Insert(Table table, Transaction transaction) : base(table, transaction)
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
                Transaction.Provider.GetParameter(sql, paramPrefix + "v" + i, with.Value);
                Transaction.Provider.GetParameterValue(sql, paramPrefix + "v" + i);

                if (i + 1 < length)
                    sql.AddText(",");
                else
                    sql.AddText(")");

                i++;
            }

            return sql;
        }

        public override Sql ToSql(string paramPrefix)
        {
            return GetWith(
                new Sql().AddFormat("INSERT INTO {0} ", Table.DbName),
                paramPrefix);
        }

        //public override int ParameterCount
        //{
        //    get { return withList.Count; }
        //}
    }
}