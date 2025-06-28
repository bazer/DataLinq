using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;

namespace DataLinq.MySql;

public class MariaDBProvider : IDatabaseProviderRegister
{
    public static bool HasBeenRegistered { get; private set; }

    public static void RegisterProvider()
    {
        if (HasBeenRegistered)
            return;

        // Ensure MySQL provider is also registered as MariaDB might inherit from it
        if (!MySQLProvider.HasBeenRegistered)
            MySQLProvider.RegisterProvider();

        var creator = new MariaDBDatabaseCreator();
        // Use the new MariaDB-specific factories
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
        : base(connectionString, databaseName, loggerFactory ?? DataLinqLoggingConfiguration.NullConfiguration)
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
                // Simple version check for 10.7+
                // Example version string: 10.7.3-MariaDB-1:10.7.3+maria~focal
                var match = System.Text.RegularExpressions.Regex.Match(versionString, @"(\d+\.\d+)");
                if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double version))
                {
                    if (version >= 10.7)
                    {
                        IsMariaDbUuidSupported = true;
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            // Log this exception
            System.Console.WriteLine($"Could not detect MariaDB version. Assuming no special features are supported. Error: {ex.Message}");
            IsMariaDbUuidSupported = false;
        }
    }
}