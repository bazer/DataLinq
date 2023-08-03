using DataLinq.Metadata;
using System.IO;
using System.Linq;
using ThrowAway;

namespace DataLinq.Config
{
    public static class ConfigReader
    {
        public static Option<DatabaseType> ParseDatabaseType(string typeName)
        {
            foreach (var (type, provider) in PluginHook.DatabaseProviders)
            {
                if (provider.IsDatabaseType(typeName))
                    return type;
            }

            return $"No provider matched with database type '{typeName}'";
        }

        public static ConfigFile Read(string path)
        {
            var file = File.ReadAllText(path);
            var config = System.Text.Json.JsonSerializer.Deserialize<ConfigFile>(file);

            return config;
        }

        public static Option<(DatabaseConfig db, DatabaseConnectionConfig connection)> GetConnection(this ConfigFile configFile, string dbName, DatabaseType databaseType)
        {
            var db = configFile.Databases.SingleOrDefault(x => x.Name.ToLower() == dbName.ToLower());
            if (db == null)
            {
                return $"Couldn't find database with name '{dbName}'";
            }

            if (db.Connections.Count == 0)
            {
                return $"Database '{dbName}' has no connections to read from";
            }

            if (db.Connections.Count > 1 && databaseType == null)
            {
                return $"Database '{dbName}' has more than one connection to read from, you need to select which one";
            }

            DatabaseConnectionConfig connection = null;
            if (databaseType != null)
            {
                connection = db.Connections.SingleOrDefault(x => x.ParsedType == databaseType);

                if (connection == null)
                {
                    return $"Couldn't find connection with type '{databaseType}' in configuration file.";
                }
            }

            if (connection == null)
                connection = db.Connections[0];

            return (db, connection);
        }
    }
}
