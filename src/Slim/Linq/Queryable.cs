using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using Modl.Db.Query;
using Remotion.Linq;
using Remotion.Linq.Parsing.Structure;
using Slim.Instances;
using Slim.Interfaces;
using Slim.Linq;
using Slim.Metadata;

namespace Slim
{
    public class Queryable<T> : QueryableBase<T>
        where T : class, IModel
    {
        protected DatabaseProvider DatabaseProvider { get; }
        protected Table Table { get; }

        //public Queryable(IQueryParser queryParser, IQueryExecutor executor)
        //    : base(new DefaultQueryProvider(typeof(Queryable<>), queryParser, executor))
        //{
        //}

        //public Queryable(IQueryProvider provider, Expression expression)
        //    : base(provider, expression)
        //{
        //}

        public Queryable(DatabaseProvider databaseProvider, Table table) : base(QueryParser.CreateDefault(), new QueryExecutor(databaseProvider, table))
        {
            this.DatabaseProvider = databaseProvider;
            this.Table = table;

            //this.DatabaseProvider = databaseProvider;
            //this.Table = databaseProvider.Database.Tables.Single(x => x.CsType == typeof(T));
        }

     
    }
}