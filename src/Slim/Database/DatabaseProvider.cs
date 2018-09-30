using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using System.Configuration;
using Modl.Db.Query;
using Slim.Interfaces;
using Slim.Metadata;
using System.Data.Common;
using Slim.Instances;

namespace Slim
{
    //public enum DatabaseType
    //{
    //    SqlServer,
    //    SqlCE,
    //    MySQL
    //}

    //public interface IDatabaseProvider
    //{
    //    //IDbCommand ToDbCommand(IQuery query);
    //}

    public abstract class DatabaseProvider<T> : DatabaseProvider
        where T : class, IDatabaseModel
    {
        public T Schema { get; }

        public Transaction<T> StartTransaction()
        {
            return new Transaction<T>(this);
        }

        protected DatabaseProvider(string connectionString) : base(connectionString, typeof(T))
        {
            Schema = GetDatabaseInstance();
        }

        protected DatabaseProvider(string connectionString, string databaseName) : base(connectionString, typeof(T), databaseName)
        {
            Schema = GetDatabaseInstance();
        }

        private T GetDatabaseInstance()
        {
            return InstanceFactory.NewDatabase<T>(this);
        }
    }

    public abstract class DatabaseProvider
    {
        //public readonly DatabaseType Type;
        public readonly string Name;
        public readonly string ConnectionString;
        public Database Database { get; }

        protected string[] ProviderNames { get; set; }
        protected IDbConnection activeConnection;

        protected DatabaseProvider(string connectionString, Type databaseType = null, string name = null)
        {
            Name = name;
            ConnectionString = connectionString;

            if (databaseType != null)
            {
                Database = MetadataFromInterfaceFactory.ParseDatabase(databaseType);
                Database.DatabaseProvider = this;
            }

            if (Name == null && Database != null)
                Name = Database.Name;
        }

        //internal abstract IDbConnection GetConnection();
        public abstract IDbCommand ToDbCommand(IQuery query);
        //internal abstract List<IDbCommand> ToDbCommands(List<IQuery> queries);
        public abstract IQuery GetLastIdQuery();
        public abstract Sql GetParameter(Sql sql, string key, object value);
        public abstract Sql GetParameterValue(Sql sql, string key);
        public abstract Sql GetParameterComparison(Sql sql, string field, Modl.Db.Query.Relation relation, string key);

        public abstract DbDataReader ExecuteReader(IDbCommand command);
        public abstract DbDataReader ExecuteReader(string query);
        public abstract int ExecuteNonQuery(IDbCommand command);
        public abstract int ExecuteNonQuery(string query);

        public IEnumerable<DbDataReader> ReadReader(IDbCommand command)
        {
            using (var reader = ExecuteReader(command))
            {
                while (reader.Read())
                    yield return reader;
            }
        }

        public IEnumerable<DbDataReader> ReadReader(string query)
        {
            using (var reader = ExecuteReader(query))
            {
                while (reader.Read())
                    yield return reader;
            }
        }

        //internal static List<IDbCommand> GetDbCommands(List<IQuery> queries)
        //{
        //    return queries.GroupBy(x => x.DatabaseProvider).SelectMany(x => x.Key.ToDbCommands(x.ToList())).ToList();
        //}

        //internal static DatabaseProvider GetNewDatabaseProvider<T>(string databaseName, string connectionString, DatabaseType providerType) where T : IDatabaseModel
        //{
        //    string providerName = null;

        //    //if (SqlServerProvider.Type == providerType)
        //    //    providerName = SqlServerProvider.ProviderNames[0];
        //    //else if (SqlCeProvider.Type == providerType)
        //    //    providerName = SqlCeProvider.ProviderNames[0];
        //    if (MySQLProvider.Type == providerType)
        //        providerName = MySQLProvider.ProviderNames[0];

        //    var database = MetadataFromInterfaceFactory.ParseDatabase(typeof(T));

        //    return new MySQLProvider(databaseName, connectionString, database);

        //    //return GetNewDatabaseProvider(new ConnectionStringSettings(databaseName, connectionString, providerName));
        //}

        //internal static Database GetNewDatabaseProvider(ConnectionStringSettings connectionConfig)
        //{
        //    Database provider = SqlServerProvider.GetNewOnMatch(connectionConfig);
        //    provider = provider ?? SqlCeProvider.GetNewOnMatch(connectionConfig);
        //    provider = provider ?? MySQLProvider.GetNewOnMatch(connectionConfig);

        //    if (provider == null)
        //        throw new Exception(string.Format("Found no DatabaseProvider matching \"{0}\"", connectionConfig.ProviderName));

        //    return provider;
        ////}

        //public static DatabaseProvider Default
        //{
        //    get
        //    {
        //        return Config.DefaultDatabase;
        //    }
        //    set
        //    {
        //        Config.DefaultDatabase = value;
        //    }
        //}

        ////internal static Database AddFromConnectionString(ConnectionStringSettings connectionConfig)
        ////{
        ////    return Config.AddDatabase(Database.GetNewDatabaseProvider(connectionConfig));
        ////}

        //public static DatabaseProvider Add(DatabaseProvider database)
        //{
        //    return Config.AddDatabase(database);
        //}

        //public static Database Add(string databaseName)
        //{
        //    return AddFromConnectionString(ConfigurationManager.ConnectionStrings[databaseName]);
        //}

        //public static Database Add(string databaseName, string connectionString, string providerName)
        //{
        //    return Config.AddDatabase(Database.GetNewDatabaseProvider(new ConnectionStringSettings(databaseName, connectionString, providerName)));
        //}

        //public static DatabaseProvider Add(string databaseName, string connectionString, DatabaseType providerType)
        //{
        //    return Config.AddDatabase(DatabaseProvider.GetNewDatabaseProvider(databaseName, connectionString, providerType));
        //}

        //public static DatabaseProvider Get(string databaseName)
        //{
        //    return Config.GetDatabase(databaseName);
        //}

        //public static List<DatabaseProvider> GetAll()
        //{
        //    return Config.GetAllDatabases();
        //}

        //public static void Remove(string databaseName)
        //{
        //    Config.RemoveDatabase(databaseName);
        //}

        //public static void RemoveAll()
        //{
        //    Config.RemoveAllDatabases();
        //}

        //public M New<M, IdType>() where M : Modl<M, IdType>, new()
        //{
        //    return Modl<M, IdType>.New(this);
        //}

        //public M Get<M, IdType>(IdType id, bool throwExceptionOnNotFound = true) where M : Modl<M, IdType>, new()
        //{
        //    return Modl<M, IdType>.Get(id, this, throwExceptionOnNotFound);
        //}

        //public bool Exists<M, IdType>(IdType id) where M : Modl<M, IdType>, new()
        //{
        //    return Modl<M, IdType>.Exists(id, this);
        //}

        //public IQueryable<M> Query<M, IdType>() where M : Modl<M, IdType>, new()
        //{
        //    return Modl<M, IdType>.Query(this);
        //    //return new LinqQuery<M, IdType>(this);
        //}

        //public IQueryable<M> Query<M>() where M : Modl<M>, new()
        //{
        //    return Modl<M>.Query(this);
        //    //return new LinqQuery<M, IdType>(this);
        //}

        //IQueryable<T> IQueryProvider.CreateQuery<T>(System.Linq.Expressions.Expression expression)
        //{
        //    return new LinqQuery<T>(this, expression);
        //}

        //IQueryable IQueryProvider.CreateQuery(System.Linq.Expressions.Expression expression)
        //{
        //    throw new NotImplementedException();
        //}

        //T IQueryProvider.Execute<T>(System.Linq.Expressions.Expression expression)
        //{
        //    //var select = new Select<T>(this, expression);

        //    return (T)this.Execute(expression);
        //}

        //object IQueryProvider.Execute(System.Linq.Expressions.Expression expression)
        //{
        //    //return this.Execute(expression);
        //    throw new NotImplementedException();
        //}

        //object Execute(Expression expression)
        //{
        //    //Type elementType = TypeSystem.GetElementType(expression.Type);

        //    //var method = expression.Type.GetMethods(BindingFlags.Static | BindingFlags.FlattenHierarchy | BindingFlags.Public).Single(x => x.Name == "Get" && x.GetParameters().Count() == 2);
        //    //return method.Invoke(null, new object[] { Convert.ToInt32(value.AttemptedValue), true });

        //    //return Activator.CreateInstance(typeof(Modl<>).MakeGenericType(expression.Type), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { reader }, null);
        //}

        //public static void DisposeAll()
        //{
        //    AsyncDbAccess.DisposeAllWorkers();
        //    CacheConfig.Clear();
        //}

        //public void Dispose()
        //{
        //    AsyncDbAccess.DisposeWorker(this);
        //}
    }
}
