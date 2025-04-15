using System.Collections.Generic;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
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
    Option<Sql, IDLOptionFailure> GetCreateTables(DatabaseDefinition metadata, bool foreignKeyRestrict);
    Option<int, IDLOptionFailure> CreateDatabase(Sql sql, string databaseName, string connectionString, bool foreignKeyRestrict);
}

public interface IMetadataFromDatabaseFactoryCreator
{
    IMetadataFromSqlFactory GetMetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options);
}

public interface IMetadataFromSqlFactory
{
    Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString);
}

public static class PluginHook
{
    public static Dictionary<DatabaseType, IDatabaseProviderCreator> DatabaseProviders = new();
    public static Dictionary<DatabaseType, ISqlFromMetadataFactory> SqlFromMetadataFactories = new();
    public static Dictionary<DatabaseType, IMetadataFromDatabaseFactoryCreator> MetadataFromSqlFactories = new();

    public static Option<int, IDLOptionFailure> CreateDatabaseFromSql(this DatabaseType type, Sql sql, string databaseOrFile, string connectionString, bool foreignKeyRestrict)
    {
        if (!MetadataFromSqlFactories.ContainsKey(type))
            return new DLOptionFailure<string>($"No creator for {type}");

        return SqlFromMetadataFactories[type].CreateDatabase(sql, databaseOrFile, connectionString, foreignKeyRestrict);
    }

    public static Option<int, IDLOptionFailure> CreateDatabaseFromMetadata(this DatabaseType type, DatabaseDefinition metadata, string databaseNameOrFile, string connectionString, bool foreignKeyRestrict)
    {
        Sql sql = GenerateSql(type, metadata, foreignKeyRestrict);
        return CreateDatabaseFromSql(type, sql, databaseNameOrFile, connectionString, foreignKeyRestrict);
    }

    public static Option<Sql, IDLOptionFailure> GenerateSql(this DatabaseType type, DatabaseDefinition metadata, bool foreignKeyRestrict)
    {
        if (!SqlFromMetadataFactories.ContainsKey(type))
            return new DLOptionFailure<string>($"No handler for {type}");

        return SqlFromMetadataFactories[type].GetCreateTables(metadata, foreignKeyRestrict);
    }
}