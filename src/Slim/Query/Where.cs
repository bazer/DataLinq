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
        EqualNull,
        NotEqual,
        NotEqualNull,
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
        private readonly Q Query;
        private readonly string Key;
        private object Value;
        private Relation Relation;
        private bool IsValue = true;

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
#pragma warning disable RCS1165 // Unconstrained type parameter checked for null.
            return SetAndReturn(value, value == null ? Relation.EqualNull : Relation.Equal);
#pragma warning restore RCS1165 // Unconstrained type parameter checked for null.
        }

        public Q EqualTo<V>(V value, bool isValue)
        {
            this.IsValue = isValue;
#pragma warning disable RCS1165 // Unconstrained type parameter checked for null.
            return SetAndReturn(value, value == null ? Relation.EqualNull : Relation.Equal);
#pragma warning restore RCS1165 // Unconstrained type parameter checked for null.
        }

        public Q NotEqualTo<V>(V value)
        {
#pragma warning disable RCS1165 // Unconstrained type parameter checked for null.
            return SetAndReturn(value, value == null ? Relation.NotEqualNull : Relation.NotEqual);
#pragma warning restore RCS1165 // Unconstrained type parameter checked for null.
        }

        public Q Like<V>(V value)
        {
            return SetAndReturn(value, Relation.Like);
        }

        public Q GreaterThan<V>(V value)
        {
            return SetAndReturn(value, Relation.BiggerThan);
        }

        public Q GreaterThanOrEqual<V>(V value)
        {
            return SetAndReturn(value, Relation.BiggerThanOrEqual);
        }

        public Q LessThan<V>(V value)
        {
            return SetAndReturn(value, Relation.SmallerThan);
        }

        public Q LessThanOrEqual<V>(V value)
        {
            return SetAndReturn(value, Relation.SmallerThanOrEqual);
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
                return Query.DatabaseProvider.DatabaseProvider.GetParameterComparison(sql, Key, Relation, prefix + "w" + number);
            else
                return sql.AddFormat("{0} {1} {2}", Key, Relation.ToSql(), Value.ToString());

            //return Query.DatabaseProvider.GetParameterComparison(sql, Key, Relation, Value.ToString());

            //return string.Format("{0} {1} @{2}", Key, RelationToString(Relation), number);
        }

        public override Sql GetCommandParameter(Sql sql, string prefix, int number)
        {
            if (IsValue)
                return Query.DatabaseProvider.DatabaseProvider.GetParameter(sql, prefix + "w" + number, Value);
            else
                return sql;

            //return new Tuple<string, object>("@" + number, Value);
        }
    }
}
