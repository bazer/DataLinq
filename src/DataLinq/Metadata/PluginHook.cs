using DataLinq.Exceptions;
using DataLinq.Query;
using System.Collections.Generic;
using ThrowAway;

namespace DataLinq.Metadata
{
    //public enum PluginHookError
    //{
    //    NoHandlerForType
    //}
    public interface ISqlFromMetadataFactory
    {
        public Option<Sql, IDataLinqOptionFailure> GetCreateTables(DatabaseMetadata metadata, bool foreignKeyRestrict);
    }

    public interface IDatabaseCreator
    {
        Option<int, IDataLinqOptionFailure> CreateDatabase(Sql sql, string databaseNameOrFile, string connectionString, bool foreignKeyRestrict);
    }

    public static class PluginHook
    {
        public static Dictionary<DatabaseType, ISqlFromMetadataFactory> SqlGenerators = new();
        public static Dictionary<DatabaseType, IDatabaseCreator> DatabaseCreators = new();

        public static Option<int, IDataLinqOptionFailure> CreateDatabaseFromSql(this DatabaseType type, Sql sql, string databaseOrFile, string connectionString, bool foreignKeyRestrict)
        {
            if (!DatabaseCreators.ContainsKey(type))
                return new DataLinqOptionFailure<string>($"No creator for {type}");

            return DatabaseCreators[type].CreateDatabase(sql, databaseOrFile, connectionString, foreignKeyRestrict);
        }

        public static Option<int, IDataLinqOptionFailure> CreateDatabaseFromMetadata(this DatabaseType type, DatabaseMetadata metadata, string databaseNameOrFile, string connectionString, bool foreignKeyRestrict)
        {
            Sql sql = GenerateSql(type, metadata, foreignKeyRestrict);
            return CreateDatabaseFromSql(type, sql, databaseNameOrFile, connectionString, foreignKeyRestrict);
        }

        public static Option<Sql, IDataLinqOptionFailure> GenerateSql(this DatabaseType type, DatabaseMetadata metadata, bool foreignKeyRestrict)
        {
            if (!SqlGenerators.ContainsKey(type))
                return new DataLinqOptionFailure<string>($"No handler for {type}");

            return SqlGenerators[type].GetCreateTables(metadata, foreignKeyRestrict);
        }
    }
}