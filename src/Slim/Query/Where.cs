using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using Slim.Extensions;
using Remotion.Linq.Clauses;
using System.Linq.Expressions;

namespace Modl.Db.Query
{
    public enum Relation
    {
        Equal,
        NotEqual,
        Like,
        BiggerThan,
        BiggerThanOrEqual,
        SmallerThan,
        SmallerThanOrEqual
    }

    public class Where<Q> : QueryPart
        //where M : IDbModl, new()
        where Q : Query<Q>
    {
        Q Query;
        string Key;
        object Value;
        Relation Relation;
        bool IsValue = true;

        internal Where(Q query, string key, bool isValue = true)
        {
            this.Query = query;
            this.Key = key;
            this.IsValue = isValue;
        }

        //public Where(string key)
        //{
        //    this.Key = key;
        //}



        public Q EqualTo<V>(V value)
        {
            return SetAndReturn(value, Modl.Db.Query.Relation.Equal);
        }

        public Q EqualTo<V>(V value, bool isValue)
        {
            this.IsValue = isValue;
            return SetAndReturn(value, Modl.Db.Query.Relation.Equal);
        }

        public Q NotEqualTo<V>(V value)
        {
            return SetAndReturn(value, Modl.Db.Query.Relation.NotEqual);
        }

        public Q Like<V>(V value)
        {
            return SetAndReturn(value, Modl.Db.Query.Relation.Like);
        }

        public Q GreaterThan<V>(V value)
        {
            return SetAndReturn(value, Modl.Db.Query.Relation.BiggerThan);
        }

        public Q GreaterThanOrEqual<V>(V value)
        {
            return SetAndReturn(value, Modl.Db.Query.Relation.BiggerThanOrEqual);
        }

        public Q LessThan<V>(V value)
        {
            return SetAndReturn(value, Modl.Db.Query.Relation.SmallerThan);
        }

        public Q LessThanOrEqual<V>(V value)
        {
            return SetAndReturn(value, Modl.Db.Query.Relation.SmallerThanOrEqual);
        }

        private Q SetAndReturn<V>(V value, Relation relation)
        {
            this.Value = value;
            this.Relation = relation;

            return Query;
        }

        //public override string ToString()
        //{
        //    return string.Format("{0} {1} '{2}'", Key, RelationToString(Relation), Value.ToString());
        //}

        public override Sql GetCommandString(Sql sql, string prefix, int number)
        {
            if (IsValue)
                return Query.DatabaseProvider.GetParameterComparison(sql, Key, Relation, prefix + "w" + number);
            else
                return sql.AddFormat("{0} {1} {2}", Key, Relation.ToSql(), Value.ToString());


            //return Query.DatabaseProvider.GetParameterComparison(sql, Key, Relation, Value.ToString());

            //return string.Format("{0} {1} @{2}", Key, RelationToString(Relation), number);
        }

        public override Sql GetCommandParameter(Sql sql, string prefix, int number)
        {
            if (IsValue)
                return Query.DatabaseProvider.GetParameter(sql, prefix + "w" + number, Value);
            else
                return sql;

            //return new Tuple<string, object>("@" + number, Value);
        }
    }
}
