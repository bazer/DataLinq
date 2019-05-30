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
using Slim.Metadata;
using Slim.Mutation;

namespace Slim.Query
{
    public class Select : Query<Select>
    {
        protected List<Join<Select>> joinList = new List<Join<Select>>();
        protected List<Column> columnsToSelect;

        public Select(Transaction database, Table table)
            : base(database, table)
        {
        }

        public override Sql ToSql(string paramPrefix)
        {
            var columns = (columnsToSelect ?? Table.Columns).Select(x => x.DbName).ToJoinedString(", ");

            var sql = new Sql().AddFormat("SELECT {0} FROM {1} \r\n", columns, Table.DbName);
            GetJoins(sql, "");
            GetWhere(sql, paramPrefix);

            return sql;
        }

        protected Sql GetJoins(Sql sql, string tableAlias)
        {
            foreach (var join in joinList)
                join.GetSql(sql, tableAlias);

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

        public override int ParameterCount
        {
            get { return whereList.Count; }
        }

        public IEnumerable<RowData> ReadInstances()
        {
            return Transaction
                .DatabaseTransaction
                .ReadReader(Transaction.DatabaseProvider.ToDbCommand(this))
                .Select(x => new RowData(x, Table));
        }

        public IEnumerable<PrimaryKeys> ReadKeys()
        {
            return Transaction
                .DatabaseTransaction
                .ReadReader(Transaction.DatabaseProvider.ToDbCommand(this))
                .Select(x => new PrimaryKeys(x, Table));
        }

        public Select What(IEnumerable<Column> columns)
        {
            columnsToSelect = columns.ToList();

            return this;
        }
    }
}