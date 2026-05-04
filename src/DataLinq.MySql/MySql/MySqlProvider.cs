using System;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Query;
using System.Diagnostics.CodeAnalysis;

namespace DataLinq.MySql;

public class MySQLProvider : IDatabaseProviderRegister
{
    public static bool HasBeenRegistered { get; private set; }

    public static void RegisterProvider()
    {
        if (HasBeenRegistered)
            return;

        var creator = new MySqlDatabaseCreator();
        var sqlFactory = new SqlFromMySqlFactory();
        var metadataFactory = new MetadataFromMySqlFactoryCreator();

        PluginHook.DatabaseProviders[DatabaseType.MySQL] = creator;
        PluginHook.SqlFromMetadataFactories[DatabaseType.MySQL] = sqlFactory;
        PluginHook.MetadataFromSqlFactories[DatabaseType.MySQL] = metadataFactory;

        HasBeenRegistered = true;
    }
}

public class MySqlProvider<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicProperties)]
    T> : SqlProvider<T> where T : class, IDatabaseModel, IDataLinqGeneratedDatabaseModel<T>
{
    static MySqlProvider()
    {
        MySQLProvider.RegisterProvider();
    }

    public MySqlProvider(string connectionString, string? databaseName = null, DataLinqLoggingConfiguration? loggerFactory = null)
        : base(connectionString, DatabaseType.MySQL, loggerFactory ?? DataLinqLoggingConfiguration.NullConfiguration, databaseName)
    {
    }
}
