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
    public class DbRead<T> : QueryableBase<T>
        where T : class, IModel
    {
        private DatabaseProvider DatabaseProvider { get; }
        private Table Table { get; }

        public DbRead(IQueryParser queryParser, IQueryExecutor executor)
            : base(new DefaultQueryProvider(typeof(DbRead<>), queryParser, executor))
        {
        }

        public DbRead(IQueryProvider provider, Expression expression)
            : base(provider, expression)
        {
        }

        public DbRead(DatabaseProvider databaseProvider) : base(QueryParser.CreateDefault(), new SlimQueryExecutor())
        {
            this.DatabaseProvider = databaseProvider;
            this.Table = databaseProvider.Database.Tables.Single(x => x.CsType == typeof(T));
        }

        //internal ModelReader(string name, IModl m) : base(name, m)
        //{
        ////}

        //public IEnumerator<T> GetEnumerator() => GetCollection().GetEnumerator();

        //IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        //private List<T> GetCollection()
        //{
        //    throw new NotImplementedException();
        //}

        //public IEnumerable<IModel> Sql(MySqlCommand sql)
        //{
        //    throw new NotImplementedException();
        //}

        ////public IModel New()
        ////{
        ////    throw new NotImplementedException();
        ////}

        public T Get(params object[] id)
        {
            return new Select(DatabaseProvider, Table)
                .Where(Table.Columns.First(x => x.PrimaryKey).Name).EqualTo(id[0])
                .ReadInstances()
                .Select(InstanceFactory.NewImmutableRow<T>)
                .SingleOrDefault();
            
            //DbAccess.ExecuteReader(query)

            //    .Select(x )
            //throw new NotImplementedException();
        }
    }
}