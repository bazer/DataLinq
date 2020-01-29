using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using Remotion.Linq.Clauses;
using Slim.Cache;
using Slim.Exceptions;
using Slim.Extensions;
using Slim.Instances;
using Slim.Interfaces;
using Slim.Linq.Visitors;
using Slim.Metadata;
using Slim.Mutation;

namespace Slim.Query
{
    public class OrderBy
    {
        public Column Column { get; }
        public bool Ascending { get; }

        public OrderBy(Column column, bool ascending)
        {
            this.Column = column;
            this.Ascending = ascending;
        }
    }

    public class Select : Query<Select>
    {
        protected List<Join<Select>> joinList = new List<Join<Select>>();
        protected List<Column> columnsToSelect;
        protected List<OrderBy> orderByList = new List<OrderBy>();
        public Select(Table table, Transaction transaction)
            : base(table, transaction)
        {
        }

        public Select(string tableName, Transaction transaction)
            : base(transaction.Provider.Metadata.Tables.Single(x => x.DbName == tableName), transaction)
        {
        }

        public override Sql ToSql(string paramPrefix)
        {
            var columns = (columnsToSelect ?? Table.Columns).Select(x => x.DbName).ToJoinedString(", ");

            var sql = new Sql().AddFormat("SELECT {0} FROM {1} \r\n", columns, Table.DbName);
            GetJoins(sql, "");
            GetWhere(sql, paramPrefix);
            GetOrderBy(sql);

            return sql;
        }

        protected Sql GetJoins(Sql sql, string tableAlias)
        {
            foreach (var join in joinList)
                join.GetSql(sql, tableAlias);

            return sql;
        }

        protected Sql GetOrderBy(Sql sql)
        {
            int length = orderByList.Count;
            if (length == 0)
                return sql;

            sql.AddText("ORDER BY ");
            sql.AddText(string.Join(", ", orderByList.Select(x => $"{x.Column.DbName} {(x.Ascending ? "" : "DESC")}")));

            return sql;
        }

        public Join<Select> InnerJoin(string tableName)
        {
            var join = new Join<Select>(this, tableName, JoinType.Inner);
            joinList.Add(join);

            return join;
        }

        public Join<Select> OuterJoin(string tableName)
        {
            var join = new Join<Select>(this, tableName, JoinType.Outer);
            joinList.Add(join);

            return join;
        }
        public Select OrderBy(string columnName, bool ascending)
        {
            var column = this.Table.Columns.Single(x => x.DbName == columnName);

            return OrderBy(column, ascending);
        }

        public Select OrderBy(Column column, bool ascending)
        {
            if (!this.Table.Columns.Contains(column))
                throw new ArgumentException($"Column '{column.DbName}' does not belong to table '{Table.DbName}'");

            this.orderByList.Add(new OrderBy(column, ascending));

            return this;
        }

        public Select OrderBy(OrderByClause orderBy)
        {
            foreach (var ordering in orderBy.Orderings)
            {
                new OrderByVisitor(this).Parse(ordering);
            }

            return this;
        }

        //public override int ParameterCount
        //{
        //    get { return whereList.Count; }
        //}

        public IEnumerable<RowData> ReadRows()
        {
            return Transaction
                .DbTransaction
                .ReadReader(Transaction.Provider.ToDbCommand(this))
                .Select(x => new RowData(x, Table));
        }

        public IEnumerable<PrimaryKeys> ReadKeys()
        {
            return Transaction
                .DbTransaction
                .ReadReader(Transaction.Provider.ToDbCommand(this))
                .Select(x => new PrimaryKeys(x, Table));
        }

        public Select What(IEnumerable<Column> columns)
        {
            columnsToSelect = columns.ToList();

            return this;
        }

        public IEnumerable<T> Execute<T>()// where T: IModel
        {
            if (Table.PrimaryKeyColumns.Count != 0)
            {
                this.What(Table.PrimaryKeyColumns);

                var keys = this
                    .ReadKeys()
                    .ToArray();

                foreach (var row in Table.Cache.GetRows(keys, Transaction))
                    yield return (T)row;
            }
            else
            {
                var rows = this
                    .ReadRows()
                    .Select(InstanceFactory.NewImmutableRow);

                foreach (var row in rows)
                    yield return (T)row;
            }
        }
    }

    public class Select<T> : Select
            where T : IModel
    {
        public Select(Transaction transaction) : base(transaction.Provider.Metadata.Tables.Single(x => x.Model.CsType == typeof(T)), transaction)
        {

        }

        public Select(Table table, Transaction transaction)
            : base(table, transaction)
        {
        }

        //public override Where<Select<T>> Where(string columnName)
        //{
        //    var where = new Where<Select<T>>(this, columnName);
        //    whereList.Add(where);

        //    return where;
        //}

        public IEnumerable<T> Execute()
        {
            return this.Execute<T>();
        }
    }
}