using DataLinq.Query;
using System.Collections.Generic;

namespace DataLinq.Metadata
{
    public interface IDatabaseCreator
    {
        bool CreateDatabase(Sql sql, string databaseNameOrFile, string connectionString, bool foreignKeyRestrict);
    }

    public static class DatabaseFactory
    {
        public enum DatabaseType
        {
            MySQL,
            SQLite
        }

        public static Dictionary<DatabaseType, ISqlFromMetadataFactory> SqlGenerators = new();
        public static Dictionary<DatabaseType, IDatabaseCreator> DbCreators = new();

        public static bool CreateDatabaseFromSql(DatabaseType type, Sql sql, string databaseOrFile, string connectionString, bool foreignKeyRestrict)
        {
            if (!DbCreators.ContainsKey(type))
                throw new System.Exception($"No creator for {type}");
            return DbCreators[type].CreateDatabase(sql, databaseOrFile, connectionString, foreignKeyRestrict);
        }

        public static bool CreateDatabaseFromMetadata(DatabaseType type, DatabaseMetadata metadata, string databaseNameOrFile, string connectionString, bool foreignKeyRestrict)
        {
            if (!SqlGenerators.ContainsKey(type))
                throw new System.Exception($"No handler for {type}");
            var sql = SqlGenerators[type].GenerateSql(metadata, foreignKeyRestrict);
            return CreateDatabaseFromSql(type, sql, databaseNameOrFile, connectionString, foreignKeyRestrict);
        }
    }
}