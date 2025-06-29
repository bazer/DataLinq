using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;

namespace DataLinq.MySql;

public class MySQLProvider : IDatabaseProviderRegister
{
    public static bool HasBeenRegistered { get; private set; }

    public static void RegisterProvider()
    {
        if (HasBeenRegistered)
            return;

        var creator = new MySqlDatabaseCreator();
        var sqlFactory = new SqlFromMysqlFactory();
        var metadataFactory = new MetadataFromMySqlFactoryCreator();

        PluginHook.DatabaseProviders[DatabaseType.MySQL] = creator;
        PluginHook.SqlFromMetadataFactories[DatabaseType.MySQL] = sqlFactory;
        PluginHook.MetadataFromSqlFactories[DatabaseType.MySQL] = metadataFactory;

        HasBeenRegistered = true;
    }
}

public class MySqlProvider<T> : SqlProvider<T> where T : class, IDatabaseModel
{
    static MySqlProvider()
    {
        MariaDBProvider.RegisterProvider();
    }

    public MySqlProvider(string connectionString, string? databaseName = null, DataLinqLoggingConfiguration? loggerFactory = null)
        : base(connectionString, DatabaseType.MySQL, loggerFactory ?? DataLinqLoggingConfiguration.NullConfiguration, databaseName)
    {
    }
}