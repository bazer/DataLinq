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

    public interface IWhere<T>: IQueryPart
    {
        //IWhere<T> And(string columnName);

        //IWhere<T> Or(string columnName);
        //WhereContinuation<T> EqualTo<V>(V value);
        //WhereContinuation<T> EqualTo<V>(V value, bool isValue);
        //WhereContinuation<T> NotEqualTo<V>(V value);
        //WhereContinuation<T> Like<V>(V value);
        //WhereContinuation<T> GreaterThan<V>(V value);
        //WhereContinuation<T> GreaterThanOrEqual<V>(V value);
        //WhereContinuation<T> LessThan<V>(V value);
        //WhereContinuation<T> LessThanOrEqual<V>(V value);
    }

    public class Where<T> : IWhere<T>
    {
        private string Key;
        private object Value;
        private Relation Relation;
        private bool IsValue = true;
        protected WhereGroup<T> Container;

        internal Where(WhereGroup<T> container, string key, bool isValue = true)
        {
            this.Container = container;
            this.Key = key;
            this.IsValue = isValue;
        }

        internal Where(WhereGroup<T> container)
        {
            this.Container = container;
        }

        internal Where<T> AddKey(string key, bool isValue = true)
        {
            this.Key = key;
            this.IsValue = isValue;

            return this;
        }

        public WhereGroup<T> EqualTo<V>(V value)
        {
#pragma warning disable RCS1165 // Unconstrained type parameter checked for null.
            return SetAndReturn(value, value == null ? Relation.EqualNull : Relation.Equal);
#pragma warning restore RCS1165 // Unconstrained type parameter checked for null.
        }

        public WhereGroup<T> EqualTo<V>(V value, bool isValue)
        {
            this.IsValue = isValue;
#pragma warning disable RCS1165 // Unconstrained type parameter checked for null.
            return SetAndReturn(value, value == null ? Relation.EqualNull : Relation.Equal);
#pragma warning restore RCS1165 // Unconstrained type parameter checked for null.
        }

        public WhereGroup<T> NotEqualTo<V>(V value)
        {
#pragma warning disable RCS1165 // Unconstrained type parameter checked for null.
            return SetAndReturn(value, value == null ? Relation.NotEqualNull : Relation.NotEqual);
#pragma warning restore RCS1165 // Unconstrained type parameter checked for null.
        }

        public WhereGroup<T> Like<V>(V value)
        {
            return SetAndReturn(value, Relation.Like);
        }

        public WhereGroup<T> GreaterThan<V>(V value)
        {
            return SetAndReturn(value, Relation.BiggerThan);
        }

        public WhereGroup<T> GreaterThanOrEqual<V>(V value)
        {
            return SetAndReturn(value, Relation.BiggerThanOrEqual);
        }

        public WhereGroup<T> LessThan<V>(V value)
        {
            return SetAndReturn(value, Relation.SmallerThan);
        }

        public WhereGroup<T> LessThanOrEqual<V>(V value)
        {
            return SetAndReturn(value, Relation.SmallerThanOrEqual);
        }

        protected WhereGroup<T> SetAndReturn<V>(V value, Relation relation)
        {
            this.Value = value;
            this.Relation = relation;

            return this.Container;
        }

        public void AddCommandString(Sql sql, string prefix, bool addCommandParameter = true, bool addParentheses = false)
        {
            if (addCommandParameter)
                GetCommandParameter(sql, prefix);

            if (addParentheses)
                sql.AddText("(");

            if (IsValue)
                Container.Query.Transaction.Provider.GetParameterComparison(sql, Key, Relation, prefix + "w" + sql.IndexAdd());
            else
                sql.AddFormat("{0} {1} {2}", Key, Relation.ToSql(), Value.ToString());

            if (addParentheses)
                sql.AddText(")");
        }

        protected void GetCommandParameter(Sql sql, string prefix)
        {
            if (IsValue)
                Container.Query.Transaction.Provider.GetParameter(sql, prefix + "w" + sql.Index, Value);

        }
    }
}
