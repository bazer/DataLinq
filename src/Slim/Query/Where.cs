using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using Slim.Extensions;
using Remotion.Linq.Clauses;
using System.Linq.Expressions;

namespace Slim.Query
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
        where Q : Query<Q>
    {
        private readonly Q Query;
        private readonly string Key;
        private object Value;
        private Relation Relation;
        private bool IsValue = true;
        protected List<WhereContinuation<Q>> continuationList = new List<WhereContinuation<Q>>();

        internal Where(Q query, string key, bool isValue = true)
        {
            this.Query = query;
            this.Key = key;
            this.IsValue = isValue;
        }

        public WhereContinuation<Q> EqualTo<V>(V value)
        {
#pragma warning disable RCS1165 // Unconstrained type parameter checked for null.
            return SetAndReturn(value, value == null ? Relation.EqualNull : Relation.Equal);
#pragma warning restore RCS1165 // Unconstrained type parameter checked for null.
        }

        public WhereContinuation<Q> EqualTo<V>(V value, bool isValue)
        {
            this.IsValue = isValue;
#pragma warning disable RCS1165 // Unconstrained type parameter checked for null.
            return SetAndReturn(value, value == null ? Relation.EqualNull : Relation.Equal);
#pragma warning restore RCS1165 // Unconstrained type parameter checked for null.
        }

        public WhereContinuation<Q> NotEqualTo<V>(V value)
        {
#pragma warning disable RCS1165 // Unconstrained type parameter checked for null.
            return SetAndReturn(value, value == null ? Relation.NotEqualNull : Relation.NotEqual);
#pragma warning restore RCS1165 // Unconstrained type parameter checked for null.
        }

        public WhereContinuation<Q> Like<V>(V value)
        {
            return SetAndReturn(value, Relation.Like);
        }

        public WhereContinuation<Q> GreaterThan<V>(V value)
        {
            return SetAndReturn(value, Relation.BiggerThan);
        }

        public WhereContinuation<Q> GreaterThanOrEqual<V>(V value)
        {
            return SetAndReturn(value, Relation.BiggerThanOrEqual);
        }

        public WhereContinuation<Q> LessThan<V>(V value)
        {
            return SetAndReturn(value, Relation.SmallerThan);
        }

        public WhereContinuation<Q> LessThanOrEqual<V>(V value)
        {
            return SetAndReturn(value, Relation.SmallerThanOrEqual);
        }

        private WhereContinuation<Q> SetAndReturn<V>(V value, Relation relation)
        {
            this.Value = value;
            this.Relation = relation;

            var continuation = new WhereContinuation<Q>(Query);
            this.continuationList.Add(continuation);

            return continuation;
        }

        public override void GetCommandString(Sql sql, string prefix, bool addCommandParameter = true)
        {
            if (addCommandParameter)
                GetCommandParameter(sql, prefix);

            if (IsValue)
                Query.Transaction.DatabaseProvider.GetParameterComparison(sql, Key, Relation, prefix + "w" + sql.IndexAdd());
            else
                sql.AddFormat("{0} {1} {2}", Key, Relation.ToSql(), Value.ToString());

            foreach (var continuation in continuationList)
                continuation.GetWhere(sql, prefix);
        }

        protected void GetCommandParameter(Sql sql, string prefix)
        {
            if (IsValue)
                Query.Transaction.DatabaseProvider.GetParameter(sql, prefix + "w" + sql.Index, Value);
            
        }
    }
}
