using System.Collections.Generic;
using System.Linq;

namespace Slim
{
    //public enum CacheLevel
    //{
    //    On,
    //    Off,
    //    All
    //}

    //public enum Timeout
    //{
    //    Never = 0,
    //    TenMinutes = 10,
    //    TwentyMinutes = 20,
    //    ThirtyMinutes = 30,
    //    OneHour = 60,
    //    OneDay = 1440
    //}

    public class Config
    {
        //public static CacheLevel DefaultCacheLevel { get { return CacheConfig.DefaultCacheLevel; } set { CacheConfig.DefaultCacheLevel = value; } }
        //public static int DefaultCacheTimeout { get { return CacheConfig.DefaultCacheTimeout; } set { CacheConfig.DefaultCacheTimeout = value; } }

        //private static CacheLevel cacheLevel;
        //public static CacheLevel CacheLevel
        //{
        //    get
        //    {
        //        return cacheLevel;
        //    }
        //    set
        //    {
        //        cacheLevel = value;

        //        if (cacheLevel == Modl.CacheLevel.Off)
        //        {
        //            AsyncDbAccess.DisposeAllWorkers();
        //            CacheManager.Clear();
        //        }
        //    }
        //}

        protected static Dictionary<string, DatabaseProvider> DatabaseProviders = new Dictionary<string, DatabaseProvider>();

        static Config()
        {
            //DefaultCacheLevel = CacheLevel.On;
            //DefaultCacheTimeout = 20;

            //foreach (ConnectionStringSettings connString in ConfigurationManager.ConnectionStrings)
            //    if (!string.IsNullOrWhiteSpace(connString.ConnectionString) && !string.IsNullOrWhiteSpace(connString.Name) && !string.IsNullOrWhiteSpace(connString.ProviderName))
            //        Database.AddFromConnectionString(connString);
        }

        private static DatabaseProvider defaultDbProvider = null;

        internal static DatabaseProvider DefaultDatabase
        {
            get
            {
                if (defaultDbProvider == null)
                    defaultDbProvider = Config.DatabaseProviders.Last().Value;

                return defaultDbProvider;
            }
            set
            {
                defaultDbProvider = value;
            }
        }

        internal static DatabaseProvider AddDatabase(DatabaseProvider database)
        {
            DatabaseProviders[database.Name] = database;

            return database;
        }

        internal static DatabaseProvider GetDatabase(string databaseName)
        {
            return DatabaseProviders[databaseName];
        }

        internal static List<DatabaseProvider> GetAllDatabases()
        {
            return DatabaseProviders.Values.ToList();
        }

        internal static void RemoveDatabase(string databaseName)
        {
            DatabaseProviders.Remove(databaseName);
        }

        internal static void RemoveAllDatabases()
        {
            DatabaseProviders.Clear();
        }

        //public static IDbConnection GetConnection(string databaseName)
        //{
        //    return DatabaseProviders[databaseName].GetConnection();
        //}
    }
}