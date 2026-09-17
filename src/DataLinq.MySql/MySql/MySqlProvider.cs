using System;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.MySql;

public class MySQLProvider : IDatabaseProviderRegister
{
    public static bool HasBeenRegistered => PluginHook.IsRegistered(DatabaseType.MySQL);

    public static void RegisterProvider()
    {
        if (HasBeenRegistered)
            return;

        var creator = new MySqlDatabaseCreator();
        var sqlFactory = new SqlFromMySqlFactory();
        var metadataFactory = new MetadataFromMySqlFactoryCreator();

        PluginHook.RegisterProvider(DatabaseType.MySQL, creator, sqlFactory, metadataFactory);
    }
}

public class MySqlProvider<T> : SqlProvider<T> where T : class, IDatabaseModel<T>
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
