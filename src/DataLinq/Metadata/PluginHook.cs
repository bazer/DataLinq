using System.Collections.Generic;
using DataLinq.Exceptions;
using DataLinq.Interfaces;
using DataLinq.Query;
using Microsoft.Extensions.Logging;
using ThrowAway;

namespace DataLinq.Metadata;

public interface IDatabaseProviderCreator
{
    Database<T> GetDatabaseProvider<T>(string connectionString, string databaseName) where T : class, IDatabaseModel;
    bool IsDatabaseType(string typeName);
    IDatabaseProviderCreator UseLoggerFactory(ILoggerFactory? loggerFactory);
}

public interface ISqlFromMetadataFactory
{
    Option<Sql, IDataLinqOptionFailure> GetCreateTables(DatabaseMetadata metadata, bool foreignKeyRestrict);
    Option<int, IDataLinqOptionFailure> CreateDatabase(Sql sql, string databaseName, string connectionString, bool foreignKeyRestrict);
}

public interface IMetadataFromDatabaseFactoryCreator
{
    IMetadataFromSqlFactory GetMetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options);
}

public interface IMetadataFromSqlFactory
{
    Option<DatabaseMetadata> ParseDatabase(string name, string csTypeName, string dbName, string connectionString);
}

public static class PluginHook
{
    public static Dictionary<DatabaseType, IDatabaseProviderCreator> DatabaseProviders = new();
    public static Dictionary<DatabaseType, ISqlFromMetadataFactory> SqlFromMetadataFactories = new();
    public static Dictionary<DatabaseType, IMetadataFromDatabaseFactoryCreator> MetadataFromSqlFactories = new();

    public static Option<int, IDataLinqOptionFailure> CreateDatabaseFromSql(this DatabaseType type, Sql sql, string databaseOrFile, string connectionString, bool foreignKeyRestrict)
    {
        if (!MetadataFromSqlFactories.ContainsKey(type))
            return new DataLinqOptionFailure<string>($"No creator for {type}");

        return SqlFromMetadataFactories[type].CreateDatabase(sql, databaseOrFile, connectionString, foreignKeyRestrict);
    }

    public static Option<int, IDataLinqOptionFailure> CreateDatabaseFromMetadata(this DatabaseType type, DatabaseMetadata metadata, string databaseNameOrFile, string connectionString, bool foreignKeyRestrict)
    {
        Sql sql = GenerateSql(type, metadata, foreignKeyRestrict);
        return CreateDatabaseFromSql(type, sql, databaseNameOrFile, connectionString, foreignKeyRestrict);
    }

    public static Option<Sql, IDataLinqOptionFailure> GenerateSql(this DatabaseType type, DatabaseMetadata metadata, bool foreignKeyRestrict)
    {
        if (!SqlFromMetadataFactories.ContainsKey(type))
            return new DataLinqOptionFailure<string>($"No handler for {type}");

        return SqlFromMetadataFactories[type].GetCreateTables(metadata, foreignKeyRestrict);
    }
}