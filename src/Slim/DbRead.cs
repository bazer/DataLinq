using System.Linq;
using Modl.Db.Query;
using Slim.Instances;
using Slim.Interfaces;
using Slim.Metadata;

namespace Slim
{
    public class DbRead<T> : Queryable<T>
        where T : class, IModel
    {
        public DbRead(DatabaseProvider databaseProvider) : base(databaseProvider, databaseProvider.Database.Tables.Single(x => x.CsType == typeof(T)))
        {
            //this.DatabaseProvider = databaseProvider;
            //this.Table = databaseProvider.Database.Tables.Single(x => x.CsType == typeof(T));
        }

        //private DatabaseProvider DatabaseProvider { get; } 

        //private Table Table { get; }

        public T Get(params object[] id)
        {
            return new Select(DatabaseProvider, Table)
                .Where(Table.Columns.First(x => x.PrimaryKey).Name).EqualTo(id[0])
                .ReadInstances()
                .Select(InstanceFactory.NewImmutableRow<T>)
                .SingleOrDefault();
        }
    }
}