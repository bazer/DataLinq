using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.SQLite
{
    public class SQLiteDatabaseCreator : IDatabaseProviderCreator
    {
        public bool IsDatabaseType(string typeName)
        {
            return typeName.Equals("sqlite", System.StringComparison.OrdinalIgnoreCase);
        }

        Database<T> IDatabaseProviderCreator.GetDatabaseProvider<T>(string connectionString, string databaseName)
        {
            return new SQLiteDatabase<T>(connectionString, databaseName);
        }
    }

    public class SQLiteDatabase<T>: Database<T>
         where T : class, IDatabaseModel
    {
        public SQLiteDatabase(string connectionString) : base(new SQLiteProvider<T>(connectionString))
        {
        }

        public SQLiteDatabase(string connectionString, string databaseName) : base(new SQLiteProvider<T>(connectionString, databaseName))
        {
        }
    }
}
