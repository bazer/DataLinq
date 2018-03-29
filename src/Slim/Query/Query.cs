using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using Modl.Db.Linq.Visitors;
using Remotion.Linq.Clauses;
using Slim;
using Slim.Exceptions;
using Slim.Instances;
using Slim.Metadata;

namespace Modl.Db.Query
{
    public interface IQuery
    {
        DatabaseProvider DatabaseProvider { get; }

        IDbCommand ToDbCommand();

        Sql ToSql(string paramPrefix);

        int ParameterCount { get; }
        //Where<C, T> Where(string key);
        //IEnumerable<IDataParameter> QueryPartsParameters();
    }

    public abstract class Query<Q> : IQuery
        //where M : IDbModl, new()
        where Q : Query<Q>
    {
        protected List<Where<Q>> whereList = new List<Where<Q>>();

        //protected IModl owner;
        //protected DatabaseProvider provider;

        public DatabaseProvider DatabaseProvider { get; }

        public abstract Sql ToSql(string paramPrefix);

        public abstract int ParameterCount { get; }
        protected Table Table;

        public Query()
        {
        }

        public Query(DatabaseProvider provider, Table table)
        {
            this.DatabaseProvider = provider;
            this.Table = table;
        }

        //public Query(IModl owner)
        //{
        //    this.owner = owner;
        //}

        public Where<Q> Where(string key)
        {
            var where = new Where<Q>((Q)this, key);
            whereList.Add(where);

            return where;
        }

        public Q Where(WhereClause where)
        {
            new WhereVisitor<Q>(this).Parse(where);

            //whereList.AddRange(Where<Q>.Parse(this as Q, where));

            return this as Q;
        }

        //public Q WhereNotAny(IEnumerable<IModl> collection)
        //{
        //    foreach (var m in collection)
        //        Where(table.IdName).NotEqualTo(m.GetId());

        //    return (Q)this;
        //}

        //public Q WhereNotAny(IEnumerable<object> collection)
        //{
        //    foreach (var id in collection)
        //        Where(table.PrimaryKeyName).NotEqualTo(id);

        //    return (Q)this;
        //}

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
            return DatabaseProvider.ToDbCommand(this);
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
                return Table.Columns.Single(x => x.CsName == propExp.Member.Name).DbName;
            //else if (expression.NodeType == ExpressionType.Lambda)
            //    return GetValue<T>(((LambdaExpression)expression).Body);
            else
                throw new InvalidQueryException("Value is not a member or constant.");
        }
    }
}