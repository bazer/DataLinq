using Remotion.Linq.Clauses;
using Slim.Exceptions;
using Slim.Linq.Visitors;
using Slim.Metadata;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;

namespace Slim.Query
{
    public interface IQuery
    {
        Transaction Transaction { get; }

        IDbCommand ToDbCommand();

        Sql ToSql(string paramPrefix);

        int ParameterCount { get; }
    }

    public abstract class Query<Q> : IQuery
        where Q : Query<Q>
    {
        protected List<Where<Q>> whereList = new List<Where<Q>>();

        public Transaction Transaction { get; }

        public abstract Sql ToSql(string paramPrefix);

        public abstract int ParameterCount { get; }
        protected Table Table;

        protected Query(Transaction transaction, Table table)
        {
            this.Transaction = transaction;
            this.Table = table;
        }

        public Where<Q> Where(string key)
        {
            var where = new Where<Q>((Q)this, key);
            whereList.Add(where);

            return where;
        }

        public Q Where(WhereClause where)
        {
            new WhereVisitor<Q>(this).Parse(where);

            return this as Q;
        }

        protected Sql GetWhere(Sql sql, string paramPrefix)
        {
            int length = whereList.Count;
            if (length == 0)
                return sql;

            sql.AddText("WHERE \r\n");

            for (int i = 0; i < length; i++)
            {
                whereList[i].GetCommandParameter(sql, paramPrefix, i);
                whereList[i].GetCommandString(sql, paramPrefix, i);

                if (i + 1 < length)
                    sql.AddText(" AND \r\n");
            }

            return sql;
        }

        public IDbCommand ToDbCommand()
        {
            return Transaction.DatabaseProvider.ToDbCommand(this);
        }

        public override string ToString()
        {
            var sql = ToSql(string.Empty);
            return sql.Text + "; " + string.Join(", ", sql.Parameters.Select(x => x.ParameterName + ": " + x.Value));
        }

        internal KeyValuePair<string, object> GetFields(BinaryExpression node)
        {
            if (node.Left is ConstantExpression && node.Right is ConstantExpression)
                throw new InvalidQueryException("Unable to compare 2 constants.");

            if (node.Left is MemberExpression)
                return GetValues(node.Left, node.Right);
            else
                return GetValues(node.Right, node.Left);
        }

        internal KeyValuePair<string, object> GetValues(Expression field, Expression value)
        {
            return new KeyValuePair<string, object>((string)GetValue(field), GetValue(value));
        }

        internal object GetValue(Expression expression)
        {
            if (expression is ConstantExpression constExp)
                return constExp.Value;
            else if (expression is MemberExpression propExp)
                return Table.Columns.Single(x => x.ValueProperty.CsName == propExp.Member.Name).DbName;
            else
                throw new InvalidQueryException("Value is not a member or constant.");
        }
    }
}