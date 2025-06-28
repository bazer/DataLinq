using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DataLinq.Extensions.Helpers;
using ThrowAway;

namespace DataLinq.Config;

public record DataLinqConfig
{
    static public Option<DataLinqConfig> FindAndReadConfigs(string configPath, Action<string> log)
    {
        if (string.IsNullOrEmpty(configPath))
            throw new ArgumentNullException(nameof(configPath));

        if (!File.Exists(configPath))
        {
            if (Directory.Exists(configPath))
                configPath = Path.Combine(configPath, "datalinq.json");

            if (!File.Exists(configPath))
                return $"Couldn't find config file, usually called 'datalinq.json'. Tried searching path:\n{configPath}";
        }

        log($"Reading config from:      {configPath}");
        var config = ConfigReader.Read(configPath);
        if (config == null)
            return $"Couldn't parse config file {configPath}.";

        List<ConfigFile> configs = [config];

        var userFilePath = configPath.Replace(".json", ".user.json");
        if (File.Exists(userFilePath))
        {
            log($"Reading user config from: {userFilePath}");
            var userConfig = ConfigReader.Read(userFilePath);
            if (userConfig == null)
                return $"Couldn't parse config file {userFilePath}.";

            configs.Add(userConfig);
        }

        var basePath = Path.GetDirectoryName(configPath);

        if (basePath == null)
            return $"Couldn't get directory name of path '{configPath}'";

        return new DataLinqConfig(basePath, configs.ToArray());
    }

    public List<DataLinqDatabaseConfig> Databases { get; }
    public string BasePath { get; }

    public DataLinqConfig(string basePath, params ConfigFile[] configFiles)
    {
        if (configFiles.Length == 0)
            throw new ArgumentException("At least one config file must be specified");

        if (basePath == null || !Directory.Exists(basePath))
            throw new ArgumentNullException(nameof(basePath), "Must be a valid directory path");

        BasePath = basePath;
        Databases = configFiles
            .First()
            .Databases
            .Select(x => new DataLinqDatabaseConfig(this, x))
            .ToList();

        foreach (var configFile in configFiles.Skip(1))
        {
            foreach (var database in configFile.Databases)
            {
                var existingDatabase = Databases.SingleOrDefault(x => x.Name == database.Name);
                if (existingDatabase == null)
                {
                    Databases.Add(new DataLinqDatabaseConfig(this, database));
                }
                else
                {
                    existingDatabase.MergeConfig(database);
                }
            }
        }
    }

    public Option<(DataLinqDatabaseConfig db, DataLinqDatabaseConnection connection)> GetConnection(string? dbName, DatabaseType databaseType)
    {
        if (string.IsNullOrEmpty(dbName) && Databases.Count != 1)
            return $"The config file has more than one database specified. Use (-a or --all) to use all configured databases, or name (-n or --name) one:\n{Databases.Select(x => x.Name).ToJoinedString()}";


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

        if (db.Connections.Count > 1 && databaseType == DatabaseType.Unknown)
        {
            return $"Database '{db.Name}' has more than one type of connection to read from, you need to select which one (-t or --type):\n{db.Connections.Select(x => x.Type).ToJoinedString()}";
        }

        DataLinqDatabaseConnection? connection = null;
        if (databaseType != DatabaseType.Unknown)
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

    public string Name { get; private set; }
    public string CsType { get; private set; }
    public string Namespace { get; private set; }
    public List<string> SourceDirectories { get; private set; }
    public string? DestinationDirectory { get; private set; }
    public List<string> Include { get; private set; }
    //public bool UseCache { get; }
    public bool UseRecord { get; private set; }
    public bool UseFileScopedNamespaces { get; private set; }
    public bool UseNullableReferenceTypes { get; private set; }
    public bool CapitalizeNames { get; private set; }
    public bool RemoveInterfacePrefix { get; private set; }
    public bool SeparateTablesAndViews { get; private set; }
    public List<DataLinqDatabaseConnection> Connections { get; private set; } = new();
    public Encoding FileEncoding { get; private set; }

    public DataLinqDatabaseConfig(DataLinqConfig config, ConfigFileDatabase database)
    {
        Config = config;
        Name = database.Name ?? throw new ArgumentNullException(nameof(database.Name));
        CsType = database.CsType ?? database.Name;
        Namespace = database.Namespace ?? "Models";
        SourceDirectories = database.SourceDirectories ?? new List<string>();
        DestinationDirectory = database.DestinationDirectory;
        Include = database.Include ?? new List<string>();
        UseRecord = database.UseRecord ?? false;
        UseFileScopedNamespaces = database.UseFileScopedNamespaces ?? false;
        UseNullableReferenceTypes = database.UseNullableReferenceTypes ?? false;
        CapitalizeNames = database.CapitalizeNames ?? false;
        RemoveInterfacePrefix = database.RemoveInterfacePrefix ?? true;
        SeparateTablesAndViews = database.SeparateTablesAndViews ?? false;
        Connections = database.Connections.Select(x => new DataLinqDatabaseConnection(this, x)).ToList();
        FileEncoding = ConfigReader.ParseFileEncoding(database.FileEncoding);
    }

    public void MergeConfig(ConfigFileDatabase database)
    {
        if (database.Name != null)
            Name = database.Name;

        if (database.CsType != null)
            CsType = database.CsType;

        if (database.Namespace != null)
            Namespace = database.Namespace;

        if (database.SourceDirectories != null)
            SourceDirectories = database.SourceDirectories;

        if (database.DestinationDirectory != null)
            DestinationDirectory = database.DestinationDirectory;

        if (database.Include != null)
            Include = database.Include;

        if (database.UseRecord != null)
            UseRecord = database.UseRecord.Value;

        if (database.UseFileScopedNamespaces != null)
            UseFileScopedNamespaces = database.UseFileScopedNamespaces.Value;

        if (database.UseNullableReferenceTypes != null)
            UseNullableReferenceTypes = database.UseNullableReferenceTypes.Value;

        if (database.CapitalizeNames != null)
            CapitalizeNames = database.CapitalizeNames.Value;

        if (database.RemoveInterfacePrefix != null)
            RemoveInterfacePrefix = database.RemoveInterfacePrefix.Value;

        if (database.SeparateTablesAndViews != null)
            SeparateTablesAndViews = database.SeparateTablesAndViews.Value;

        if (database.Connections != null)
            Connections = database.Connections.Select(x => new DataLinqDatabaseConnection(this, x)).ToList();

        if (database.FileEncoding != null)
            FileEncoding = ConfigReader.ParseFileEncoding(database.FileEncoding);
    }
}

public record DataLinqDatabaseConnection
{
    public DataLinqDatabaseConfig DatabaseConfig { get; }

    public DatabaseType Type { get; }
    public string DataSourceName { get; }
    public DataLinqConnectionString ConnectionString { get; }

    public string GetRootedPath(string basePath)
    {
        if (Path.IsPathRooted(DataSourceName))
            return DataSourceName;
        else if (Path.IsPathRooted(ConnectionString.Path))
            return ConnectionString.Path;

        return Path.Combine(basePath, DataSourceName);
    }

    public DataLinqDatabaseConnection(DataLinqDatabaseConfig databaseConfig, ConfigFileDatabaseConnection connection)
    {
        DatabaseConfig = databaseConfig;
        DataSourceName = connection.DataSourceName ?? connection.DatabaseName ?? throw new ArgumentNullException(nameof(connection.DataSourceName));
        Type = ConfigReader.ParseDatabaseType(connection.Type); // ?? throw new ArgumentException($"Couldn't find database type for '{connection.Type}'");
        ConnectionString = new DataLinqConnectionString(connection.ConnectionString);
    }
}
