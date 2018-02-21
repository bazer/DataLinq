using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Data.Common;
using System.Threading.Tasks;
using Slim.Metadata;
using Slim.MySql;

namespace Modl.Db.Query
{
    public class Select : Query<Select>
        //where M : IDbModl, new()
    {
        Expression expression;
        protected List<Join<Select>> joinList = new List<Join<Select>>();

        public Select(DatabaseProvider database, Table table)
            : base(database, table)
        {
            expression = Expression.Constant(this);
        }

        public Select(DatabaseProvider database, Table table, Expression expression)
            : base(database, table)
        {
            this.expression = expression;
            //var parser = new LinqParser<Select>(this);
            //parser.ParseTree(expression);
        }

        public override Sql ToSql(string paramPrefix)
        {
            var sql = new Sql().AddFormat("SELECT * FROM {0} \r\n", table.Name);
            GetJoins(sql, "");
            GetWhere(sql, paramPrefix);

            return sql;

            //return GetWhere(
            //    new Sql().AddFormat("SELECT * FROM {0} \r\n", table.Name),
            //    paramPrefix);
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

        public DbDataReader Execute()
        {
            return DbAccess.ExecuteReader(this).First();
        }

        //public M Get<M>() where M : IDbModl, new()
        //{
        //    return new Materializer<M>(Execute(), DatabaseProvider).ReadAndClose();
        //}

        //public IEnumerable<M> GetAll<M>() where M : IDbModl, new()
        //{
        //    return new Materializer<M>(Execute(), DatabaseProvider).GetAll();
        //}

        //public Materializer<M> GetMaterializer<M>() where M : IDbModl, new()
        //{
        //    return new Materializer<M>(Execute(), DatabaseProvider);
        //}

        //public Task<DbDataReader> ExecuteAsync(bool onQueue = true)
        //{
        //    return AsyncDbAccess.ExecuteReader(this, onQueue);
        //}

        //public Task<M> GetAsync<M>(bool onQueue = true) where M : IDbModl, new()
        //{
        //    return Materializer<M>.Async(ExecuteAsync(onQueue), DatabaseProvider).ContinueWith(x => x.Result.ReadAndClose());
        //}

        //public Task<IEnumerable<M>> GetAllAsync<M>(bool onQueue = true) where M : IDbModl, new()
        //{
        //    return Materializer<M>.Async(ExecuteAsync(onQueue), DatabaseProvider).ContinueWith(x => x.Result.GetAll());
        //}

        //public Task<Materializer<M>> GetMaterializerAsync<M>(bool onQueue = true) where M : IDbModl, new()
        //{
        //    return Materializer<M>.Async(ExecuteAsync(onQueue), DatabaseProvider);
        //}
    }
}
