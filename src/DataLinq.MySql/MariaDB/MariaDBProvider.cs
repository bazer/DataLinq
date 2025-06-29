using System;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.MySql;

namespace DataLinq.MariaDB;

public class MariaDBProvider : IDatabaseProviderRegister
{
    public static bool HasBeenRegistered { get; private set; }

    public static void RegisterProvider()
    {
        if (HasBeenRegistered)
            return;

        var creator = new MariaDBDatabaseCreator();
        var sqlFactory = new SqlFromMariaDBFactory();
        var metadataFactory = new MetadataFromMariaDBFactoryCreator();

        PluginHook.DatabaseProviders[DatabaseType.MariaDB] = creator;
        PluginHook.SqlFromMetadataFactories[DatabaseType.MariaDB] = sqlFactory;
        PluginHook.MetadataFromSqlFactories[DatabaseType.MariaDB] = metadataFactory;

        HasBeenRegistered = true;
    }
}

public class MariaDBProvider<T> : SqlProvider<T> where T : class, IDatabaseModel
{
    public bool IsMariaDbUuidSupported { get; private set; }

    static MariaDBProvider()
    {
        MariaDBProvider.RegisterProvider();
    }

    public MariaDBProvider(string connectionString, string? databaseName = null, DataLinqLoggingConfiguration? loggerFactory = null)
        : base(connectionString, DatabaseType.MariaDB, loggerFactory ?? DataLinqLoggingConfiguration.NullConfiguration, databaseName)
    {
        DetectServerVersion();
    }

    private void DetectServerVersion()
    {
        try
        {
            var versionString = DatabaseAccess.ExecuteScalar<string>("SELECT @@version");
            if (versionString != null && versionString.Contains("MariaDB"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(versionString, @"(\d+\.\d+(\.\d+)?)");
                if (match.Success && Version.TryParse(match.Groups[1].Value, out var version))
                {
                    if (version >= new Version(10, 7))
                    {
                        IsMariaDbUuidSupported = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log this exception
            System.Console.WriteLine($"Could not detect MariaDB version. Assuming no special features are supported. Error: {ex.Message}");
            IsMariaDbUuidSupported = false;
        }
    }
}