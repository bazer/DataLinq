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
    public class DbRead<T> : Queryable<T>
    {
        public DbRead(DatabaseProvider databaseProvider) : base(databaseProvider, databaseProvider.Database.Tables.Single(x => x.CsType == typeof(T)))
        {
        }

        public DbRead(IQueryProvider provider, Expression expression) : base(provider, expression)
        {
        }

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