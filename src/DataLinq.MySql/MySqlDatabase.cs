using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.MySql
{
    public class MySqlDatabaseCreator : IDatabaseProviderCreator
    {
        public bool IsDatabaseType(string typeName)
        {
            return typeName.Equals("mysql", System.StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("mariadb", System.StringComparison.OrdinalIgnoreCase);
        }

        Database<T> IDatabaseProviderCreator.GetDatabaseProvider<T>(string connectionString, string databaseName)
        {
            return new MySqlDatabase<T>(connectionString, databaseName);
        }
    }

    public class MySqlDatabase<T>: Database<T>
         where T : class, IDatabaseModel
    {
        public MySqlDatabase(string connectionString) : base(new MySQLProvider<T>(connectionString))
        {
        }

        public MySqlDatabase(string connectionString, string databaseName) : base(new MySQLProvider<T>(connectionString, databaseName))
        {
        }
    }
}
