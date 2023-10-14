using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ThrowAway;

namespace DataLinq.Config
{
    public record DataLinqConfig
    {
        public List<DataLinqDatabaseConfig> Databases { get; set; }

        public DataLinqConfig(ConfigFile configFile)
        {
            Databases = configFile.Databases.Select(x => new DataLinqDatabaseConfig(this, x)).ToList();
        }

        public Option<(DataLinqDatabaseConfig db, DataLinqDatabaseConnection connection)> GetConnection(string? dbName, DatabaseType? databaseType)
        {
            if (string.IsNullOrEmpty(dbName) && Databases.Count != 1)
                return $"The config file has more than one database specified, you need to select which one to use";
            
            var db = string.IsNullOrEmpty(dbName)
                ? Databases.Single()
                : Databases.SingleOrDefault(x => x.Name.ToLower() == dbName.ToLower());

            if (db == null)
            {
                return $"Couldn't find database with name '{dbName}'";
            }

            if (db.Connections.Count == 0)
            {
                return $"Database '{db.Name}' has no connections to read from";
            }

            if (db.Connections.Count > 1 && databaseType == null)
            {
                return $"Database '{db.Name}' has more than one type of connection to read from, you need to select which one (-t or --type)";
            }

            DataLinqDatabaseConnection connection = null;
            if (databaseType != null)
            {
                connection = db.Connections.SingleOrDefault(x => x.Type == databaseType);

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

    public record DataLinqDatabaseConfig
    {
        public DataLinqConfig Config { get; }

        public string Name { get; set; }
        public string CsType { get; set; }
        public string Namespace { get; set; }
        public List<string> SourceDirectories { get; set; }
        public string? DestinationDirectory { get; set; }
        public List<string> Tables { get; set; }
        public List<string> Views { get; set; }
        public bool UseCache { get; set; }
        public bool UseRecord { get; set; }
        public bool UseFileScopedNamespaces { get; set; }
        public bool CapitalizeNames { get; set; }
        public bool RemoveInterfacePrefix { get; set; }
        public bool SeparateTablesAndViews { get; set; }
        public List<DataLinqDatabaseConnection> Connections { get; set; } = new();
        public Encoding FileEncoding { get; }

        public DataLinqDatabaseConfig(DataLinqConfig config, ConfigFileDatabase database)
        {
            Config = config;
            Name = database.Name ?? throw new ArgumentNullException(nameof(database.Name));
            CsType = database.CsType ?? database.Name;
            Namespace = database.Namespace ?? "Models";
            SourceDirectories = database.SourceDirectories ?? new List<string>();
            DestinationDirectory = database.DestinationDirectory;
            Tables = database.Tables ?? new List<string>();
            Views = database.Views ?? new List<string>();
            UseCache = database.UseCache ?? false;
            UseRecord = database.UseRecord ?? false;
            UseFileScopedNamespaces = database.UseFileScopedNamespaces ?? false;
            CapitalizeNames = database.CapitalizeNames ?? false;
            RemoveInterfacePrefix = database.RemoveInterfacePrefix ?? true;
            SeparateTablesAndViews = database.SeparateTablesAndViews ?? false;
            Connections = database.Connections.Select(x => new DataLinqDatabaseConnection(this, x)).ToList();
            FileEncoding = ConfigReader.ParseFileEncoding(database.FileEncoding);
        }
    }

    public record DataLinqDatabaseConnection
    {
        public DataLinqDatabaseConfig DatabaseConfig { get; }

        public DatabaseType Type { get; }
        public string DatabaseName { get; }
        public DataLinqConnectionString ConnectionString { get; }

        public string GetRootedPath(string basePath)
        {
            if (Path.IsPathRooted(DatabaseName))
                return DatabaseName;
            else if (Path.IsPathRooted(ConnectionString.Path))
                return ConnectionString.Path;

            return Path.Combine(basePath, DatabaseName);
        }

        public DataLinqDatabaseConnection(DataLinqDatabaseConfig databaseConfig, ConfigFileDatabaseConnection connection)
        {
            DatabaseConfig = databaseConfig;
            DatabaseName = connection.DatabaseName ?? throw new ArgumentNullException(nameof(connection.DatabaseName));
            Type = ConfigReader.ParseDatabaseType(connection.Type) ?? throw new ArgumentException($"Couldn't find database type for '{connection.Type}'");
            ConnectionString = new DataLinqConnectionString(connection.ConnectionString);
        }
    }
}
